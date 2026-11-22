using System;
using UnityEngine;

namespace OniPrecision
{
    public sealed partial class FluidPipette
    {
        // U60 uses its private MaxMass value inside AddElement.
        private static readonly System.Reflection.FieldInfo CapacityField =
            typeof(ConduitFlow).GetField("MaxMass", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        private static double PipeCapacity(FluidPhase phase)
        {
            if (CapacityField?.GetValue(PipeFlow(phase)) is float capacity) return capacity;
            return phase == FluidPhase.Gas ? TransferPlan.MaxGasKg : TransferPlan.MaxLiquidKg;
        }

        private static bool VisibleCell(int cell)
        {
            return Grid.IsValidCell(cell) && Grid.IsVisible(cell) &&
                ClusterManager.Instance != null && Grid.WorldIdx[cell] == ClusterManager.Instance.activeWorldId;
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

        internal bool PlanPickup(int cell, bool pipe, FluidPhase phase, float capacity,
            out FluidSample sample, out float dose, out TransferPlan plan)
        {
            sample = default;
            plan = default;
            dose = 0;
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || heldKg >= capacity ||
                !ReadEndpoint(cell, pipe, phase, out sample) || sample.Phase != phase ||
                !(sample.MassKg > 0) || !(sample.TemperatureK > 0)) return false;
            if (heldKg > 0 && (sample.Element != heldHash || originalK != outputK)) return false;
            double requested = Math.Min((double)capacity - heldKg, sample.MassKg);
            dose = (float)requested;
            if (pipe)
            {
                var buffer = new FluidSample(int.MaxValue, heldHash, heldKg > 0 ? phase : FluidPhase.Vacuum,
                    heldKg, originalK);
                return TransferPlan.TryCreate(sample, buffer, phase, requested, false, null,
                    out plan, out _, calibrated: true);
            }
            return dose > 0 && (heldKg == 0 || (heldKg + dose > heldKg && heldKg + dose > dose));
        }

        private static bool Running()
        {
            return SpeedControlScreen.Instance != null && !SpeedControlScreen.Instance.IsPaused;
        }

        private string PlanTransfer(out TransferPlan plan, out FluidSample src)
        {
            plan = default;
            src = default;
            if (!sourcePipe || !targetPipe)
            return PipetteText.Get("Choose a conduit to transfer from this panel.", "拒绝：地图转移已禁用；尚未取样。请选择管道。");
            if (!ReadEndpoint(source, sourcePipe, mode, out src) || !ReadEndpoint(target, targetPipe, mode, out var dst))
            return PipetteText.Get("Invalid source or target.", "拒绝：起点或目标无效。");
            if (!TryNumber(massText, out var dose))
            return PipetteText.Get("Invalid dose.", "拒绝：剂量不是有效数值。");
            if ((uint)unitIndex >= (uint)KgPerUnit.Length)
            return PipetteText.Get("Select a dose unit.", "拒绝：请明确选择剂量单位。");
            dose *= KgPerUnit[unitIndex];
            double? setC = null;
            bool unlock = ResearchDone();
            if (tempered && !OverTemperatureLimit((float)dose))
            {
                if (!unlock || !TryNumber(temperatureText, out var temp))
                return PipetteText.Get("Temperature control is locked or the temperature is invalid.", "拒绝：变温未解锁或温度无效。");
                setC = temp;
            }

            if (!TransferPlan.TryCreate(src, dst, mode, dose, unlock, setC, out plan, out var reason))
            return RejectLabel(reason);

            if (!TransferPlan.CanFitPipe(dst.MassKg, plan.AmountKg, PipeCapacity(mode)))
            return PipetteText.Get("Insufficient conduit capacity. No sample taken.", "拒绝：目标管道没有足够空间。未取样。");

            var element = ElementLoader.FindElementByHash((SimHashes)src.Element);
            if (element == null) return PipetteText.Get("Unknown source element.", "拒绝：无法识别源元素。");
            if (setC.HasValue && !WithinPhase(element, plan.TransferTemperatureK))
            return PipetteText.Get("Requested temperature would change the phase.", "拒绝：设定温度会使该元素发生相变。");
            return null;
        }

        private void StartTransfer()
        {
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || heldKg > 0) return;
            string refusal = PlanTransfer(out var plan, out var src);
            if (refusal != null)
            {
                status = refusal;
                return;
            }
            if (TakePipeSample(source, src, plan)) TryEmit(target, true, false);
        }

