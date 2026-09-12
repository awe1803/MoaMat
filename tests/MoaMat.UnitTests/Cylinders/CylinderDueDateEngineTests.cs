using MoaMat.Domain.Cylinders;

namespace MoaMat.UnitTests.Cylinders;

/// <summary>
/// <see cref="CylinderDueDateEngine"/> is the pure C# mirror of
/// <c>public.bouteille_echeance()</c> (<c>db/item_bouteille.sql</c>). The
/// periodicities below are the ticket's 6 acceptance profiles, taken from the
/// legacy mirror <c>public.ref_regle_requalification</c>: Plongée ACIER/ALU
/// (2,5 ans optique / 5 ans hydraulique), Plongée Carbonne (3 ans hydraulique
/// seul), Deco O² (2,5 ans / 5 ans), O² Secourisme (5 ans optique seul),
/// Tampons (10 ans hydraulique seul).
/// </summary>
public sealed class CylinderDueDateEngineTests
{
    private static readonly DateOnly EffectiveFrom = new(2020, 1, 1);
    private static readonly DateOnly LastControlOn = new(2024, 1, 15);

    // Mirrors the seed data of public.ref_periodicite_bouteille exactly.
    private static readonly IReadOnlyCollection<CylinderPeriodicityRule> Rules =
    [
        new(CylinderReferenceType.DivingSteelCode, CylinderControlType.Optical, 30, EffectiveFrom),
        new(CylinderReferenceType.DivingSteelCode, CylinderControlType.Hydraulic, 60, EffectiveFrom),
        new(CylinderReferenceType.DivingAluminiumCode, CylinderControlType.Optical, 30, EffectiveFrom),
        new(CylinderReferenceType.DivingAluminiumCode, CylinderControlType.Hydraulic, 60, EffectiveFrom),
        new(CylinderReferenceType.DivingCarbonCode, CylinderControlType.Hydraulic, 36, EffectiveFrom),
        new(CylinderReferenceType.DecoOxygenCode, CylinderControlType.Optical, 30, EffectiveFrom),
        new(CylinderReferenceType.DecoOxygenCode, CylinderControlType.Hydraulic, 60, EffectiveFrom),
        new(CylinderReferenceType.RescueOxygenCode, CylinderControlType.Optical, 60, EffectiveFrom),
        new(CylinderReferenceType.BufferTankCode, CylinderControlType.Hydraulic, 120, EffectiveFrom),
    ];

    private static CylinderDueDates Compute(CylinderFamily family, CylinderMaterial? material) =>
        CylinderDueDateEngine.ComputeDueDates(family, material, LastControlOn, LastControlOn, Rules);

    public static TheoryData<CylinderFamily, CylinderMaterial?, DateOnly?, DateOnly?> SixProfiles()
    {
        var data = new TheoryData<CylinderFamily, CylinderMaterial?, DateOnly?, DateOnly?>
        {
            { CylinderFamily.Diving, CylinderMaterial.Steel, LastControlOn.AddMonths(30), LastControlOn.AddMonths(60) },
            { CylinderFamily.Diving, CylinderMaterial.Aluminium, LastControlOn.AddMonths(30), LastControlOn.AddMonths(60) },
            { CylinderFamily.Diving, CylinderMaterial.Carbon, null, LastControlOn.AddMonths(36) },
            { CylinderFamily.DecoOxygen, null, LastControlOn.AddMonths(30), LastControlOn.AddMonths(60) },
            { CylinderFamily.RescueOxygen, null, LastControlOn.AddMonths(60), null },
            { CylinderFamily.BufferTank, null, null, LastControlOn.AddMonths(120) },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(SixProfiles))]
    public void Each_of_the_6_profiles_produces_the_expected_due_dates(
        CylinderFamily family, CylinderMaterial? material, DateOnly? expectedOptical, DateOnly? expectedHydraulic)
    {
        var dueDates = Compute(family, material);

        Assert.Equal(expectedOptical, dueDates.OpticalDueOn);
        Assert.Equal(expectedHydraulic, dueDates.HydraulicDueOn);
    }

