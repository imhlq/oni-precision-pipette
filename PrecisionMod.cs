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
            base.OnLoad(harmony);
            harmony.PatchAll(typeof(PrecisionMod).Assembly);
        }
    }

    [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
    internal static class InstallPanel
    {
        private static void Postfix(SaveGame __instance)
        {
            if (__instance.GetComponent<FluidPipette>() == null)
                __instance.gameObject.AddComponent<FluidPipette>();
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.CanSave))]
    internal static class PreventSaveWhileInFlight
    {
        private static void Postfix(ref bool __result)
        {
            if (FluidPipette.Current != null && FluidPipette.Current.InFlight)
                __result = false;
        }
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class FluidPipette : KMonoBehaviour
    {
        internal static FluidPipette Current { get; private set; }
        internal bool InFlight => inFlight;
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

        private bool expanded, tempered, inFlight, sourcePipe, targetPipe;
        private int source = -1, target = -1, picking, unitIndex, emissionEpoch;
        private FluidPhase mode = FluidPhase.Gas;
        private string massText = "50", temperatureText = "-25", status = "请选择起点和目标格。";
        private Rect panel = new Rect(16, 110, 448, 0);
        private static readonly string[] Units = { "µg", "mg", "g", "kg" };
        private static readonly double[] KgPerUnit = { 1e-9, 1e-6, 1e-3, 1 };

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Current = this;
            if (heldKg > 0)
            {
                mode = ElementLoader.FindElementByHash((SimHashes)heldHash)?.IsLiquid == true
                    ? FluidPhase.Liquid : FluidPhase.Gas;
                status = "已恢复上次未投放的样本。请选目标重试，或退回原处。";
            }
        }

        protected override void OnCleanUp()
        {
            if (Current == this) Current = null;
            emissionEpoch++;
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
            var enabled = GUI.enabled;
            GUI.enabled = !inFlight && !held;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(mode == FluidPhase.Gas, "移气管", "Button") && mode != FluidPhase.Gas)
                SetMode(FluidPhase.Gas);
            if (GUILayout.Toggle(mode == FluidPhase.Liquid, "移液管", "Button") && mode != FluidPhase.Liquid)
                SetMode(FluidPhase.Liquid);
            GUILayout.EndHorizontal();

            DrawEndpointRow(true);
            GUI.enabled = !inFlight;
            DrawEndpointRow(false);
            GUI.enabled = !inFlight && !held;
            GUILayout.BeginHorizontal();
            GUILayout.Label("剂量", GUILayout.Width(60));
            massText = GUILayout.TextField(massText, 20, GUILayout.Width(90));
            unitIndex = GUILayout.SelectionGrid(unitIndex, Units, Units.Length);
            GUILayout.EndHorizontal();

            GUI.enabled = GUI.enabled && unlocked;
            tempered = GUILayout.Toggle(tempered && unlocked,
                unlocked ? "指定本次投放温度" : "变温未解锁：完成温度调节研究");
            if (tempered && unlocked)
            {
                GUILayout.BeginHorizontal();
                temperatureText = GUILayout.TextField(temperatureText, 16, GUILayout.Width(100));
                GUILayout.Label("°C；变温单次最多 20 g");
                GUILayout.EndHorizontal();
            }

            GUI.enabled = !inFlight && !held;
            if (GUILayout.Button("执行一次")) StartTransfer();
            GUI.enabled = !inFlight;
            if (held)
            {
                GUILayout.Label("缓冲中：" + DescribeDose(heldKg));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("投放到所选目标")) TryEmit(target, targetPipe, false);
                if (GUILayout.Button("原温退回原格")) TryEmit(originalCell, originalPipe, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(picking == 0 ? status : (picking == 1 ? "点击地图起点格" : "点击地图目标格") + "（Esc 取消）");
            if (GUILayout.Button("收起")) { expanded = false; picking = 0; }
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
            int kind = GUILayout.SelectionGrid(pipe ? 1 : 0, new[] { "地图", "管道" }, 2,
                GUILayout.Width(125));
            if ((kind == 1) != pipe)
            {
                if (isSource) { sourcePipe = kind == 1; source = -1; }
                else { targetPipe = kind == 1; target = -1; }
                picking = 0;
            }
            if (GUILayout.Button(CellLabel(isSource ? source : target, kind == 1)))
                picking = isSource ? 1 : 2;
            GUILayout.EndHorizontal();
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
            if (inFlight || heldKg > 0) return;
            if (!Running()) { status = "请先解除暂停；暂停时不会扣取或投放。"; return; }
            if (!ReadEndpoint(source, sourcePipe, mode, out var src) || !ReadEndpoint(target, targetPipe, mode, out var dst))
            { status = "拒绝：起点或目标无效。"; return; }
            if (!TryNumber(massText, out var dose))
            { status = "拒绝：剂量不是有效数值。"; return; }
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
            bool dstPipe = targetPipe;
            if (sourcePipe)
            {
                var flow = PipeFlow(mode);
                // Both operations run on Unity's main thread; use the amount actually removed.
                var removed = flow.RemoveElement(srcCell, plan.AmountKg);
                if (!(removed.mass > 0f)) { status = "没有取到管内样本；未投放。"; return; }
                Buffer(removed.mass, removed.temperature,
                    setC.HasValue ? plan.TransferTemperatureK : removed.temperature,
                    (int)removed.element, removed.diseaseIdx, removed.diseaseCount, srcCell, true);
                if (heldHash != src.Element ||
                    Math.Abs(heldKg - plan.AmountKg) > plan.AmountKg * 0.0001)
                { status = "管内实际取量或元素有变化。已保留样本，请核对后处理。"; return; }
                TryEmit(dstCell, dstPipe, false);
                return;
            }
            int ticket = ++emissionEpoch;
            inFlight = true;
            status = "等待模拟器确认取样……";

            var handle = Game.Instance.massConsumedCallbackManager.Add((info, _) =>
            {
                if (this == null || Current != this || ticket != emissionEpoch) return;
                if (!(info.mass > 0f))
                { inFlight = false; status = "没有取到样本；未投放。"; return; }

                Buffer(info.mass, info.temperature, setC.HasValue ? plan.TransferTemperatureK : info.temperature,
                    (int)ElementLoader.elements[(int)info.elemIdx].id, info.diseaseIdx, info.diseaseCount, srcCell, false);
                inFlight = false;
                if (heldHash != src.Element ||
                    Math.Abs(heldKg - plan.AmountKg) > plan.AmountKg * 0.0001)
                { status = "实际取量或元素与计划不同。已保留样本，请核对后处理。"; return; }

                TryEmit(dstCell, dstPipe, false);
            }, null, nameof(FluidPipette));
            SimMessages.ConsumeMass(srcCell, element.id, plan.AmountKg, (byte)1, (byte)1, handle.index);
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
        }

        // No ReplaceElement, destructive displacement, adjacent search, or credit before a native debit.
        private void TryEmit(int dstCell, bool dstPipe, bool returnToSource)
        {
            if (inFlight || !(heldKg > 0f)) return;
            if (!Running()) { status = "模拟已暂停。样本保留，解除暂停后可重试。"; return; }
            var element = ElementLoader.FindElementByHash((SimHashes)heldHash);
            float temperature = returnToSource ? originalK : outputK;
            if (element == null || (!element.IsGas && !element.IsLiquid) ||
                !(temperature > 0) || !WithinPhase(element, temperature))
            { status = "拒绝：样本状态或温度无效。"; return; }
            FluidPhase phase = element.IsGas ? FluidPhase.Gas : FluidPhase.Liquid;
            if (!ReadEndpoint(dstCell, dstPipe, phase, out var dst) ||
                (!returnToSource && dstCell == originalCell && dstPipe == originalPipe) ||
                (dst.Phase != FluidPhase.Vacuum && (dst.Element != heldHash || dst.Phase != phase)))
            { status = "拒绝：目标须为空或同元素。样本保留，未改投附近。"; return; }
            if ((double)(float)((double)dst.MassKg + heldKg) - dst.MassKg != heldKg)
            { status = "拒绝：目标质量太大，无法准确接收该剂量。样本保留。"; return; }

            if (dstPipe)
            {
                if (!TransferPlan.CanFitPipe(dst.MassKg, heldKg, phase))
                { status = "拒绝：目标管道没有足够空间。样本保留。"; return; }
                float before = heldKg;
                float accepted = PipeFlow(phase).AddElement(dstCell, element.id, before,
                    temperature, germIndex, germCount);
                if (!(accepted > 0f))
                { status = "管道拒绝投放。样本保留，可重试或退回。"; return; }
                if (accepted < before)
                {
                    heldKg = before - accepted;
                    germCount -= (int)Math.Round(germCount * ((double)accepted / before));
                    status = "管道只接收了 " + DescribeDose(accepted) + "；余量保留。";
                    return;
                }
                FinishEmit(returnToSource, temperature);
                return;
            }

            // Recheck the exact selected cell immediately before handing off to the native
            // emitter, which reports refusal via its completion callback.
            int ticket = ++emissionEpoch;
            inFlight = true;
            status = "等待模拟器确认投放……";
            var handle = Game.Instance.massEmitCallbackManager.Add((info, _) =>
            {
                if (this == null || Current != this || ticket != emissionEpoch) return;
                inFlight = false;
                if (info.suceeded != 0)
                {
                    FinishEmit(returnToSource, temperature);
                }
                else status = "模拟器拒绝投放。样本保留，可重试或退回。";
            }, null, nameof(FluidPipette));
            SimMessages.EmitMass(dstCell, ElementLoader.GetElementIndex(element.id), heldKg,
                temperature, germIndex, germCount, handle.index);
        }

        private void FinishEmit(bool returning, float temperature)
        {
            status = (returning ? "已退回：" : "已投放：") +
                DescribeDose(heldKg) + " · " + (temperature - 273.15).ToString("0.##", Invariant) + " °C";
            heldKg = 0;
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