        private bool TakePipeSample(int srcCell, FluidSample src, TransferPlan plan)
        {
            var flow = PipeFlow(mode);
            var before = flow.GetContents(srcCell);
            if (before.mass != src.MassKg || (int)before.element != src.Element ||
                before.temperature != src.TemperatureK || before.diseaseCount < 0 ||
                (before.diseaseCount > 0 && before.diseaseIdx == byte.MaxValue))
            { status = PipetteText.Get("Source contents changed. No sample taken.", "拒绝：源管道状态或病菌数据变化。未取样。"); return false; }
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
                { Quarantine(PipetteText.Get("Unconfirmed conduit debit", "管道取样账目无法确认")); return false; }
                if (removed.mass == 0)
                { inFlight = false; uncertainContext = null; status = PipetteText.Get("No sample removed from the conduit.", "管道未取出样本。"); return false; }
                float priorMass = heldKg;
                Buffer(removed.mass, removed.temperature,
                    plan.TransferTemperatureK,
                    (int)removed.element, removed.diseaseIdx, removed.diseaseCount, srcCell, true);
                inFlight = false;
                uncertainContext = null;
                if (heldHash != src.Element || heldKg - priorMass != plan.AmountKg)
                { status = PipetteText.Get("Actual pickup differs from plan. Sample retained for return.", "实际取样与计划不同；样本保留，可退回。"); return false; }
            }
            catch (Exception error) { Quarantine(PipetteText.Get("Conduit pickup error", "管道取样异常"), error); return false; }
            return true;
        }

        private void Buffer(float mass, float fromK, float toK, int hash, byte germ, int count,
            int cell, bool pipe)
        {
            if (heldKg > 0)
            {
                if (hash != heldHash) throw new InvalidOperationException("Sample elements differ");
                float total = heldKg + mass;
                if (total <= heldKg || total <= mass) throw new InvalidOperationException("Unrepresentable sample total");
                if (count > int.MaxValue - germCount) throw new OverflowException("Sample germ count overflow");
                var disease = Klei.SimUtil.CalculateFinalDiseaseInfo(germIndex, germCount, germ, count);
                germIndex = disease.idx;
                germCount = disease.count;
                originalK = (float)(((double)heldKg * originalK + (double)mass * fromK) / total);
                outputK = originalK;
                heldKg = total;
                if (originalCell != cell || originalPipe != pipe) originVersion = 0;
                return;
            }
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
        private string CheckEmission(int dstCell, bool dstPipe, bool returnToSource, bool originalTemperature,
            out Element element, out float temperature, out FluidSample dst, out int beforeGerms, out byte beforeDisease)
        {
            element = null; temperature = 0; dst = default; beforeGerms = 0; beforeDisease = byte.MaxValue;
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || !(heldKg > 0f)) return PipetteText.Get("No sample to dispense.", "当前无可投放的样本。");
            if (returnToSource && (originVersion != 1 || !Grid.IsValidCell(originalCell) ||
                Grid.WorldIdx[originalCell] != originalWorld))
            return PipetteText.Get("Unknown origin. Choose a compatible conduit and use the original temperature.", "拒绝：旧缓冲或原世界无法确认。请选择兼容管道按原温投放。");
            if (!dstPipe)
            return PipetteText.Get("Choose a compatible conduit. Sample retained.", "拒绝：地图投放和原格退回已禁用。缓冲保留；可投放至兼容管道。");
            if (!returnToSource && !originalTemperature && outputK != originalK && !ResearchDone())
            return PipetteText.Get("Changing temperature requires research. Return at the original temperature.", "拒绝：变温需研究解锁。可按原温退回管道。");
            element = ElementLoader.FindElementByHash((SimHashes)heldHash);
            temperature = returnToSource || originalTemperature || OverTemperatureLimit(heldKg) ? originalK : outputK;
            if (germCount < 0 || (germCount > 0 && germIndex == byte.MaxValue) ||
                element == null || (!element.IsGas && !element.IsLiquid) ||
                !(temperature > 0) || !WithinPhase(element, temperature))
            return PipetteText.Get("Invalid sample state or temperature.", "拒绝：样本状态或温度无效。");
            FluidPhase phase = element.IsGas ? FluidPhase.Gas : FluidPhase.Liquid;
            if (!ReadEndpoint(dstCell, dstPipe, phase, out dst) ||
                (!returnToSource && dstCell == originalCell && dstPipe == originalPipe) ||
                (dst.Phase != FluidPhase.Vacuum && (dst.Element != heldHash || dst.Phase != phase)))
            return PipetteText.Get("Target must be empty or contain the same element. Sample retained.", "拒绝：目标须为空或同元素。样本保留，未改投附近。");
            if ((double)(float)((double)dst.MassKg + heldKg) - dst.MassKg != heldKg)
            return PipetteText.Get("Target mass too large for an accurate transfer. Sample retained.", "拒绝：目标质量太大，无法准确接收该剂量。样本保留。");

            if (!TransferPlan.CanFitPipe(dst.MassKg, heldKg, PipeCapacity(phase)))
            return PipetteText.Get("Insufficient conduit capacity. Sample retained.", "拒绝：目标管道没有足够空间。样本保留。");
            var flow = PipeFlow(phase);
            var before = flow.GetContents(dstCell);
            if (before.mass != dst.MassKg || (before.mass > 0 && (int)before.element != heldHash))
            return PipetteText.Get("Conduit contents changed. Sample retained.", "拒绝：目标管道状态变化。缓冲保留。");
            if (before.diseaseCount < 0 || (before.diseaseCount > 0 && before.diseaseIdx == byte.MaxValue) ||
                (before.diseaseCount > 0 && germCount > 0 && before.diseaseIdx != germIndex))
            return PipetteText.Get("Different germ types. Sample retained.", "拒绝：病菌种类不同，无法核对病菌账目。样本保留。");
            if ((long)before.diseaseCount + germCount > int.MaxValue)
            return PipetteText.Get("Germ count would overflow. Sample retained.", "拒绝：病菌计数将溢出。缓冲保留。");
            beforeGerms = before.diseaseCount;
            beforeDisease = before.diseaseIdx;
            return null;
        }

