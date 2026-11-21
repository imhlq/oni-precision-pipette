using System;
using OniPrecision;

static class TransferPlanTests
{
    private const int Hydrogen = -10;
    private const int Oxygen = -11;
    private const int Water = -12;
    private const int Oil = -13;
    private static readonly FluidSample Vacuum = new FluidSample(2, 0, FluidPhase.Vacuum, 0, 0);
    private static readonly FluidSample H = new FluidSample(1, Hydrogen, FluidPhase.Gas, .01f, 300f);
    private static readonly FluidSample W = new FluidSample(1, Water, FluidPhase.Liquid, 2, 300f);
    private static int cases;

    static void Main(string[] args)
    {
        AccountingTests();
        var micro = Accept(H, Vacuum, FluidPhase.Gas, 50e-9, true, -25);
        Assert(Math.Abs(micro.AmountKg - 50e-9) <= 50e-9 * .05, "50 µg tolerance");
        Assert(Math.Abs(micro.TransferTemperatureK - 248.15) < .001, "°C to K");
        Assert(micro.TargetAfterTemperatureK == micro.TransferTemperatureK, "vacuum output temperature");

        var normal = Accept(W, Vacuum, FluidPhase.Liquid, .03, false, null);
        Assert(normal.TransferTemperatureK == 300, "normal mode preserves input");
        Accept(new FluidSample(1, Hydrogen, FluidPhase.Gas, 50e-9f, 300),
            Vacuum, FluidPhase.Gas, 50e-9, false, null);

        var warmTarget = new FluidSample(2, Water, FluidPhase.Liquid, .5f, 350);
        var mixed = Accept(W, warmTarget, FluidPhase.Liquid, .5, true, 0);
        Assert(Math.Abs(mixed.TargetAfterTemperatureK - 311.575) < .001, "same-element thermal mixing");

        Reject(H, new FluidSample(2, Oxygen, FluidPhase.Gas, .02f, 285), FluidPhase.Gas,
            50e-9, false, null, TransferRejection.IncompatibleTarget);
        Reject(W, new FluidSample(2, Oil, FluidPhase.Liquid, 1, 300), FluidPhase.Liquid,
            .03, false, null, TransferRejection.IncompatibleTarget);
        Reject(H, new FluidSample(2, 8, FluidPhase.Other, 1, 280), FluidPhase.Gas,
            50e-9, false, null, TransferRejection.IncompatibleTarget);
        Reject(H, H, FluidPhase.Gas, 50e-9, false, null, TransferRejection.SameCell);
        Reject(H, Vacuum, FluidPhase.Liquid, .01, false, null, TransferRejection.IncorrectSourcePhase);
        Reject(H, Vacuum, FluidPhase.Gas, 50e-9, false, -25, TransferRejection.TemperatureLocked);
        Reject(H, Vacuum, FluidPhase.Gas, 50e-9, true, -274, TransferRejection.InvalidTemperature);
        Reject(H, Vacuum, FluidPhase.Gas, 50e-9, true, double.NaN, TransferRejection.InvalidTemperature);
        Reject(H, Vacuum, FluidPhase.Gas, 0, false, null, TransferRejection.InvalidAmount);
        Reject(W, Vacuum, FluidPhase.Liquid, 100, false, null, TransferRejection.InvalidAmount);
        Reject(W, Vacuum, FluidPhase.Liquid, 3, false, null, TransferRejection.InsufficientSource);
        Reject(new FluidSample(-1, Hydrogen, FluidPhase.Gas, .2f, 300), Vacuum,
            FluidPhase.Gas, 50e-9, false, null, TransferRejection.InvalidCell);
        Reject(new FluidSample(1, Hydrogen, FluidPhase.Gas, 1, 300), Vacuum,
            FluidPhase.Gas, 50e-9, false, null, TransferRejection.Precision);
        Reject(H, new FluidSample(2, Hydrogen, FluidPhase.Gas, 1, 300),
            FluidPhase.Gas, 50e-9, false, null, TransferRejection.Precision);
        Reject(H, new FluidSample(2, 0, FluidPhase.Vacuum, .01f, 0),
            FluidPhase.Gas, 50e-9, false, null, TransferRejection.InvalidSample);

        Fit(.75f, .25f, FluidPhase.Gas, true);
        Fit(9.5f, .5f, FluidPhase.Liquid, true);
        Fit(0f, 1f, FluidPhase.Gas, true);
        Fit(0f, 10f, FluidPhase.Liquid, true);
        Fit(.75f, .250001f, FluidPhase.Gas, false);
        Fit(9.5f, .500001f, FluidPhase.Liquid, false);
        Fit(1f, 1e-9f, FluidPhase.Gas, false);
        Fit(10f, 1e-6f, FluidPhase.Liquid, false);
        Fit(10f - 1f / 1048576f, 1e-6f, FluidPhase.Liquid, false);
        Fit(10f - 1f / 1048576f, 1f / 1048576f, FluidPhase.Liquid, true);
        Fit(-1f, .1f, FluidPhase.Gas, false);
        Fit(float.NaN, .1f, FluidPhase.Gas, false);
        Fit(float.PositiveInfinity, .1f, FluidPhase.Gas, false);
        Fit(.1f, float.NaN, FluidPhase.Gas, false);
        Fit(.1f, float.PositiveInfinity, FluidPhase.Gas, false);
        Fit(.1f, -.1f, FluidPhase.Gas, false);
        Fit(.1f, 0f, FluidPhase.Gas, false);
        Fit(0f, .1f, FluidPhase.Vacuum, false);

        bool stress = Array.IndexOf(args, "--stress") >= 0;
        int trials = stress ? 1000000 : 2000;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Random(281);
        int success = 0;
        for (int i = 0; i < trials; i++)
        {
            float sourceKg = (float)Math.Pow(10, -5 + 5 * rng.NextDouble());
            float destKg = (float)Math.Pow(10, -5 + 5 * rng.NextDouble());
            double dose = Math.Pow(10, -8 + 7 * rng.NextDouble());
            var phase = stress && i % 2 == 1 ? FluidPhase.Liquid : FluidPhase.Gas;
            int element = phase == FluidPhase.Gas ? Hydrogen : Water;
            var a = new FluidSample(1, element, phase, sourceKg, 300);
            var b = stress && i % 3 == 1 ? Vacuum : new FluidSample(2, element, phase, destKg, 250);
            if (TransferPlan.TryCreate(a, b, phase, dose, false, null,
                out var plan, out _))
            {
                CheckInvariant(a, b, dose, plan);
                success++;
            }
        }
        Assert(success > 500, "ordinary same-element cases should commonly succeed");
        Console.WriteLine("{0} explicit planner cases plus {2} randomized cases passed ({1} accepted, {3} ms)",
            cases, success, trials, timer.ElapsedMilliseconds);
    }

