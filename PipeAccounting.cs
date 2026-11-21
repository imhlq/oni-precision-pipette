using System;
namespace OniPrecision
{
    // Exact checks against synchronous pipe snapshots. An unverified receipt never
    // authorizes retry, buffer clearing, or a successful save.
    public static class PipeAccounting
    {
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        public static bool ValidBuffer(float mass, float originalK, float outputK, int germs)
        {
            return Finite(mass) && mass >= 0 &&
                (mass == 0 ? true : germs >= 0 && Finite(originalK) && originalK > 0 && originalK <= 9999 &&
                 Finite(outputK) && outputK > 0 && outputK <= 9999);
        }
        public static bool ValidDebit(float before, float after, float removed,
            int germsBefore, int germsAfter, int germsRemoved)
        {
            return Finite(before) && Finite(after) && Finite(removed) &&
                before >= 0 && after >= 0 && removed >= 0 &&
                (double)before - after == removed && germsBefore >= 0 &&
                germsAfter >= 0 && germsRemoved >= 0 &&
                (long)germsBefore - germsAfter == germsRemoved &&
                (removed > 0 || germsRemoved == 0);
        }
        public static bool TryCredit(float held, int germs, float before, float after,
            int germsBefore, int germsAfter, float accepted, out float remaining, out int germsRemaining)
        {
            remaining = held; germsRemaining = germs;
            if (!Finite(held) || held <= 0 || !Finite(before) || before < 0 ||
                !Finite(after) || after < before || !Finite(accepted) || accepted < 0 || accepted > held ||
                (double)after - before != accepted || germs < 0 || germsBefore < 0 || germsAfter < germsBefore)
                return false;
            long deliveredGerms = (long)germsAfter - germsBefore;
            float rest = held - accepted;
            if ((double)held - rest != accepted || deliveredGerms > germs ||
                (accepted == 0 && deliveredGerms != 0) || (rest == 0 && deliveredGerms != germs))
                return false;
            remaining = rest; germsRemaining = germs - (int)deliveredGerms;
            return true;
        }
    }
}
