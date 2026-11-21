using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace OniPrecision
{
    public sealed partial class FluidPipette
    {
        private RectTransform ui, sheet, gridFrame;
        private LocText fontSource;
        private ToolParameterMenu parameterMenu;
        private bool showingParameters;

        private float nextRefresh;
        private TextMeshProUGUI feedback;
        private InputField amount, degrees;
        private Slider doseSlider;
        private readonly List<System.Action> refresh = new List<System.Action>();

        partial void DestroyPanel()
        {
            if (mapTool != null) { if (PlayerController.Instance != null && PlayerController.Instance.ActiveTool == mapTool && mapTool.Previous != null) PlayerController.Instance.ActivateTool(mapTool.Previous); Destroy(mapTool.gameObject); }
            HideParameters();
            if (sheet != null) Destroy(sheet.gameObject);
            if (ui != null) Destroy(ui.gameObject);
            refresh.Clear();
        }

        private RectTransform Box(string name, Transform parent, int x, int y, int w, int h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private void UpdateGridFrame(bool active)
        {
            if (gridFrame == null)
            {
                gridFrame = Box("YAPP hovered cell", ui, 0, 0, 1, 1);
                gridFrame.pivot = new Vector2(.5f,.5f);
                gridFrame.SetAsFirstSibling();
                for (int i=0; i<4; i++)
                {
                    var edge = Box("Edge", gridFrame, 0, 0, 0, 0);
                    bool horizontal = i < 2;
                    float side = i % 2;
                    edge.anchorMin = horizontal ? new Vector2(0,side) : new Vector2(side,0);
                    edge.anchorMax = horizontal ? new Vector2(1,side) : new Vector2(side,1);
                    edge.pivot = new Vector2(.5f,.5f);
                    edge.sizeDelta = horizontal ? new Vector2(0,2) : new Vector2(2,0);
                    var image = edge.gameObject.AddComponent<Image>();
                    image.color = new Color(1,1,1,.85f);
                    image.raycastTarget = false;
                }
            }
            var camera = Camera.main;
            int cell = active ? Grid.PosToCell(camera.ScreenToWorldPoint(KInputManager.GetMousePos())) : -1;
            bool show = active && VisibleCell(cell) && (UnityEngine.EventSystems.EventSystem.current == null ||
                !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
            gridFrame.gameObject.SetActive(show);
            if (!show) return;
            var center = Grid.CellToPosCCC(cell,Grid.SceneLayer.Move);
            var canvasCamera = ui.GetComponentInParent<Canvas>().worldCamera;
            Vector2 Local(Vector3 pos)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(ui, camera.WorldToScreenPoint(pos), canvasCamera, out var local);
                return local;
            }
            var half = new Vector3(.5f,.5f,0);
            gridFrame.localPosition = Local(center);
            gridFrame.sizeDelta = Local(center + half) - Local(center - half);
        }

        private TextMeshProUGUI Caption(RectTransform parent, string value)
        {
            var rect = Box("Caption", parent, 0, 0, 0, 0);
            rect.anchorMax = Vector2.one;
            rect.anchorMin = Vector2.zero;
            rect.offsetMin = new Vector2(3, 1);
            rect.offsetMax = new Vector2(-3, -1);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = fontSource.font;
            label.fontSize = 15;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        private RectTransform ButtonAt(RectTransform parent, int x, int y, int width, Func<string> title,
            System.Action action, Func<bool> available)
        {
            var rect = Box("Button", parent, x, y, width, 30);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(.34f,.32f,.41f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var label = Caption(rect, title());
            button.onClick.AddListener(() => {
                if (!available()) return;
                action();
                label.text = title();
            });
            refresh.Add(() => {
                button.interactable = available();
                string value = title();
                if (label.text != value) label.text = value;
            });
            return rect;
        }

        private InputField Number(int x, int y, string value, System.Action<string> set)
        {
            var rect = Box("Number", sheet, x, y, 100, 30);
            rect.gameObject.AddComponent<Image>().color = Color.white;
            var field = rect.gameObject.AddComponent<InputField>();
            var textRect = Box("Digits", rect, 5, 0, 90, 30);
            var digits = textRect.gameObject.AddComponent<Text>();
            digits.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            digits.alignment = TextAnchor.MiddleLeft;
            digits.color = Color.black;
            field.textComponent = digits;
            field.contentType = InputField.ContentType.DecimalNumber;
            field.text = value;
            field.onValueChanged.AddListener(v => set(v));
            return field;
        }

        private bool Editable() => PrecisionMod.Ready && !invalidBuffer && !InFlight;


        private static readonly bool TestUnlock = true;
        private static bool Unlocked(string tech) => TestUnlock || Db.Get()?.Techs?.TryGet(tech)?.IsComplete() == true;
        private static string ToolTech(FluidPhase phase) => phase == FluidPhase.Gas ? "GasPiping" : "LiquidPiping";
        private static string ToolLabel(FluidPhase phase) => phase == FluidPhase.Gas ? "取气管" : "取液器";
        [KSerialization.Serialize] private bool sameOnly;
        private float ConfiguredKg
        {
            get
            {
                if (!TryNumber(massText, out var value) || unitIndex < 0 || unitIndex >= KgPerUnit.Length) return 0;
                return value >= 1 && value <= 1000 ? (float)(value * KgPerUnit[unitIndex]) : 0;
            }
        }

        private void Changed()
        {
            mapTool?.CancelHold();
            mapTool?.InvalidatePreview();
        }

        private void ApplyPreset()
        {
            unitIndex = calibration;
            massText = "10";
            if (amount != null) amount.text = massText;
            Changed();
        }

        private void BuildDoseControl()
        {
            amount = Number(112,34,massText,value => {
                massText = value;
                if (TryNumber(value,out double n))
                    doseSlider.SetValueWithoutNotify((float)Math.Round(Math.Max(0, Math.Min(200, n/5))));
                Changed();
            });
            amount.onEndEdit.AddListener(value => {
                double n = TryNumber(value,out var typed) ? typed : 1;
                amount.text = Math.Max(1, Math.Min(1000, n)).ToString(Invariant);
            });
            var track = Box("Dose slider", sheet, 8, 70, 268, 24);
            track.gameObject.AddComponent<Image>().color = new Color(.23f,.21f,.28f);
            doseSlider = track.gameObject.AddComponent<Slider>();
            doseSlider.maxValue = 200;
            doseSlider.wholeNumbers = true;
            var rail = Box("Handle area",track,7,0,254,24);
            var handle = Box("Handle",rail,0,0,12,22);
            handle.pivot = new Vector2(.5f,.5f);
            handle.anchoredPosition = Vector2.zero;
            doseSlider.targetGraphic = handle.gameObject.AddComponent<Image>();
            doseSlider.handleRect = handle;
            doseSlider.SetValueWithoutNotify(2);
            doseSlider.onValueChanged.AddListener(index => amount.text = Math.Max(1,(int)index * 5).ToString(Invariant));
        }

        internal bool OverDoseControl()
        {
            if (!showingParameters || doseSlider == null) return false;
            var canvas = sheet.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var mouse = (Vector2)KInputManager.GetMousePos();
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)amount.transform, mouse, camera) ||
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)doseSlider.transform, mouse, camera);
        }

        private void ScrollDose(float wheel)
        {
            if (InFlight) return;
            double current = TryNumber(massText,out var value) ? Math.Max(1,Math.Min(1000,value)) : 1;
            double index = wheel > 0 ? Math.Floor(current/5)+1 : Math.Ceiling(current/5)-1;
            amount.text = Math.Max(1,Math.Min(1000,index*5)).ToString(Invariant);
        }

        private void BuildParameters()
        {
            parameterMenu = ToolMenu.Instance.toolParameterMenu;
            sheet = Box("YAPP configuration", parameterMenu.widgetContainer.transform.parent, 0, 0, 285, 270);
            var layout = sheet.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 285;
            layout.minHeight = layout.preferredHeight = 270;
            sheet.gameObject.AddComponent<Image>().color = new Color(.16f,.15f,.2f,.98f);
            var title = Caption(Box("Title",sheet,4,0,277,32),"");
            refresh.Add(()=>title.text = ToolLabel(cursorPhase) + "设置");
            Caption(Box("Dose",sheet,4,34,100,30),"装填上限");
            BuildDoseControl();
            for (int i=0;i<Units.Length;i++)
            {
                int n=i;
                ButtonAt(sheet,5+i*69,102,65,()=>unitIndex==n?"["+Units[n]+"]":Units[n],
                    ()=>{unitIndex=n; Changed();},()=>!InFlight);
            }
            ButtonAt(sheet,5,138,275,()=>sameOnly?"仅允许同类目标  [开启]":"仅允许同类目标  [关闭]",
                ()=>{sameOnly=!sameOnly; Changed();},()=>!InFlight);
            ButtonAt(sheet,5,174,165,()=>!Unlocked(TechId)?"变温未解锁":tempered?"指定温度 °C":"保持原温",
                ()=>{tempered=!tempered; Changed();},()=>!InFlight && Unlocked(TechId));
            degrees=Number(176,174,temperatureText,v=>{temperatureText=v; Changed();});
            feedback=Caption(Box("Status",sheet,5,206,275,60),"");
            sheet.gameObject.SetActive(false);
        }

        internal bool OverParameters()
        {
            if (!showingParameters || sheet == null || !sheet.gameObject.activeInHierarchy) return false;
            var canvas = sheet.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var mouse = (Vector2)KInputManager.GetMousePos();
            return RectTransformUtility.RectangleContainsScreenPoint(sheet, mouse, camera) ||
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)parameterMenu.content.transform, mouse, camera);
        }

        private void HideParameters()
        {
            if (!showingParameters) return;
            showingParameters=false;
            if (sheet!=null) sheet.gameObject.SetActive(false);
            if (parameterMenu!=null)
            {
                parameterMenu.content.SetActive(false);
                parameterMenu.widgetContainer.SetActive(true);
            }
        }

        private void LateUpdate()
        {
            if(Current!=this || Game.Instance==null || Game.Instance.IsLoading() || Camera.main==null) return;
            if(ui==null)
            {
                if(GameScreenManager.Instance==null || GameScreenManager.Instance.ssOverlayCanvas==null) return;
                fontSource=UnityEngine.Object.FindAnyObjectByType<LocText>();
                if(fontSource==null || fontSource.font==null) return;
                ui=Box("YAPP cell highlight",GameScreenManager.Instance.ssOverlayCanvas.transform,0,0,1,1);
            }
            bool active=mapTool!=null && PlayerController.Instance.ActiveTool==mapTool;
            UpdateGridFrame(active);
            if(!active) { HideParameters(); return; }
            if(sheet==null)
            {
                if(ToolMenu.Instance==null || ToolMenu.Instance.toolParameterMenu==null) return;
                BuildParameters();
            }
            if(!showingParameters)
            {
                showingParameters=true;
                parameterMenu.widgetContainer.SetActive(false);
                parameterMenu.content.SetActive(true);
                sheet.gameObject.SetActive(true);
                nextRefresh=0;
            }
            CursorInput();
            mapTool.AdvanceHold();
            if(Time.unscaledTime < nextRefresh) return;
            nextRefresh=Time.unscaledTime+.15f;
            foreach(var update in refresh) update();
            amount.interactable=doseSlider.interactable=!InFlight;
            degrees.gameObject.SetActive(tempered && Unlocked(TechId));
            degrees.interactable=!InFlight;
            feedback.text=ConfiguredKg<=0 ? "请输入 1 至 1000 之间的数值。" :
                "上限 " + DescribeDose(ConfiguredKg) + " · " + (cursorPipe?"管道":"地图") + "\n" + status;
        }
    }
}
