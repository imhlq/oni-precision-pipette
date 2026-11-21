using System;

namespace OniPrecision
{
    public enum FluidPhase { Vacuum, Gas, Liquid, Other }

    public enum TransferRejection
    {
        None, InvalidCell, SameCell, IncorrectSourcePhase, IncompatibleTarget,
        InvalidSample, InvalidAmount, InsufficientSource, TemperatureLocked,
        InvalidTemperature, Precision
    }

    public struct FluidSample
    {
        public readonly int Cell, Element;
        public readonly FluidPhase Phase;
        public readonly float MassKg, TemperatureK;

        public FluidSample(int cell, int element, FluidPhase phase, float massKg, float temperatureK)
        {
            Cell = cell;
            Element = element;
            Phase = phase;
            MassKg = massKg;
            TemperatureK = temperatureK;
        }
    }

    /// <summary>Pure validation and numerical planning; does not issue a simulation command.</summary>
    public struct TransferPlan
    {
        public const double MinGasKg = 1e-8, MaxGasKg = 1;
        public const double MinLiquidKg = 1e-6, MaxLiquidKg = 10;
        public const double RelativeTolerance = 0.05;

        public readonly float AmountKg, SourceAfterKg, TargetAfterKg;
        public readonly float TransferTemperatureK, TargetAfterTemperatureK;

        private TransferPlan(float amount, float sourceAfter, float targetAfter, float blobK,
            float targetK)
        {
            AmountKg = amount;
            SourceAfterKg = sourceAfter;
            TargetAfterKg = targetAfter;
            TransferTemperatureK = blobK;
            TargetAfterTemperatureK = targetK;
        }

        public static bool CanFitPipe(float existingKg, float additionKg, FluidPhase phase)
        {
            double capacity;
            switch (phase)
            {
                case FluidPhase.Gas: capacity = MaxGasKg; break;
                case FluidPhase.Liquid: capacity = MaxLiquidKg; break;
                default: return false;
            }

            return CanFitPipe(existingKg, additionKg, capacity);
        }

        public static bool CanFitPipe(float existingKg, float additionKg, double capacity)
        {
            if (!Finite(existingKg) || existingKg < 0 || !Finite(capacity) ||
                !Finite(additionKg) || additionKg <= 0)
                return false;

            // Float addition could round over-capacity values down to the cap.
            // Subtract first to avoid losing tiny positive additions at the cap.
            return existingKg < capacity && additionKg <= capacity - existingKg;
        }