        internal string DropRefusal(int cell, bool pipe, FluidPhase phase, out FluidSample target)
        {
            target = default;
            var held = ElementLoader.FindElementByHash((SimHashes)heldHash);
            if (!PrecisionMod.Ready || InFlight || invalidBuffer || heldKg <= 0 || held == null ||
                (phase == FluidPhase.Gas ? !held.IsGas : !held.IsLiquid)) return PipetteText.Get("Sample unavailable", "样本不可用");
            if (pipe) return CheckEmission(cell, true, originalPipe && cell == originalCell, true,
                out _, out _, out target, out _, out _);
            if (!ReadEndpoint(cell, false, phase, out target)) return PipetteText.Get("Target cell unavailable", "目标格不可用");
            if (target.Phase != FluidPhase.Vacuum && target.Element != heldHash && !(held.IsLiquid && target.Phase == FluidPhase.Gas))
                return PipetteText.Get("Target contains another element", "目标格已有其他物质");
            if (target.Element == heldHash && target.MassKg + heldKg == target.MassKg)
                return PipetteText.Get("Dose is too small to increase the target mass.", "该剂量太小，无法增加目标格质量。");
            return null;
        }

        private void TryEmit(int dstCell, bool dstPipe, bool returnToSource, bool originalTemperature = false)
        {
            EmitChecked(dstCell,dstPipe,returnToSource,originalTemperature,null);
        }

        private void EmitChecked(int dstCell,bool dstPipe,bool returnToSource,bool originalTemperature,float? configuredK)
        {
            string refusal = CheckEmission(dstCell, dstPipe, returnToSource, originalTemperature,
                out var element, out float temperature, out var dst, out int beforeGerms, out byte beforeDisease);
            if (refusal != null) { status = refusal; return; }
            if(configuredK.HasValue) temperature=configuredK.Value;
            var flow = PipeFlow(element.IsGas ? FluidPhase.Gas : FluidPhase.Liquid);
            uncertainContext = "credit cell=" + dstCell + " world=" + Grid.WorldIdx[dstCell] +
                " element=" + heldHash + " kg=" + heldKg.ToString("R", Invariant);
            inFlight = true;
            try
            {
                float accepted = flow.AddElement(dstCell, element.id, heldKg,
                    temperature, germIndex, germCount);
                var after = flow.GetContents(dstCell);
                if (!PipeAccounting.TryCredit(heldKg, germCount, dst.MassKg, after.mass,
                        beforeGerms, after.diseaseCount, accepted,
                        out float remaining, out int germsRemaining) ||
                    (after.mass > 0 && after.element != element.id) ||
                    (after.diseaseCount > 0 && after.diseaseIdx !=
                        (beforeGerms > 0 ? beforeDisease : germIndex)))
                { Quarantine(PipetteText.Get("Unconfirmed conduit delivery", "管道投放账目无法确认")); return; }
                inFlight = false;
                uncertainContext = null;
                if (remaining == 0) { FinishEmit(returnToSource, temperature); return; }
                heldKg = remaining;
                germCount = germsRemaining;
                status = accepted == 0 ? PipetteText.Get("Conduit refused delivery. Sample retained.", "管道拒绝投放。样本保留。") :
                    PipetteText.Get("Conduit accepted only ", "管道只接收了 ") + DescribeDose(accepted) + PipetteText.Get(". Remaining mass and germs retained.", "；已核对余量与病菌，缓冲保留。");
            }
            catch (Exception error) { Quarantine(PipetteText.Get("Conduit delivery error", "管道投放异常"), error); }
        }

        private void FinishEmit(bool returning, float temperature)
        {
            status = (returning ? PipetteText.Get("Returned: ", "已退回：") : PipetteText.Get("Delivered: ", "已投放：")) +
                DescribeDose(heldKg) + " · " + (temperature - 273.15).ToString("0.##", Invariant) + " °C";
            heldKg = 0;
            germCount = 0;
            originalCell = -1;
            originalPipe = false;
        }

    }
}
