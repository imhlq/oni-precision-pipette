using System;
using UnityEngine;
using HarmonyLib;

namespace OniPrecision
{
    public sealed partial class PrecisionMod
    {
        private const string TemperatureOptionKey = "Richard.OniPrecision.UnlimitedTemperatureMass";

        private static void AddTemperatureOption(System.Collections.IList ___displayedMods)
        {
            foreach (var entry in ___displayedMods)
            {
                var type = entry.GetType();
                int index = (int)AccessTools.Field(type, "mod_index").GetValue(entry);
                if (Global.Instance.modManager.mods[index].staticID != "Richard.OniPrecision") continue;
                var row = (RectTransform)AccessTools.Field(type, "rect_transform").GetValue(entry);
                var manage = row.GetComponent<HierarchyReferences>().GetReference<KButton>("ManageButton");
                var option = UnityEngine.Object.Instantiate(manage, manage.transform.parent);
                option.transform.SetSiblingIndex(manage.transform.GetSiblingIndex());
                option.isInteractable = true;
                var label = option.GetComponentInChildren<LocText>();
                bool chinese = Localization.GetCurrentLanguageCode()?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
                void Refresh()
                {
                    label.text = chinese ? "解除温控限重：" + (FluidPipette.UnlimitedTemperatureMass ? "开" : "关")
                        : "Unlimited temp. mass: " + (FluidPipette.UnlimitedTemperatureMass ? "On" : "Off");
                }
                option.GetComponent<ToolTip>().toolTip = chinese ? "允许为超过 20 g 的样本指定温度。仍需完成温度调节研究。"
                    : "Allow temperature adjustment above 20 g. Temperature Modulation research is still required.";
                option.onClick += () => {
                    FluidPipette.UnlimitedTemperatureMass = !FluidPipette.UnlimitedTemperatureMass;
                    PlayerPrefs.SetInt(TemperatureOptionKey, FluidPipette.UnlimitedTemperatureMass ? 1 : 0);
                    PlayerPrefs.Save();
                    Refresh();
                };
                Refresh();
                break;
            }
        }

