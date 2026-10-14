using MoaMat.Domain.Regulators;

namespace MoaMat.UnitTests.Regulators;

/// <summary>
/// <see cref="RegulatorDueDateEngine"/> is the pure C# mirror of
/// <c>public.detendeur_echeance()</c> (<c>db/item_detendeur.sql</c>): a single
/// counter, periodicity administrable per (brand, model) via
/// <c>public.ref_periodicite_detendeur</c> — no fixed interval, and never a
/// guessed default when the profile has not been configured yet.
/// </summary>
public sealed class RegulatorDueDateEngineTests
{
    private static readonly DateOnly EffectiveFrom = new(2020, 1, 1);
    private static readonly DateOnly LastIntervention = new(2025, 6, 10);

    private static readonly IReadOnlyCollection<RegulatorPeriodicityRule> Rules =
    [
        new("Scubapro", "MK25", 12, EffectiveFrom),
        new("Scubapro", "MK25", 6, new DateOnly(2026, 1, 1)),
    ];

    [Fact]
    public void ComputeDueDate_applies_the_rule_matching_brand_and_model()
    {
        var due = RegulatorDueDateEngine.ComputeDueDate("Scubapro", "MK25", LastIntervention, Rules);

        Assert.Equal(new DateOnly(2026, 6, 10), due);
    }

    [Fact]
    public void ComputeDueDate_is_case_and_whitespace_insensitive()
    {
        var due = RegulatorDueDateEngine.ComputeDueDate("  SCUBAPRO ", " mk25", LastIntervention, Rules);

        Assert.Equal(new DateOnly(2026, 6, 10), due);
    }

    [Fact]
    public void ComputeDueDate_is_null_when_no_rule_covers_this_profile()
    {
        var due = RegulatorDueDateEngine.ComputeDueDate("Mares", "Abyss", LastIntervention, Rules);

        Assert.Null(due);
    }

    [Theory]
    [InlineData(null, "MK25")]
    [InlineData("Scubapro", null)]
    [InlineData("", "MK25")]
    public void ComputeDueDate_is_null_when_brand_or_model_is_missing(string? brand, string? model)
    {
        var due = RegulatorDueDateEngine.ComputeDueDate(brand, model, LastIntervention, Rules);

        Assert.Null(due);
    }

    [Fact]
    public void ComputeDueDate_is_null_when_last_intervention_is_unknown()
    {
        var due = RegulatorDueDateEngine.ComputeDueDate("Scubapro", "MK25", null, Rules);

        Assert.Null(due);
    }

    [Fact]
    public void ComputeDueDate_applies_the_rule_in_force_at_the_intervention_date_not_today()
    {
        // The 6-month rule only takes effect 2026-01-01; an intervention
        // performed before that date still uses the 12-month rule, even
        // though the newer rule already exists in the referential.
        var due = RegulatorDueDateEngine.ComputeDueDate("Scubapro", "MK25", new DateOnly(2025, 12, 1), Rules);

        Assert.Equal(new DateOnly(2026, 12, 1), due);
    }
}
