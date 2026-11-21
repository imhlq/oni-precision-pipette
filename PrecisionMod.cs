using System;
using System.Globalization;
using HarmonyLib;
using KSerialization;
using UnityEngine;

namespace OniPrecision
{
    public sealed class PrecisionMod : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            Ready = false;
            base.OnLoad(harmony);
            // Resolve every required hook before installing any of them. No PatchAll:
            // one missing hook must disable transfers, not break the entire mod load.
            var install = AccessTools.DeclaredMethod(typeof(SaveGame), "OnPrefabInit", Type.EmptyTypes);
            var canSave = AccessTools.DeclaredMethod(typeof(Game), "CanSave", Type.EmptyTypes);
            if (install == null)
            {
                Debug.LogError("[Precision Pipette] Required U60 hooks missing. Transfers disabled; keep the previous save backup.");
                return;
            }
            try
            {
                if (canSave == null || canSave.ReturnType != typeof(bool))
                {
                    // Still attach the serialized component to retain old buffers, but
                    // never enable mutations without the save guard.
                    harmony.Patch(install, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(Install)));
                    Debug.LogError("[Precision Pipette] Save guard unavailable. Buffers retained; transfers disabled.");
                    return;
                }
                harmony.Patch(canSave, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(CheckSave)));
                harmony.Patch(install, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(Install)));
                Ready = true;
            }
            catch (Exception error)
            {
                Debug.LogError("[Precision Pipette] Hook installation failed. Transfers disabled. " + error);
            }
        }

        internal static bool Ready { get; private set; }
        private static void Install(SaveGame __instance)
        {
            if (__instance.GetComponent<FluidPipette>() == null)
                __instance.gameObject.AddComponent<FluidPipette>();
        }
        private static void CheckSave(ref bool __result)
        {
            if (FluidPipette.Current != null && FluidPipette.Current.InFlight) __result = false;
        }
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class FluidPipette : KMonoBehaviour
    {
        internal static FluidPipette Current { get; private set; }
        internal bool InFlight => inFlight || uncertain;
        private const string TechId = "TemperatureModulation";
        private const double ThermalMaxKg = 0.02;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [Serialize] private float heldKg;
        [Serialize] private float originalK;
        [Serialize] private float outputK;
        [Serialize] private int heldHash;
        [Serialize] private byte germIndex;
        [Serialize] private int germCount;
        [Serialize] private int originalCell = -1;
        [Serialize] private bool originalPipe;
        [Serialize] private int originVersion;
        [Serialize] private int originalWorld;
        private object nativeSaveBlocker;
        private ClusterManager blockerOwner;

        // Persist quarantine if another save path serializes despite the guards.
        [Serialize] private bool uncertain;
        [Serialize] private string uncertainContext;
        private bool invalidBuffer;
        private bool expanded, tempered, inFlight, sourcePipe = true, targetPipe = true;
        private int source = -1, target = -1, picking, unitIndex;
        private FluidPhase mode = FluidPhase.Gas;
        private string massText = "50", temperatureText = "-25", status = "请选择起点和目标格。";
        private Rect panel = new Rect(16, 110, 448, 0);
        private static readonly string[] Phases = { "移气管", "移液管" };
        private static readonly string[] EndpointKinds = { "地图", "管道" };
        private static readonly string[] Units = { "µg", "mg", "g", "kg" };
        private static readonly double[] KgPerUnit = { 1e-9, 1e-6, 1e-3, 1 };

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Current = this;
            // v0.2.0 FinishEmit left stale metadata behind after clearing heldKg.
            if (heldKg == 0)
            {
                germCount = 0; germIndex = 0; originalK = outputK = 0;
                originalCell = -1; originalPipe = false; originVersion = 0;
            }
            invalidBuffer = !PipeAccounting.ValidBuffer(heldKg, originalK, outputK, germCount);
            if (uncertain)
            {
                status = "转移结果不确定：禁止重试和保存。请退出并读取操作前存档。 " + uncertainContext;
                expanded = true;
                BlockNativeSave();
            }
            else if (invalidBuffer)
                status = "旧缓冲数据无效：原样保留并禁用转移；请恢复备份。";
            else if (heldKg > 0)
            {
                mode = ElementLoader.FindElementByHash((SimHashes)heldHash)?.IsLiquid == true
                    ? FluidPhase.Liquid : FluidPhase.Gas;
                status = originVersion == 1 ? "已恢复样本。请选择兼容管道投放，或退回已验证原管道。" :
                    "已恢复旧缓冲。禁止自动退回；请选择兼容管道投放（可用原温）。";
            }
        }

        protected override void OnCleanUp()
        {
            if (Current == this) Current = null;
            if (nativeSaveBlocker != null && blockerOwner != null)
            {
                try { AccessTools.Method(typeof(ClusterManager), "RemoveSaveBlocker")?.Invoke(
                    blockerOwner, new[] { nativeSaveBlocker }); }
                catch (Exception error) { Debug.LogError("[Precision Pipette] Save blocker cleanup: " + error); }
            }
            base.OnCleanUp();
        }

        private static bool ResearchDone()
        {
            return Db.Get()?.Techs?.TryGet(TechId)?.IsComplete() == true;
        }

        private void OnGUI()
        {
            if (Current != this || Game.Instance == null || Game.Instance.IsLoading() ||
                Camera.main == null) return;

            if (!expanded)
            {
                if (GUI.Button(new Rect(16, 110, 140, 28),
                        heldKg > 0 ? "移气 / 移液  !" : "移气 / 移液"))
                    expanded = true;
                return;
            }

            panel = GUILayout.Window(GetInstanceID(), panel, DrawWindow, "精密移气 / 移液",
                GUILayout.Width(448));
            DrawMark(source, sourcePipe ? "管起" : "格起");
            DrawMark(target, targetPipe ? "管终" : "格终");

            Event ev = Event.current;
            if (picking == 0 || ev == null) return;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                picking = 0;
                status = "已取消选格。";
                ev.Use();
                return;
            }
            if (ev.type != EventType.MouseDown || ev.button != 0 ||
                panel.Contains(ev.mousePosition)) return;
            int cell = Grid.PosToCell(Camera.main.ScreenToWorldPoint(KInputManager.GetMousePos()));
            bool pipe = picking == 1 ? sourcePipe : targetPipe;
            if (!ReadEndpoint(cell, pipe, mode, out _))
                status = pipe ? "拒绝：这里没有已建成的对应管道。" : "拒绝：请选择当前地图可见、不是固体或门的格子。";
            else
            {
                if (picking == 1) source = cell;
                else target = cell;
                picking = 0;
                status = "已选格；点「执行一次」才取样。";
            }
            ev.Use();
        }

        private void DrawWindow(int id)
        {
            bool unlocked = ResearchDone();
            bool held = heldKg > 0;
            if (!PrecisionMod.Ready || uncertain || invalidBuffer)
            {
                GUILayout.Label(!PrecisionMod.Ready ? "U60 必需挂钩不可用：转移已禁用。" : status);
                GUI.DragWindow(new Rect(0, 0, 448, 20));
                return;
            }
            GUILayout.Label("U60 稳定性候选：仅管道转移。地图仅查看，禁止取样 / 投放 / 退回。");
            var enabled = GUI.enabled;
            GUI.enabled = enabled && !inFlight && !held;
            GUILayout.BeginHorizontal();
            int phase = mode == FluidPhase.Gas ? 0 : 1;
            int chosenPhase = Choose(phase, Phases, 88);
            if (chosenPhase != phase) SetMode(chosenPhase == 0 ? FluidPhase.Gas : FluidPhase.Liquid);
            GUILayout.EndHorizontal();

            DrawEndpointRow(true);
            GUI.enabled = enabled && !inFlight;
            DrawEndpointRow(false);
            GUI.enabled = enabled && !inFlight && !held;
            GUILayout.BeginHorizontal();
            GUILayout.Label("剂量", GUILayout.Width(54));
            massText = GUILayout.TextField(massText, 20, GUILayout.Width(90));
            unitIndex = Choose(unitIndex, Units, 48);
            GUILayout.EndHorizontal();

            GUI.enabled = GUI.enabled && unlocked;
            bool requestedTemperature = GUILayout.Toggle(tempered && unlocked,
                unlocked ? "指定本次投放温度" : "变温未解锁：完成温度调节研究");
            if (GUI.enabled) tempered = requestedTemperature;
            else if (!unlocked && !held) tempered = false;
            if (tempered && unlocked)
            {
                GUILayout.BeginHorizontal();
                temperatureText = GUILayout.TextField(temperatureText, 16, GUILayout.Width(100));
                GUILayout.Label("°C；变温单次最多 20 g");
                GUILayout.EndHorizontal();
            }

            GUI.enabled = enabled && !inFlight && !held;
            if (Click("执行一次")) StartTransfer();
            GUI.enabled = enabled && !inFlight;
            if (held)
            {
                GUILayout.Label("缓冲中：" + DescribeDose(heldKg));
                GUILayout.BeginHorizontal();
                if (Click("投放到所选目标")) TryEmit(target, targetPipe, false);
                if (Click("原温投放到所选管道")) TryEmit(target, targetPipe, false, true);
                if (Click("原温退回原格")) TryEmit(originalCell, originalPipe, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(picking == 0 ? status : (picking == 1 ? "点击地图起点格" : "点击地图目标格") + "（Esc 取消）");
            if (Click("收起")) { expanded = false; picking = 0; }
            GUI.enabled = enabled;
            GUI.DragWindow(new Rect(0, 0, 448, 20));
        }

        private void SetMode(FluidPhase phase)
        {
            mode = phase;
            source = target = -1;
            massText = phase == FluidPhase.Gas ? "50" : "30";
            unitIndex = phase == FluidPhase.Gas ? 0 : 2;
            picking = 0;
        }

        private void DrawEndpointRow(bool isSource)
        {
            bool pipe = isSource ? sourcePipe : targetPipe;
            GUILayout.BeginHorizontal();
            GUILayout.Label(isSource ? "起点" : "目标", GUILayout.Width(42));
            int kind = Choose(pipe ? 1 : 0, EndpointKinds, 61);
            if ((kind == 1) != pipe)
            {
                if (isSource) { sourcePipe = kind == 1; source = -1; }
                else { targetPipe = kind == 1; target = -1; }
                picking = 0;
            }
            if (Click(CellLabel(isSource ? source : target, kind == 1)))
                picking = isSource ? 1 : 2;
            GUILayout.EndHorizontal();
        }

        // Moono v2026.11.20.04 has measured Button behavior, but no handwritten
        // SelectionGrid implementation. Use the same small button groups on both players,
        // and accept clicks only while the caller has enabled that group.
        private static bool Click(string label, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(label, options);
            return GUI.enabled && clicked;
        }

        private static int Choose(int selected, string[] choices, float width)
        {
            int next = selected;
            for (int i = 0; i < choices.Length; i++)
                if (Click((i == selected ? "[" + choices[i] + "]" : choices[i]), GUILayout.Width(width)))
                    next = i;
            return next;
        }

        private string CellLabel(int cell, bool pipe)
        {
            if (!ReadEndpoint(cell, pipe, mode, out var sample))
                return pipe ? (mode == FluidPhase.Gas ? "选气管" : "选液管") : "选地图格";
            Grid.CellToXY(cell, out int x, out int y);
            return "(" + x + ", " + y + ") " + (sample.Phase == FluidPhase.Vacuum ?
                (pipe ? "空管" : "真空") : ElementLoader.FindElementByHash((SimHashes)sample.Element)?.name.ToString());
        }

        private static bool VisibleCell(int cell)
        {
            return Grid.IsValidCell(cell) && Grid.IsVisible(cell) &&
                ClusterManager.Instance != null && Grid.WorldIdx[cell] == ClusterManager.Instance.activeWorldId;
        }

        private static void DrawMark(int cell, string label)
        {
            if (!VisibleCell(cell)) return;
            Vector3 point = Camera.main.WorldToScreenPoint(Grid.CellToPosCCC(cell, Grid.SceneLayer.Move));
            if (point.z >= 0)
                GUI.Box(new Rect(point.x - 23, Screen.height - point.y - 14, 46, 28), label);
        }

        private static ConduitFlow PipeFlow(FluidPhase phase)
        {
            return phase == FluidPhase.Gas ? Game.Instance?.gasConduitFlow :
                phase == FluidPhase.Liquid ? Game.Instance?.liquidConduitFlow : null;
        }

        private static bool ReadEndpoint(int cell, bool pipe, FluidPhase mode, out FluidSample sample)
        {
            sample = default;
            if (!VisibleCell(cell)) return false;
            // Map and pipe in the same grid coordinate are distinct compartments.
            int key = checked(cell * 2 + (pipe ? 1 : 0));
            if (pipe)
            {
                var flow = PipeFlow(mode);
                if (flow == null || !flow.HasConduit(cell)) return false;
                var contents = flow.GetContents(cell);
                if (contents.mass == 0f)
                {
                    sample = new FluidSample(key, (int)SimHashes.Vacuum, FluidPhase.Vacuum, 0, 0);
                    return true;
                }
                var element = ElementLoader.FindElementByHash(contents.element);
                if (element == null) return false;
                sample = new FluidSample(key, (int)contents.element,
                    element.IsGas ? FluidPhase.Gas : element.IsLiquid ? FluidPhase.Liquid : FluidPhase.Other,
                    contents.mass, contents.temperature);
                return true;
            }
            if (Grid.IsSolidCell(cell) || Grid.HasDoor[cell]) return false;
            var e = Grid.Element[cell];
            if (e == null) return false;
            FluidPhase phase = e.IsVacuum ? FluidPhase.Vacuum : e.IsGas ? FluidPhase.Gas :
                e.IsLiquid ? FluidPhase.Liquid : FluidPhase.Other;
            sample = new FluidSample(key, (int)e.id, phase, Grid.Mass[cell], Grid.Temperature[cell]);
            return true;
        }

        private static bool Running()
        {
            return SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused;
        }

        private void StartTransfer()
        {
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || heldKg > 0) return;
            if (!sourcePipe || !targetPipe)
            { status = "拒绝：地图转移已禁用；尚未取样。请选择管道。"; return; }
            if (!Running()) { status = "请先解除暂停；暂停时不会扣取或投放。"; return; }
            if (!ReadEndpoint(source, sourcePipe, mode, out var src) || !ReadEndpoint(target, targetPipe, mode, out var dst))
            { status = "拒绝：起点或目标无效。"; return; }
            if (!TryNumber(massText, out var dose))
            { status = "拒绝：剂量不是有效数值。"; return; }
            if ((uint)unitIndex >= (uint)KgPerUnit.Length)
            { status = "拒绝：请明确选择剂量单位。"; return; }
            dose *= KgPerUnit[unitIndex];
            double? setC = null;
            bool unlock = ResearchDone();
            if (tempered)
            {
                if (!unlock || !TryNumber(temperatureText, out var temp) || dose > ThermalMaxKg)
                { status = "拒绝：变温未解锁、温度无效，或剂量超过 20 g。"; return; }
                setC = temp;
            }

            if (!TransferPlan.TryCreate(src, dst, mode, dose, unlock, setC, out var plan, out var reason))
            { status = RejectLabel(reason); return; }

            if (targetPipe && !TransferPlan.CanFitPipe(dst.MassKg, plan.AmountKg, mode))
            { status = "拒绝：目标管道没有足够空间。未取样。"; return; }

            var element = ElementLoader.FindElementByHash((SimHashes)src.Element);
            if (element == null) { status = "拒绝：无法识别源元素。"; return; }
            if (setC.HasValue && !WithinPhase(element, plan.TransferTemperatureK))
            { status = "拒绝：设定温度会使该元素发生相变。"; return; }
            int srcCell = source, dstCell = target;
            var flow = PipeFlow(mode);
            var before = flow.GetContents(srcCell);
            if (before.mass != src.MassKg || (int)before.element != src.Element ||
                before.temperature != src.TemperatureK || before.diseaseCount < 0 ||
                (before.diseaseCount > 0 && before.diseaseIdx == byte.MaxValue))
            { status = "拒绝：源管道状态或病菌数据变化。未取样。"; return; }
            uncertainContext = "debit cell=" + srcCell + " world=" + Grid.WorldIdx[srcCell] +
                " element=" + src.Element + " kg=" + plan.AmountKg.ToString("R", Invariant);
            inFlight = true;
            try
            {
                var removed = flow.RemoveElement(srcCell, plan.AmountKg);
                var after = flow.GetContents(srcCell);
                if (!PipeAccounting.ValidDebit(before.mass, after.mass, removed.mass,
                        before.diseaseCount, after.diseaseCount, removed.diseaseCount) ||
                    (removed.mass > 0 && (removed.element != before.element ||
                     removed.temperature != before.temperature ||
                     (removed.diseaseCount > 0 && removed.diseaseIdx != before.diseaseIdx))) ||
                    (after.mass > 0 && (after.element != before.element ||
                     (after.diseaseCount > 0 && after.diseaseIdx != before.diseaseIdx))))
                { Quarantine("管道取样账目无法确认"); return; }
                if (removed.mass == 0)
                { inFlight = false; uncertainContext = null; status = "管道未取出样本。"; return; }
                Buffer(removed.mass, removed.temperature,
                    setC.HasValue ? plan.TransferTemperatureK : removed.temperature,
                    (int)removed.element, removed.diseaseIdx, removed.diseaseCount, srcCell, true);
                inFlight = false;
                uncertainContext = null;
                if (heldHash != src.Element || heldKg != plan.AmountKg)
                { status = "实际取样与计划不同；样本保留，可退回。"; return; }
            }
            catch (Exception error) { Quarantine("管道取样异常", error); return; }
            TryEmit(dstCell, true, false);
        }

        private void Quarantine(string reason, Exception error = null)
        {
            uncertain = true;
            expanded = true;
            BlockNativeSave();
            inFlight = false;
            status = reason + "。结果不确定：禁止重试和保存；请退出并读取操作前存档。";
            Debug.LogError("[Precision Pipette] " + status + " " + uncertainContext + " " + error);
        }

        private void BlockNativeSave()
        {
            if (nativeSaveBlocker != null || ClusterManager.Instance == null) return;
            try
            {
                blockerOwner = ClusterManager.Instance;
                nativeSaveBlocker = AccessTools.Method(typeof(ClusterManager), "AddSaveBlocker",
                    new[] { typeof(string) })?.Invoke(blockerOwner,
                    new object[] { "Precision Pipette: uncertain transfer; reload pre-transfer save" });
            }
            catch (Exception error) { Debug.LogError("[Precision Pipette] Native save blocker unavailable: " + error); }
        }

        private void Buffer(float mass, float fromK, float toK, int hash, byte germ, int count,
            int cell, bool pipe)
        {
            heldKg = mass;
            originalK = fromK;
            outputK = toK;
            heldHash = hash;
            germIndex = germ;
            germCount = count;
            originalCell = cell;
            originalPipe = pipe;
            originalWorld = Grid.WorldIdx[cell];
            originVersion = 1;
        }

        // No ReplaceElement, destructive displacement, adjacent search, or credit before a native debit.
        private void TryEmit(int dstCell, bool dstPipe, bool returnToSource, bool originalTemperature = false)
        {
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || !(heldKg > 0f)) return;
            if (returnToSource && (originVersion != 1 || !Grid.IsValidCell(originalCell) ||
                Grid.WorldIdx[originalCell] != originalWorld))
            { status = "拒绝：旧缓冲或原世界无法确认。请选择兼容管道按原温投放。"; return; }
            if (!dstPipe)
            { status = "拒绝：地图投放和原格退回已禁用。缓冲保留；可投放至兼容管道。"; return; }
            if (!returnToSource && !originalTemperature && outputK != originalK && (!ResearchDone() || heldKg > ThermalMaxKg))
            { status = "拒绝：变温需研究解锁且最多 20 g。可按原温退回管道。"; return; }
            if (!Running()) { status = "模拟已暂停。样本保留，解除暂停后可重试。"; return; }
            var element = ElementLoader.FindElementByHash((SimHashes)heldHash);
            float temperature = returnToSource || originalTemperature ? originalK : outputK;
            if (germCount < 0 || (germCount > 0 && germIndex == byte.MaxValue) ||
                element == null || (!element.IsGas && !element.IsLiquid) ||
                !(temperature > 0) || !WithinPhase(element, temperature))
            { status = "拒绝：样本状态或温度无效。"; return; }
            FluidPhase phase = element.IsGas ? FluidPhase.Gas : FluidPhase.Liquid;
            if (!ReadEndpoint(dstCell, dstPipe, phase, out var dst) ||
                (!returnToSource && dstCell == originalCell && dstPipe == originalPipe) ||
                (dst.Phase != FluidPhase.Vacuum && (dst.Element != heldHash || dst.Phase != phase)))
            { status = "拒绝：目标须为空或同元素。样本保留，未改投附近。"; return; }
            if ((double)(float)((double)dst.MassKg + heldKg) - dst.MassKg != heldKg)
            { status = "拒绝：目标质量太大，无法准确接收该剂量。样本保留。"; return; }

            if (!TransferPlan.CanFitPipe(dst.MassKg, heldKg, phase))
            { status = "拒绝：目标管道没有足够空间。样本保留。"; return; }
            var flow = PipeFlow(phase);
            var before = flow.GetContents(dstCell);
            if (before.mass != dst.MassKg || (before.mass > 0 && (int)before.element != heldHash))
            { status = "拒绝：目标管道状态变化。缓冲保留。"; return; }
            if (before.diseaseCount < 0 || (before.diseaseCount > 0 && before.diseaseIdx == byte.MaxValue) ||
                (before.diseaseCount > 0 && germCount > 0 && before.diseaseIdx != germIndex))
            { status = "拒绝：病菌种类不同，无法核对病菌账目。样本保留。"; return; }
            if ((long)before.diseaseCount + germCount > int.MaxValue)
            { status = "拒绝：病菌计数将溢出。缓冲保留。"; return; }
            uncertainContext = "credit cell=" + dstCell + " world=" + Grid.WorldIdx[dstCell] +
                " element=" + heldHash + " kg=" + heldKg.ToString("R", Invariant);
            inFlight = true;
            try
            {
                float accepted = flow.AddElement(dstCell, element.id, heldKg,
                    temperature, germIndex, germCount);
                var after = flow.GetContents(dstCell);
                if (!PipeAccounting.TryCredit(heldKg, germCount, before.mass, after.mass,
                        before.diseaseCount, after.diseaseCount, accepted,
                        out float remaining, out int germsRemaining) ||
                    (after.mass > 0 && after.element != element.id) ||
                    (after.diseaseCount > 0 && after.diseaseIdx !=
                        (before.diseaseCount > 0 ? before.diseaseIdx : germIndex)))
                { Quarantine("管道投放账目无法确认"); return; }
                inFlight = false;
                uncertainContext = null;
                if (remaining == 0) { FinishEmit(returnToSource, temperature); return; }
                heldKg = remaining;
                germCount = germsRemaining;
                status = accepted == 0 ? "管道拒绝投放。样本保留。" :
                    "管道只接收了 " + DescribeDose(accepted) + "；已核对余量与病菌，缓冲保留。";
            }
            catch (Exception error) { Quarantine("管道投放异常", error); }
        }

        private void FinishEmit(bool returning, float temperature)
        {
            status = (returning ? "已退回：" : "已投放：") +
                DescribeDose(heldKg) + " · " + (temperature - 273.15).ToString("0.##", Invariant) + " °C";
            heldKg = 0;
            germCount = 0;
            originalCell = -1;
            originalPipe = false;
        }

        private static bool WithinPhase(Element element, float kelvin)
        {
            return !(element.lowTempTransition != null && kelvin <= element.lowTemp) &&
                !(element.highTempTransition != null && kelvin >= element.highTemp);
        }

        private static bool TryNumber(string s, out double number)
        {
            return (double.TryParse(s, NumberStyles.Float, Invariant, out number) ||
                    double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) &&
                   !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static string DescribeDose(float kg)
        {
            return kg < 1e-6f ? (kg * 1e9).ToString("0.###", Invariant) + " µg" :
                kg < 1e-3f ? (kg * 1e6).ToString("0.###", Invariant) + " mg" :
                kg < 1 ? (kg * 1e3).ToString("0.###", Invariant) + " g" :
                kg.ToString("0.###", Invariant) + " kg";
        }

        private static string RejectLabel(TransferRejection reason)
        {
            switch (reason)
            {
                case TransferRejection.SameCell: return "拒绝：起点和目标不能是同一格。";
                case TransferRejection.IncorrectSourcePhase: return "拒绝：起点元素不是所选的气体或液体。";
                case TransferRejection.IncompatibleTarget: return "拒绝：目标格有异种元素，不会覆盖。";
                case TransferRejection.InsufficientSource: return "拒绝：起点的量不足。";
                case TransferRejection.InvalidAmount: return "拒绝：气体范围 10 µg–1 kg；液体范围 1 mg–10 kg。";
                case TransferRejection.InvalidTemperature:
                case TransferRejection.TemperatureLocked: return "拒绝：温度无效或未解锁。";
                case TransferRejection.Precision: return "拒绝：该剂量无法精准记账；试着从质量更小的格子取样。";
                default: return "拒绝：格子状态无效。";
            }
        }
    }
}