        static partial void InstallZoomFilter(Harmony harmony)
        {
            FluidPipette.UnlimitedTemperatureMass = PlayerPrefs.GetInt(TemperatureOptionKey, 0) != 0;
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ModsScreen), "BuildDisplay"),
                postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(AddTemperatureOption)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(FallMonitor.Instance), nameof(FallMonitor.Instance.UpdateFalling)),
                prefix: new HarmonyMethod(typeof(DuplicantGrabTool), nameof(DuplicantGrabTool.AllowFallUpdate)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Db),"Initialize"),
                postfix:new HarmonyMethod(typeof(FluidPipette),nameof(FluidPipette.RegisterResearch)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(HoverTextDrawer), nameof(HoverTextDrawer.EndDrawing), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(MapPipetteTool), nameof(MapPipetteTool.AppendHeldSample)));
            var input = AccessTools.DeclaredMethod(typeof(CameraController), "OnKeyDown", new[] { typeof(KButtonEvent) });
            if (input != null) harmony.Patch(input, prefix: new HarmonyMethod(typeof(FluidPipette), nameof(FluidPipette.FilterZoom)));
        }
    }

    // PlayerController routes world clicks to its active InterfaceTool.
    public sealed class MapPipetteTool : InterfaceTool
    {
        internal FluidPipette Owner;
        internal InterfaceTool Previous;
        private const float HoldSeconds = .65f;
        private const int Steps = 16;
        private readonly Texture2D[,] frames = new Texture2D[2, Steps + 1 + 12];
        private int heldCell = -1;
        private float started, flashUntil;
        private bool releasing;
        private SelectToolHoverTextCard hoverStyle;
        private static readonly float[] ShakeAngles = { -8, 6, -4, 3, -1.5f, .8f };

        private int previewCell = -1, previewWorld = -1;
        private float refreshAt;
        private bool canPick, canDrop;
        private string heldCaption;
        private readonly System.Collections.Generic.List<KSelectable> hoverHits = new System.Collections.Generic.List<KSelectable>();
        private static MapPipetteTool drawingTargetCard;

        internal static void AppendHeldSample(HoverTextDrawer __instance)
        {
            var tool = drawingTargetCard;
            if (tool == null || tool.heldCaption == null) return;
            __instance.BeginShadowBar();
            __instance.DrawText(tool.heldCaption, tool.hoverStyle.Styles_BodyText.Standard);
            __instance.EndShadowBar();
        }

        private void Preview(int cell, bool force = false)
        {
            int world = ClusterManager.Instance == null ? -1 : ClusterManager.Instance.activeWorldId;
            if (!force && cell == previewCell && world == previewWorld && Time.unscaledTime < refreshAt) return;
            previewCell = cell;
            previewWorld = world;
            refreshAt = Time.unscaledTime + .2f;
            canPick = Owner.CanPick(cell, out _, out _, out _);
            canDrop = Owner.HasMapDose && Owner.CanDrop(cell);
            heldCaption = Owner.HasMapDose ? Owner.SampleCaption + (canDrop ? PipetteText.Get(" · Can dispense", " · 可放置") : PipetteText.Get(" · Cannot dispense", " · 不可放置")) : null;
        }

        internal void InvalidatePreview() => refreshAt = 0;

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (Owner == null || Owner.OverParameters() || !hasFocus || !ShowHoverUI() || HoverTextScreen.Instance == null) return;
            if (hoverStyle == null && SelectTool.Instance != null)
                hoverStyle = SelectTool.Instance.GetComponent<SelectToolHoverTextCard>();
            if (hoverStyle == null) return;
            int cell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            Preview(cell);
            if ((!canPick && heldCaption == null) || !Grid.IsValidCell(cell))
            {
                HoverTextScreen.Instance.BeginDrawing().EndDrawing();
                return;
            }

            hoverHits.Clear();
            GetSelectablesUnderCursor(hoverHits);
            // The stock card owns the drawing pass. Append the pipette receipt before
            // it closes the pass so a second BeginDrawing cannot erase the stock card.
            drawingTargetCard = this;
            try { hoverStyle.UpdateHoverElements(hoverHits); }
            finally { drawingTargetCard = null; }
        }

        private Texture2D Frame(bool gas, int index)
        {
            int row = gas ? 1 : 0;
            if (frames[row, index] != null) return frames[row, index];
            const int size = 48;
            string asset = gas ? "YAPP.gas.rgba" : "YAPP.liquid.rgba";
            byte[] rgba;
            using (var stream = typeof(MapPipetteTool).Assembly.GetManifestResourceStream(asset))
            using (var reader = new System.IO.BinaryReader(stream))
                rgba = reader.ReadBytes(size * size * 4);
            bool flash = index > Steps;
            int level = flash ? (index >= Steps + 1 + ShakeAngles.Length ? Steps : 0) : index;
            float edge = 9 + 40f * level / Steps;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int offset = 4 * (y * size + x);
                if (rgba[offset + 3] == 0) continue;
                int along = x + y;
                if (level > 0 && along >= 9 && along <= edge && rgba[offset] > 90 &&
                    rgba[offset + 1] > 100 && rgba[offset + 2] > 100)
                {
                    rgba[offset] = gas ? (byte)238 : (byte)32;
                    rgba[offset + 1] = gas ? (byte)175 : (byte)171;
                    rgba[offset + 2] = gas ? (byte)65 : (byte)236;
                }
                if (flash)
                    for (int c = 0; c < 3; c++) rgba[offset+c] += (byte)((255-rgba[offset+c]) / 2);
            }
            if (flash)
            {
                float angle = ShakeAngles[(index - Steps - 1) % ShakeAngles.Length] * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                int tipX = gas ? 3 : 5, tipY = gas ? 2 : 4;
                var rotated = new byte[rgba.Length];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int sx = tipX + Mathf.RoundToInt((x-tipX)*cos + (y-tipY)*sin);
                    int sy = tipY + Mathf.RoundToInt((y-tipY)*cos - (x-tipX)*sin);
                    if ((uint)sx < size && (uint)sy < size)
                        Array.Copy(rgba, 4*(sy*size+sx), rotated, 4*(y*size+x), 4);
                }
                rgba = rotated;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, false);
            return frames[row, index] = texture;
        }

        private bool panelCursor;
        private Texture2D selectionCursor;

        private bool UsePanelCursor()
        {
            if (!Owner.OverParameters()) return false;
            CancelHold();
            if (!panelCursor)
            {
                if (selectionCursor != null) SetCursor(selectionCursor, SelectTool.Instance.cursorOffset, CursorMode.Auto);
                else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                panelCursor = true;
            }
            return true;
        }

        private void ShowFrame(int level)
        {
            bool gas = Owner.GasCursor;
            var frame = Frame(gas, level);
            var offset = gas ? new Vector2(3,46) : new Vector2(5,44);
            if (panelCursor && selectionCursor == null) Cursor.SetCursor(frame, offset, CursorMode.Auto);
            panelCursor = false;
            SetCursor(frame, offset, CursorMode.Auto);
        }

        internal void RefreshCursor()
        {
            if (PlayerController.Instance == null || PlayerController.Instance.ActiveTool != this || UsePanelCursor()) return;
            int level = RestLevel();
            float remaining = flashUntil - Time.unscaledTime;
            if (remaining > 0 && (level == Steps || level == 0))
            {
                int pose = Mathf.Clamp((int)((.30f - remaining) / .05f), 0, ShakeAngles.Length-1);
                level = Steps + 1 + pose + (level == Steps ? ShakeAngles.Length : 0);
            }
            ShowFrame(level);
        }

        private int RestLevel() => !Owner.HasMapDose ? 0 : Owner.FullDose ? Steps : Steps/2;

        internal void ConfirmTransfer(float before, bool drop)
        {
            InvalidatePreview();
            if (drop ? before > 0 && !Owner.HasMapDose : Owner.StoredMass > before && Owner.FullDose)
                flashUntil = Time.unscaledTime + .30f;
            RefreshCursor();
        }

        internal bool Holding => heldCell >= 0;
        internal void CancelHold() { heldCell = -1; }

        internal void AdvanceHold()
        {
            if (UsePanelCursor()) return;
            if (Owner.AutoPipetteActive && heldCell < 0 && hasFocus && Application.isFocused)
            {
                // Alt-modified clicks may not be dispatched as Action.MouseLeft/MouseRight.
                if (Input.GetMouseButtonDown(0)) BeginHold(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()), false);
                else if (Input.GetMouseButtonDown(1)) BeginHold(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()), true);
            }
            if (heldCell < 0) { RefreshCursor(); return; }
            int hovered = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            Preview(hovered);
            if (!Input.GetMouseButton(releasing ? 1 : 0) || !Application.isFocused || hovered != heldCell ||
                !Owner.CanHold || (releasing ? !canDrop : !canPick) || (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
            { CancelHold(); RefreshCursor(); return; }
            float progress = (Time.unscaledTime - started) / HoldSeconds;
            if (progress >= 1)
            {
                int cell = heldCell;
                CancelHold();
                Owner.ClickMap(cell, releasing);
                RefreshCursor();
                return;
            }
            int step = Mathf.Clamp((int)(progress * Steps), 0, Steps - 1);
            int fill = RestLevel();
            ShowFrame(Mathf.RoundToInt(Mathf.Lerp(fill, releasing ? 0 : Steps, step / (float)Steps)));
        }

        protected override void OnActivateTool()
        {
            base.OnActivateTool();
            selectionCursor = SelectTool.Instance == null ? null :
                AccessTools.Field(typeof(InterfaceTool), "cursor").GetValue(SelectTool.Instance) as Texture2D;
            RefreshCursor();
        }

        protected override void OnDeactivateTool(InterfaceTool next)
        {
            CancelHold();
            flashUntil = 0;
            Owner.LeaveCursorView();
            base.OnDeactivateTool(next);
        }

        private void BeginHold(Vector3 position, bool dispense)
        {
            if (heldCell >= 0 || !Owner.CanHold || Owner.OverParameters()) return;
            int cell = Grid.PosToCell(position);
            Preview(cell, force: true);
            if (dispense ? !canDrop : !canPick) return;
            releasing = dispense;
            heldCell = cell;
            started = Time.unscaledTime;
        }

        public override void OnLeftClickDown(Vector3 position) => BeginHold(position, false);
        public override void OnLeftClickUp(Vector3 position) { if (!releasing) CancelHold(); }
        public override void OnRightClickDown(Vector3 position, KButtonEvent e)
        {
            e.TryConsume(Action.MouseRight);
            BeginHold(position, true);
        }
        public override void OnRightClickUp(Vector3 position) { if (releasing) CancelHold(); }

        protected override void OnCleanUp()
        {
            foreach (var texture in frames) if (texture != null) Destroy(texture);
            base.OnCleanUp();
        }
    }

    public sealed partial class FluidPipette
    {
        private static Sprite temperatureIcon;

        private static Sprite TemperatureIcon()
        {
            if (temperatureIcon != null) return temperatureIcon;
            const int size = 96;
            using (var stream = typeof(FluidPipette).Assembly.GetManifestResourceStream("YAPP.temperature.rgba"))
            using (var reader = new System.IO.BinaryReader(stream))
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(reader.ReadBytes(size * size * 4));
                texture.Apply();
                temperatureIcon = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
                return temperatureIcon;
            }
        }

        internal static void RegisterResearch()
        {
            PipetteText.Chinese = Localization.GetCurrentLanguageCode()?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
            const string id = "YAPP.TemperatureControl";
            var db = Db.Get();
            var tech = db.Techs.TryGet(TechId);
            if (tech == null || db.TechItems.TryGet(id) != null) return;
            db.TechItems.AddTechItem(id, PipetteText.Get("Pipette temperature control", "滴管温度控制"),
                PipetteText.Get("Set the delivery temperature for samples.", "可指定样本的放置温度。"),
                (a, b) => TemperatureIcon());
            tech.AddUnlockedItemIDs(id);
        }

        private MapPipetteTool mapTool;
        private bool cursorPipe;
        private FluidPhase cursorPhase = FluidPhase.Liquid;
        private HashedString priorView, appliedView;
        private bool ownsView;

        private void ChangeView(bool pipe)
        {
            mapTool?.InvalidatePreview();
            mapTool?.CancelHold();
            cursorPipe = pipe;
            var overlay = OverlayScreen.Instance;
            if (overlay == null) return;
            appliedView = pipe ? (cursorPhase == FluidPhase.Gas ? OverlayModes.GasConduits.ID :
                OverlayModes.LiquidConduits.ID) : OverlayModes.None.ID;
            if (overlay.mode != appliedView) overlay.ToggleOverlay(appliedView);
            ownsView = true;
        }

        internal void LeaveCursorView()
        {
            autoPrevious = null;
            HideParameters();
            var overlay = OverlayScreen.Instance;
            if (ownsView && overlay != null && overlay.mode == appliedView && overlay.mode != priorView)
                overlay.ToggleOverlay(priorView);
            ownsView = false;
        }

        private void CursorInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && mapTool.Previous != null)
            {
                PlayerController.Instance.ActivateTool(mapTool.Previous);
                return;
            }
            var overlay = OverlayScreen.Instance;
            if (overlay != null && overlay.mode != appliedView)
            {
                mapTool.InvalidatePreview();
                mapTool.CancelHold();
                appliedView = overlay.mode;
                ownsView = false;
                cursorPipe = overlay.mode == OverlayModes.GasConduits.ID || overlay.mode == OverlayModes.LiquidConduits.ID;
                if (cursorPipe && !InFlight)
                {
                    var phase = overlay.mode == OverlayModes.GasConduits.ID ? FluidPhase.Gas : FluidPhase.Liquid;
                    RestoreDose(phase);
                    SelectReservoir(cursorPhase);
                }
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0 && !DoseModifierHeld && OverDoseControl()) { ScrollDose(wheel); return; }
            if ((amount != null && amount.isFocused) || (degrees != null && degrees.isFocused)) return;
            if (Input.GetKeyDown(KeyCode.Tab)) ChangeView(!cursorPipe);
            if (Input.GetKeyDown(KeyCode.BackQuote)) ChangeView(false);
            if (wheel != 0 && DoseModifierHeld && !InFlight)
            {
                mapTool.InvalidatePreview();
                mapTool.CancelHold();
                bool larger = wheel > 0;
                double current = TryNumber(massText, out var value) ? value : 10;
                int next = unitIndex;
                if (larger ? current >= 10 : current <= 10)
                    next = (next + (larger ? 1 : -1) + KgPerUnit.Length) % KgPerUnit.Length;
                ApplyPreset(next);
            }
        }

        private static bool DoseModifierHeld => Input.GetKey(KeyCode.LeftAlt);
        internal static void FilterZoom(KButtonEvent __0)
        {
            if (PlayerController.Instance == null ||
                !(PlayerController.Instance.ActiveTool is MapPipetteTool) || Input.mouseScrollDelta.y == 0 ||
                (!DoseModifierHeld && (Current == null || !Current.OverDoseControl()))) return;
            __0.TryConsume(Action.ZoomIn);
            __0.TryConsume(Action.ZoomOut);
        }

        private int mapTicket;
        internal bool FullDose => heldKg >= ConfiguredKg;
        internal float StoredMass => heldKg;
        internal string SampleCaption => PipetteText.Get("Held: ", "管中：") + ElementLoader.FindElementByHash((SimHashes)heldHash)?.name +
            " · " + DescribeDose(heldKg) + " · " + GameUtil.GetFormattedTemperature(originalK);
        private string CursorDrop(int cell, out FluidSample target, out float kelvin)
        {
            kelvin=originalK;
            string refusal=DropRefusal(cell,cursorPipe,cursorPhase,out target);
            if(refusal!=null) return refusal;
            if(sameOnly && target.Phase!=FluidPhase.Vacuum && target.Element!=heldHash)
                return PipetteText.Get("Choose an empty cell or the same element.", "仅允许同类：请选择空格或同元素目标。");
            if(!tempered || OverTemperatureLimit(heldKg)) return null;
            if(!Unlocked(TechId) || !TryNumber(temperatureText,out double c))
                return PipetteText.Get("Adjusting temperature requires research and a valid temperature.", "变温需要温度调节科技和有效温度。");
            kelvin=(float)(c+273.15);
            if(!(kelvin>0 && kelvin<=9999) || !WithinPhase(ElementLoader.FindElementByHash((SimHashes)heldHash),kelvin))
                return PipetteText.Get("Invalid temperature or phase change.", "温度无效或会导致相变。");
            return null;
        }
        internal bool CanDrop(int cell) => CursorDrop(cell,out _,out _) == null;
        internal bool CanPick(int cell, out FluidSample sample, out float dose, out TransferPlan plan) =>
            PlanPickup(cell, cursorPipe, cursorPhase, ConfiguredKg, out sample, out dose, out plan);
        internal bool GasCursor => cursorPhase == FluidPhase.Gas;
        internal bool CanHold => Editable();
        internal bool HasMapDose => heldKg > 0;

        private DuplicantGrabTool grabTool;
        private readonly System.Collections.Generic.List<KSelectable> grabHits = new System.Collections.Generic.List<KSelectable>();
        private InterfaceTool autoPrevious;
        private FluidPhase autoPreviousPhase;
        private bool autoPreviousPipe, autoReturnPending;
        internal bool AutoPipetteActive => autoPrevious != null || Input.GetKey(KeyCode.LeftAlt);

        private void UpdateAutoSelection()
        {
            var controller = PlayerController.Instance;
            var tool = controller.ActiveTool;
            if (autoPrevious != null)
            {
                autoReturnPending = !Input.GetKey(KeyCode.LeftAlt) || !Application.isFocused;
                if (!autoReturnPending || mapTool.Holding || inFlight || HasMapDose) return;
                var previous = autoPrevious;
                autoPrevious = null;
                if (tool != mapTool) return;
                if (previous == mapTool)
                {
                    SelectReservoir(autoPreviousPhase);
                    RestoreDose(autoPreviousPhase);
                    ChangeView(autoPreviousPipe);
                }
                else controller.ActivateTool(previous);
                return;
            }
            if (!Input.GetKeyDown(KeyCode.LeftAlt) || tool == null || !tool.hasFocus || !Editable() ||
                Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
            var events = UnityEngine.EventSystems.EventSystem.current;
            if ((events != null && events.IsPointerOverGameObject()) || OverParameters() ||
                (amount != null && amount.isFocused) || (degrees != null && degrees.isFocused)) return;

            if (tool == grabTool) return;
            var body = DuplicantGrabTool.FindTarget(tool, grabHits, Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            if (body != null)
            {
                if (grabTool == null)
                {
                    var obj = new GameObject("YAPP Duplicant Grab");
                    obj.SetActive(false);
                    obj.transform.SetParent(controller.transform, false);
                    grabTool = obj.AddComponent<DuplicantGrabTool>();
                }
                grabTool.Previous = tool;
                grabTool.Target = body;
                controller.ActivateTool(grabTool);
                return;
            }
            int cell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            if (!VisibleCell(cell) || Grid.IsSolidCell(cell) || Grid.HasDoor[cell] || Grid.Mass[cell] <= 0) return;
            var element = Grid.Element[cell];
            if (element == null || !(element.IsLiquid || element.IsGas)) return;
            var phase = element.IsLiquid ? FluidPhase.Liquid : FluidPhase.Gas;
            if (tool == mapTool && cursorPhase == phase && !cursorPipe) return;
            autoPreviousPhase = cursorPhase;
            autoPreviousPipe = cursorPipe;
            ChooseCursor(phase, mapOnly: true);
            if (controller.ActiveTool == mapTool)
            {
                autoPrevious = tool;
                autoReturnPending = false;
            }
        }

        private void ChooseCursor(FluidPhase phase, bool mapOnly = false)
        {
            if (InFlight || invalidBuffer) return;
            if (!mapOnly) autoPrevious = null;
            SelectReservoir(phase);
            RestoreDose(phase);
            bool active = mapTool != null && PlayerController.Instance.ActiveTool == mapTool;
            if (active)
            {
                ChangeView(!mapOnly && cursorPipe);
                return;
            }
            ToggleMapTool(mapOnly);
        }

        private void ToggleMapTool(bool mapOnly)
        {
            var controller = PlayerController.Instance;
            if (controller == null) return;
            if (mapTool == null)
            {
                var obj = new GameObject("YAPP Map Pipette");
                obj.SetActive(false);
                obj.transform.SetParent(controller.transform, false);
                mapTool = obj.AddComponent<MapPipetteTool>();
                mapTool.Owner = this;
            }
            if (controller.ActiveTool == mapTool)
            {
                if (mapTool.Previous != null) controller.ActivateTool(mapTool.Previous);
                return;
            }
            mapTool.Previous = controller.ActiveTool;
            priorView = OverlayScreen.Instance == null ? OverlayModes.None.ID : OverlayScreen.Instance.mode;
            controller.ActivateTool(mapTool);
            ChangeView(!mapOnly && (priorView == OverlayModes.GasConduits.ID || priorView == OverlayModes.LiquidConduits.ID));
            status = HasMapDose ? PipetteText.Get("Hold right to dispense", "右键长按放出") : PipetteText.Get("Hold left to collect", "左键长按取样");
        }

        internal void ClickMap(int cell, bool drop)
        {
            if (!Editable()) return;
            float before = heldKg;
            if (cursorPipe && OverlayScreen.Instance != null && OverlayScreen.Instance.mode !=
                (cursorPhase == FluidPhase.Gas ? OverlayModes.GasConduits.ID : OverlayModes.LiquidConduits.ID))
            { status = PipetteText.Get("Switch to the matching conduit overlay.", "当前管道视图与样本类型不符，请切换视图。"); return; }
            FluidSample sample;
            TransferPlan plan = default;
            float dose = 0;
            float deliveryK=originalK;
            if (drop)
            {
                string refusal=CursorDrop(cell,out sample,out deliveryK);
                if(refusal!=null){status=refusal;return;}
                if (cursorPipe)
                {
                    EmitChecked(cell, true, originalPipe && cell == originalCell, true, deliveryK);
                    mapTool.ConfirmTransfer(before, true);
                    return;
                }

            }
            else if (!CanPick(cell, out sample, out dose, out plan)) return;
            var element = ElementLoader.FindElementByHash((SimHashes)(drop ? heldHash : sample.Element));
            if (element == null || (cursorPhase == FluidPhase.Liquid ? !element.IsLiquid : !element.IsGas))
            { status = drop ? PipetteText.Get("Use the conduit panel for this sample.", "当前样本请用管道面板投放。") : PipetteText.Get("No matching fluid in this cell.", "此格没有对应流体。"); return; }
            if (cursorPipe)
            {
                mode = cursorPhase;
                if (TakePipeSample(cell, sample, plan)) status = PipetteText.Get("Collected ", "已取 ") + DescribeDose(heldKg);
                mapTool.ConfirmTransfer(before, drop);
                return;
            }
            int ticket = ++mapTicket;
            inFlight = true;
            mapPending = true;
            mapTool.InvalidatePreview();
            status = Running() ? (drop ? PipetteText.Get("Dispensing…", "正在投放…") : PipetteText.Get("Collecting…", "正在取样…")) : PipetteText.Get("Waiting for the simulation.", "已提交，等待模拟器确认。");
            uncertainContext = (drop ? "map emit " : "map consume ") + cell;
            bool Pending() => this != null && Current == this && mapPending && ticket == mapTicket;
            void Complete()
            {
                mapPending = inFlight = false;
                uncertainContext = null;
                if (mapTool != null) mapTool.ConfirmTransfer(before, drop);
            }
            try
            {
                if (drop)
                {
                    if (element.IsLiquid && (sample.Phase == FluidPhase.Gas || sample.Phase == FluidPhase.Vacuum))
                    {
                        FallingWater.instance.AddParticle(cell, ElementLoader.GetElementIndex(element.id),
                            heldKg, deliveryK, germIndex, germCount);
                        FinishEmit(false, deliveryK);
                        Complete();
                        return;
                    }
                    var handle = Game.Instance.massEmitCallbackManager.Add((info, _) => {
                        if (!Pending()) return;
                        if (info.suceeded != 0) FinishEmit(false, deliveryK);
                        else status = PipetteText.Get("Cell rejected delivery. Sample retained.", "目标格拒绝滴液，样本仍在管中。");
                        Complete();
                    }, null, nameof(MapPipetteTool));
                    SimMessages.EmitMass(cell, ElementLoader.GetElementIndex(element.id), heldKg,
                        deliveryK, germIndex, germCount, handle.index);
                }
                else
                {
                    var handle = Game.Instance.massConsumedCallbackManager.Add((info, _) => {
                        if (!Pending()) return;
                        if (info.mass > 0)
                        {
                            Buffer(info.mass, info.temperature, info.temperature,
                                (int)ElementLoader.elements[(int)info.elemIdx].id,
                                info.diseaseIdx, info.diseaseCount, cell, false);
                            mode = cursorPhase;
                            status = PipetteText.Get("Collected ", "已取 ") + DescribeDose(heldKg) + PipetteText.Get(". Hold right to dispense.", "，右键长按放出。");
                        }
                        else status = PipetteText.Get("No sample collected. Choose another cell.", "未取到液体，请重新选格。");
                        Complete();
                    }, null, nameof(MapPipetteTool));
                    SimMessages.ConsumeMass(cell, element.id, dose, (byte)1, (byte)1, handle.index);
                }
            }
            catch (Exception error) { mapPending = false; Quarantine(PipetteText.Get("Map transfer error", "地图取放异常"), error); }
        }
    }
}