        public static bool TryCreate(FluidSample source, FluidSample target,
            FluidPhase desiredPhase, double requestedKg, bool temperatureUnlocked,
            double? targetCelsius, out TransferPlan plan, out TransferRejection rejection, bool calibrated = false)
        {
            plan = default(TransferPlan);
            if (source.Cell < 0 || target.Cell < 0)
                return Reject(TransferRejection.InvalidCell, out rejection);
            if (source.Cell == target.Cell)
                return Reject(TransferRejection.SameCell, out rejection);
            if ((desiredPhase != FluidPhase.Gas && desiredPhase != FluidPhase.Liquid) ||
                source.Phase != desiredPhase)
                return Reject(TransferRejection.IncorrectSourcePhase, out rejection);
            if (target.Phase != FluidPhase.Vacuum &&
                (target.Phase != desiredPhase || target.Element != source.Element))
                return Reject(TransferRejection.IncompatibleTarget, out rejection);

            bool empty = target.Phase == FluidPhase.Vacuum;
            if (!Finite(source.MassKg) || source.MassKg <= 0 ||
                !Finite(target.MassKg) || (empty ? target.MassKg != 0 : target.MassKg <= 0) ||
                !ValidKelvin(source.TemperatureK) || (!empty && !ValidKelvin(target.TemperatureK)))
                return Reject(TransferRejection.InvalidSample, out rejection);

            double min = calibrated ? 1e-9 : desiredPhase == FluidPhase.Gas ? MinGasKg : MinLiquidKg;
            double max = desiredPhase == FluidPhase.Gas ? MaxGasKg : MaxLiquidKg;
            if (!Finite(requestedKg) || requestedKg < min || requestedKg > max)
                return Reject(TransferRejection.InvalidAmount, out rejection);
            // A decimal amount captured in one float can round slightly below the request.
            // Comparing against its float representation doesn't forgive a real shortage.
            if (source.MassKg < (float)requestedKg)
                return Reject(TransferRejection.InsufficientSource, out rejection);

            float blobK = source.TemperatureK;
            if (targetCelsius.HasValue)
            {
                if (!temperatureUnlocked)
                    return Reject(TransferRejection.TemperatureLocked, out rejection);
                double requestedK = targetCelsius.Value + 273.15;
                if (!ValidKelvin(requestedK))
                    return Reject(TransferRejection.InvalidTemperature, out rejection);
                blobK = (float)requestedK;
            }

            float sourceAfter = 0, targetAfter = 0, amount = 0;
            double bestError = double.PositiveInfinity;
            float roundedDose = (float)requestedKg;
            float roundedSource = Math.Max(0f, (float)((double)source.MassKg - requestedKg));
            float roundedTarget = (float)((double)target.MassKg + requestedKg);

            // There are three separate float quantities to respect. Check the raw request
            // (as a float), plus the doses implied by adjacent representable source/target
            // masses. A single float dose must debit and credit the exact same amount.
            // Validate the dose itself, not only the stored end states: the adapter may need
            // to tell the sim to consume and add the dose rather than replace entire cells.
            Candidate(roundedDose, source.MassKg, target.MassKg, requestedKg,
                ref sourceAfter, ref targetAfter, ref amount, ref bestError);
            for (int offset = -2; offset <= 2; ++offset)
            {
                float nearbySource = Neighbor(roundedSource, offset);
                float nearbyTarget = Neighbor(roundedTarget, offset);
                if (Finite(nearbySource))
                    Candidate((float)((double)source.MassKg - nearbySource),
                        source.MassKg, target.MassKg, requestedKg,
                        ref sourceAfter, ref targetAfter, ref amount, ref bestError);
                if (Finite(nearbyTarget))
                    Candidate((float)((double)nearbyTarget - target.MassKg),
                        source.MassKg, target.MassKg, requestedKg,
                        ref sourceAfter, ref targetAfter, ref amount, ref bestError);
            }
            if (amount == 0 || bestError > requestedKg * RelativeTolerance ||
                amount > max)
                return Reject(TransferRejection.Precision, out rejection);

            double combinedK = empty ? blobK :
                ((double)target.MassKg * target.TemperatureK + (double)amount * blobK) /
                ((double)target.MassKg + amount);
            plan = new TransferPlan(amount, sourceAfter, targetAfter, blobK, (float)combinedK);
            rejection = TransferRejection.None;
            return true;
        }

        private static void Candidate(float candidate, float source, float target, double requested,
            ref float sourceAfter, ref float targetAfter, ref float amount, ref double bestError)
        {
            if (!Finite(candidate) || candidate <= 0 || candidate > source)
                return;

            double expectedSource = (double)source - candidate;
            double expectedTarget = (double)target + candidate;
            float nextSource = (float)expectedSource;
            float nextTarget = (float)expectedTarget;
            if (!Finite(nextTarget) || nextTarget <= target || nextSource >= source)
                return;
            // Comparing deltas rather than total verifies the tiny dose is not rounded away.
            if ((double)source - nextSource != candidate || (double)nextTarget - target != candidate)
                return;

            double error = Math.Abs((double)candidate - requested);
            if (error >= bestError)
                return;
            sourceAfter = nextSource;
            targetAfter = nextTarget;
            amount = candidate;
            bestError = error;
        }

        private static float Neighbor(float nonnegative, int offset)
        {
            if (!Finite(nonnegative) || nonnegative < 0)
                return float.NaN;
            if (offset == 0)
                return nonnegative;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(nonnegative == 0 ? 0f : nonnegative), 0);
            long stepped = (long)bits + offset;
            if (stepped < 0 || stepped >= 0x7F800000L)
                return float.NaN;
            return BitConverter.ToSingle(BitConverter.GetBytes((int)stepped), 0);
        }

        private static bool ValidKelvin(double v) { return Finite(v) && v > 0 && v <= 9999; }
        private static bool Finite(double v) { return !double.IsInfinity(v) && !double.IsNaN(v); }
        private static bool Reject(TransferRejection reason, out TransferRejection rejection)
        {
            rejection = reason;
            return false;
        }
    }
}
