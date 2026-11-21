using System;
using System.Globalization;
using HarmonyLib;
using KSerialization;
using UnityEngine;

namespace OniPrecision
{
    internal static class PipetteText
    {
        internal static bool Chinese;
        internal static string Get(string english, string chinese) => Chinese ? chinese : english;
    }

    public sealed partial class PrecisionMod : KMod.UserMod2
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
                Debug.LogError("[YAPP] Required U60 hooks missing. Transfers disabled; keep the previous save backup.");
                return;
            }
            try
            {
                if (canSave == null || canSave.ReturnType != typeof(bool))
                {
                    // Still attach the serialized component to retain old buffers, but
                    // never enable mutations without the save guard.
                    harmony.Patch(install, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(Install)));
                    Debug.LogError("[YAPP] Save guard unavailable. Buffers retained; transfers disabled.");
                    return;
                }
                harmony.Patch(canSave, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(CheckSave)));
                harmony.Patch(install, postfix: new HarmonyMethod(typeof(PrecisionMod), nameof(Install)));
                InstallZoomFilter(harmony);
                Ready = true;
            }
            catch (Exception error)
            {
                Debug.LogError("[YAPP] Hook installation failed. Transfers disabled. " + error);
            }
        }

        static partial void InstallZoomFilter(Harmony harmony);
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
    public sealed partial class FluidPipette : KMonoBehaviour
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
        [SerializationConfig(MemberSerialization.OptIn)]
        private struct Reservoir
        {
            [Serialize] public float mass, fromK, toK;
            [Serialize] public int hash, germs, cell, world, version;
            [Serialize] public byte disease;
            [Serialize] public bool pipe;
        }
        [Serialize] private Reservoir spare;
        private void SelectReservoir(FluidPhase phase)
        {
            bool Matches(int hash) => ElementLoader.FindElementByHash((SimHashes)hash)?.IsGas == (phase == FluidPhase.Gas);
            if ((heldKg > 0 && !Matches(heldHash)) || (spare.mass > 0 && Matches(spare.hash)))
            {
                var next = spare;
                spare = new Reservoir { mass = heldKg, fromK = originalK, toK = outputK, hash = heldHash,
                    germs = germCount, disease = germIndex, cell = originalCell, world = originalWorld,
                    version = originVersion, pipe = originalPipe };
                heldKg = next.mass; originalK = next.fromK; outputK = next.toK; heldHash = next.hash;
                germCount = next.germs; germIndex = next.disease; originalCell = next.cell;
                originalWorld = next.world; originVersion = next.version; originalPipe = next.pipe;
            }
            mode = phase;
        }

        private object nativeSaveBlocker;
        private ClusterManager blockerOwner;

        // Persist quarantine if another save path serializes despite the guards.
        [Serialize] private bool mapPending;
        [Serialize] private bool uncertain;
        [Serialize] private string uncertainContext;
        private bool invalidBuffer;
        private bool tempered, inFlight, sourcePipe = true, targetPipe = true;
        private int source = -1, target = -1, unitIndex = 2;
        private FluidPhase mode = FluidPhase.Gas;
        private string massText = "10", temperatureText = "-25", status = PipetteText.Get("Select the source and destination cells.", "请选择起点和目标格。");
        private static string Micrograms => STRINGS.UI.UNITSUFFIXES.MASS.MICROGRAM.ToString().Trim();
        private static readonly string[] Units = { Micrograms, STRINGS.UI.UNITSUFFIXES.MASS.MILLIGRAM.ToString().Trim(),
            STRINGS.UI.UNITSUFFIXES.MASS.GRAM.ToString().Trim(), STRINGS.UI.UNITSUFFIXES.MASS.KILOGRAM.ToString().Trim() };
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
            if (mapPending) { uncertain = true; mapPending = false; }
            if (uncertain)
            {
                status = PipetteText.Get("Transfer result unknown. Reload a pre-transfer save without saving. ", "转移结果不确定：禁止重试和保存。请退出并读取操作前存档。 ") + uncertainContext;
                BlockNativeSave();
            }
            else if (invalidBuffer)
                status = PipetteText.Get("Invalid saved sample. Transfers disabled. Restore a backup.", "旧缓冲数据无效：原样保留并禁用转移；请恢复备份。");
            else if (heldKg > 0)
            {
                mode = ElementLoader.FindElementByHash((SimHashes)heldHash)?.IsLiquid == true
                    ? FluidPhase.Liquid : FluidPhase.Gas;
                status = originVersion == 1 ? PipetteText.Get("Sample restored. Select a compatible conduit to dispense or return it to its verified source.", "已恢复样本。请选择兼容管道投放，或退回已验证原管道。") :
                    PipetteText.Get("Legacy sample restored. Select a compatible conduit for delivery at the original temperature.", "已恢复旧缓冲。禁止自动退回；请选择兼容管道投放（可用原温）。");
            }
        }

        protected override void OnCleanUp()
        {
            DestroyPanel();
            if (Current == this) Current = null;
            if (nativeSaveBlocker != null && blockerOwner != null)
            {
                try { AccessTools.Method(typeof(ClusterManager), "RemoveSaveBlocker")?.Invoke(
                    blockerOwner, new[] { nativeSaveBlocker }); }
                catch (Exception error) { Debug.LogError("[YAPP] Save blocker cleanup: " + error); }
            }
            base.OnCleanUp();
        }

        private static bool ResearchDone()
        {
            return Db.Get()?.Techs?.TryGet(TechId)?.IsComplete() == true;
        }

        partial void DestroyPanel();

        private void Quarantine(string reason, Exception error = null)
        {
            uncertain = true;
            BlockNativeSave();
            inFlight = false;
            status = reason + PipetteText.Get(". Transfer result unknown. Exit without saving and reload a pre-transfer save.", "。结果不确定：禁止重试和保存；请退出并读取操作前存档。");
            Debug.LogError("[YAPP] " + status + " " + uncertainContext + " " + error);
        }

        private void BlockNativeSave()
        {
            if (nativeSaveBlocker != null || ClusterManager.Instance == null) return;
            try
            {
                blockerOwner = ClusterManager.Instance;
                nativeSaveBlocker = AccessTools.Method(typeof(ClusterManager), "AddSaveBlocker",
                    new[] { typeof(string) })?.Invoke(blockerOwner,
                    new object[] { "YAPP: uncertain transfer; reload pre-transfer save" });
            }
            catch (Exception error) { Debug.LogError("[YAPP] Native save blocker unavailable: " + error); }
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
            return kg < 1e-6f ? (kg * 1e9).ToString("0.###", Invariant) + " " + Micrograms :
                kg < 1e-3f ? (kg * 1e6).ToString("0.###", Invariant) + " " + Units[1] :
                kg < 1 ? (kg * 1e3).ToString("0.###", Invariant) + " " + Units[2] :
                kg.ToString("0.###", Invariant) + " " + Units[3];
        }

        private static string RejectLabel(TransferRejection reason)
        {
            switch (reason)
            {
                case TransferRejection.SameCell: return PipetteText.Get("Choose different source and destination cells.", "拒绝：起点和目标不能是同一格。");
                case TransferRejection.IncorrectSourcePhase: return PipetteText.Get("The source does not match the selected phase.", "拒绝：起点元素不是所选的气体或液体。");
                case TransferRejection.IncompatibleTarget: return PipetteText.Get("The target contains a different element.", "拒绝：目标格有异种元素，不会覆盖。");
                case TransferRejection.InsufficientSource: return PipetteText.Get("Insufficient mass at the source.", "拒绝：起点的量不足。");
                case TransferRejection.InvalidAmount: return PipetteText.Get("Gas range: 10 ", "拒绝：气体范围 10 ") + Micrograms + "–1 " + Units[3] + PipetteText.Get(". Liquid range: 1 ", "；液体范围 1 ") + Units[1] + "–10 " + Units[3] + "。";
                case TransferRejection.InvalidTemperature:
                case TransferRejection.TemperatureLocked: return PipetteText.Get("Temperature invalid or locked.", "拒绝：温度无效或未解锁。");
                case TransferRejection.Precision: return PipetteText.Get("This dose cannot be represented accurately. Try a lighter source.", "拒绝：该剂量无法精准记账；试着从质量更小的格子取样。");
                default: return PipetteText.Get("Invalid cell state.", "拒绝：格子状态无效。");
            }
        }
    }
}