    [Fact]
    public void A_control_with_no_applicable_rule_is_null_not_a_default_value()
    {
        // Carbon has no optical control at all in the referential (mirrors
        // "JAMAIS" in the legacy data) — a recorded control date must still
        // resolve to null, never fall back to some periodicity.
        var dueDates = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Carbon, LastControlOn, LastControlOn, Rules);

        Assert.Null(dueDates.OpticalDueOn);
        Assert.NotNull(dueDates.HydraulicDueOn);
    }

    [Fact]
    public void The_two_counters_are_independent_when_only_one_control_is_recorded()
    {
        var dueDates = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Steel,
            lastOpticalControlOn: LastControlOn,
            lastHydraulicControlOn: null,
            Rules);

        Assert.Equal(LastControlOn.AddMonths(30), dueDates.OpticalDueOn);
        Assert.Null(dueDates.HydraulicDueOn);
    }

    [Fact]
    public void Setting_one_counter_never_changes_the_other()
    {
        var before = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Steel, LastControlOn, LastControlOn, Rules);

        var afterHydraulicMovedEarlier = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Steel,
            lastOpticalControlOn: LastControlOn,
            lastHydraulicControlOn: LastControlOn.AddYears(-3),
            Rules);

        Assert.Equal(before.OpticalDueOn, afterHydraulicMovedEarlier.OpticalDueOn);
        Assert.NotEqual(before.HydraulicDueOn, afterHydraulicMovedEarlier.HydraulicDueOn);
    }

    [Fact]
    public void The_rule_in_force_at_the_control_date_applies_not_the_rule_in_force_today()
    {
        // The referential is historized: an admin tightening the hydraulic
        // periodicity for steel cylinders from 2025 onward must not retroactively
        // change the due date of a control performed under the old rule.
        var rules = new List<CylinderPeriodicityRule>(Rules)
        {
            new(CylinderReferenceType.DivingSteelCode, CylinderControlType.Hydraulic, 48, new DateOnly(2025, 1, 1)),
        };

        var controlUnderOldRule = new DateOnly(2024, 6, 1);
        var controlUnderNewRule = new DateOnly(2025, 6, 1);

        var oldRuleResult = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Steel, null, controlUnderOldRule, rules);
        var newRuleResult = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.Diving, CylinderMaterial.Steel, null, controlUnderNewRule, rules);

        Assert.Equal(controlUnderOldRule.AddMonths(60), oldRuleResult.HydraulicDueOn);
        Assert.Equal(controlUnderNewRule.AddMonths(48), newRuleResult.HydraulicDueOn);
    }

    [Fact]
    public void Month_end_overflow_matches_PostgreSQL_date_plus_interval_not_DateOnly_AddMonths()
    {
        // public.item_bouteille_appliquer_hors_validite() is enforced by
        // public.bouteille_echeance(), which uses PostgreSQL's
        // "date + interval 'n months'" arithmetic: it OVERFLOWS into the next
        // month when the target month is too short, rather than clamping like
        // DateOnly.AddMonths does. This is the PostgreSQL manual's own example
        // (§9.9.3): 2005-01-31 + 1 month = 2005-03-03 (DateOnly.AddMonths would
        // give 2005-02-28). If this engine used AddMonths, a UI built on it
        // could show a due date the automatic bascule would not agree with.
        var rules = new[] { new CylinderPeriodicityRule(CylinderReferenceType.BufferTankCode, CylinderControlType.Hydraulic, 1, new DateOnly(2000, 1, 1)) };

        var dueDates = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.BufferTank, material: null,
            lastOpticalControlOn: null,
            lastHydraulicControlOn: new DateOnly(2005, 1, 31),
            rules);

        Assert.Equal(new DateOnly(2005, 3, 3), dueDates.HydraulicDueOn);
        Assert.NotEqual(new DateOnly(2005, 1, 31).AddMonths(1), dueDates.HydraulicDueOn);
    }

    [Fact]
    public void EarliestDueOn_ignores_a_counter_that_does_not_apply()
    {
        var dueDates = CylinderDueDateEngine.ComputeDueDates(
            CylinderFamily.BufferTank, material: null, lastOpticalControlOn: null, lastHydraulicControlOn: LastControlOn, Rules);

        Assert.Equal(dueDates.HydraulicDueOn, dueDates.EarliestDueOn);
    }
}