    static void AccountingTests()
    {
        Assert(PipeAccounting.ValidDebit(1f, .75f, .25f, 11, 8, 3), "verified debit");
        Assert(PipeAccounting.ValidDebit(1f, 1f, 0, 11, 11, 0), "refused debit");
        Assert(!PipeAccounting.ValidDebit(1f, .5f, .25f, 11, 8, 3), "debit mass mismatch");
        Assert(!PipeAccounting.ValidDebit(1f, .75f, .25f, 11, 7, 3), "debit germ mismatch");
        Assert(!PipeAccounting.ValidDebit(1f, 1f, 0, 11, 10, 1), "zero mass with germs");
        Assert(!PipeAccounting.ValidDebit(1f, float.NaN, .25f, 11, 8, 3), "NaN debit");
        Assert(PipeAccounting.TryCredit(1, 11, 0, .25f, 0, 2, .25f, out var rest, out var germs)
            && rest == .75f && germs == 9, "partial uses actual germs, not rounding");
        Assert(PipeAccounting.TryCredit(rest, germs, .25f, 1, 2, 11, .75f, out rest, out germs)
            && rest == 0 && germs == 0, "complete remainder");
        Assert(PipeAccounting.TryCredit(1, 11, .5f, .5f, 4, 4, 0, out rest, out germs)
            && rest == 1 && germs == 11, "zero refusal keeps buffer");
        Assert(!PipeAccounting.TryCredit(1, 11, 0, 1, 0, 10, 1, out rest, out germs)
            && rest == 1 && germs == 11, "full receipt cannot erase leftover germs");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -.1f, 2f })
            Assert(!PipeAccounting.TryCredit(1, 11, 0, 1, 0, 11, bad, out rest, out germs), "invalid receipt");
        Assert(!PipeAccounting.TryCredit(1, 11, 0, .5f, 0, 5, .25f, out rest, out germs), "wrong target delta");
        Assert(!PipeAccounting.TryCredit(1, 11, 1, 1, 0, 0, 1e-8f, out rest, out germs), "rounded-away acceptance");
        Assert(!PipeAccounting.TryCredit(1, 11, 0, 1e-8f, 0, 0, 1e-8f, out rest, out germs), "rounded-away remainder");
        Assert(!PipeAccounting.TryCredit(1, 11, 0, .25f, 0, 12, .25f, out rest, out germs), "extra germs");
        Assert(!PipeAccounting.TryCredit(1, 11, 0, 0, 0, 1, 0, out rest, out germs), "germs on zero receipt");
        Assert(PipeAccounting.ValidBuffer(0, 0, 0, 0), "new save empty buffer");
        Assert(PipeAccounting.ValidBuffer(.01f, 300, 248.15f, 3), "legacy pending tempered sample");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f })
            Assert(!PipeAccounting.ValidBuffer(bad, 300, 300, 0), "bad legacy mass");
        Assert(!PipeAccounting.ValidBuffer(1, 0, 300, 0), "bad legacy temperature");
        Assert(PipeAccounting.ValidBuffer(0, float.NaN, 0, 3), "old empty buffer metadata is stale, not held matter");
        for (int mass = 1; mass <= 64; mass++)
            for (int accepted = 0; accepted <= mass; accepted++)
            {
                int delivered = accepted * 3 / mass;
                Assert(PipeAccounting.TryCredit(mass / 64f, 3, 0, accepted / 64f,
                    0, delivered, accepted / 64f, out rest, out germs), "fractional receipts");
                Assert((double)rest + accepted / 64f == mass / 64f && germs + delivered == 3,
                    "partial mass and germ conservation");
            }
        Console.WriteLine("Pipe accounting: malformed receipts, legacy buffers, and 2144 partial receipts passed.");
    }

    static TransferPlan Accept(FluidSample a, FluidSample b, FluidPhase phase, double kg,
        bool unlocked, double? celsius)
    {
        cases++;
        bool ok = TransferPlan.TryCreate(a, b, phase, kg, unlocked, celsius,
            out var plan, out var reason);
        Assert(ok && reason == TransferRejection.None, "unexpected rejection: " + reason);
        CheckInvariant(a, b, kg, plan);
        return plan;
    }

    static void Reject(FluidSample a, FluidSample b, FluidPhase phase, double kg, bool unlocked,
        double? celsius, TransferRejection expected)
    {
        cases++;
        bool ok = TransferPlan.TryCreate(a, b, phase, kg, unlocked, celsius,
            out var plan, out var reason);
        Assert(!ok && reason == expected && plan.AmountKg == 0,
            "expected rejection " + expected + ", got " + reason);
    }

    static void CheckInvariant(FluidSample a, FluidSample b, double requested, TransferPlan p)
    {
        Assert((double)a.MassKg - p.SourceAfterKg == p.AmountKg, "source conservation");
        Assert((double)p.TargetAfterKg - b.MassKg == p.AmountKg, "target conservation");
        Assert(p.AmountKg > 0 && Math.Abs(p.AmountKg - requested) <= requested * .05, "dose accuracy");
        Assert(p.SourceAfterKg >= 0 && p.TargetAfterKg > b.MassKg, "physical mass");
        if (b.Phase != FluidPhase.Vacuum)
            Assert(p.TargetAfterTemperatureK >= Math.Min(b.TemperatureK, p.TransferTemperatureK) &&
                p.TargetAfterTemperatureK <= Math.Max(b.TemperatureK, p.TransferTemperatureK),
                "thermal average");
    }

    static void Fit(float existing, float addition, FluidPhase phase, bool expected)
    {
        cases++;
        Assert(TransferPlan.CanFitPipe(existing, addition, phase) == expected,
            "pipe capacity: " + existing + " + " + addition + " in " + phase);
    }

    static void Assert(bool value, string label)
    {
        if (!value) throw new Exception(label);
    }
}
