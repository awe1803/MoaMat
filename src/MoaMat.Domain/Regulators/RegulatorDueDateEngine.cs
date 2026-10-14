namespace MoaMat.Domain.Regulators;

/// <summary>
/// Pure computation of a regulator's maintenance due date, mirroring
/// <c>public.detendeur_echeance()</c> (<c>db/item_detendeur.sql</c>) so the
/// same (brand, model) periodicity referential produces the same due date in
/// the database and here.
/// </summary>
/// <remarks>
/// A single counter, unlike <see cref="Cylinders.CylinderDueDateEngine"/>'s two
/// independent ones. The periodicity is administrable per (brand, model)
/// via <c>public.ref_periodicite_detendeur</c> — no fixed interval, and no
/// due date at all until that profile has been configured: a missing rule
/// means "not parameterized yet", never a guessed default.
/// </remarks>
public static class RegulatorDueDateEngine
{
    /// <summary>Computes the due date of the next maintenance.</summary>
    /// <param name="brand">Brand of the regulator (<c>public.item.marque</c>), or <c>null</c> when unknown.</param>
    /// <param name="model">Model of the regulator (<c>public.item.modele</c>), or <c>null</c> when unknown.</param>
    /// <param name="lastInterventionOn">Date of the last known intervention, or <c>null</c> if none is known.</param>
    /// <param name="rules">
    /// The periodicity referential, historized: several rules may share a
    /// (brand, model) at different <see cref="RegulatorPeriodicityRule.EffectiveOn"/>
    /// dates. The rule applied is the one in force at <paramref name="lastInterventionOn"/>,
    /// not at today's date.
    /// </param>
    /// <returns>
    /// The due date, or <c>null</c> when the last intervention, the brand or
    /// the model is unknown, or when no rule yet covers this (brand, model).
    /// </returns>
    public static DateOnly? ComputeDueDate(
        string? brand,
        string? model,
        DateOnly? lastInterventionOn,
        IReadOnlyCollection<RegulatorPeriodicityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (lastInterventionOn is not { } interventionOn
            || string.IsNullOrWhiteSpace(brand)
            || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var rule = rules
            .Where(r => Normalize(r.Brand) == Normalize(brand)
                     && Normalize(r.Model) == Normalize(model)
                     && r.EffectiveOn <= interventionOn)
            .OrderByDescending(r => r.EffectiveOn)
            .FirstOrDefault();

        return rule is null ? null : AddMonthsPostgresStyle(interventionOn, rule.PeriodicityInMonths);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>
    /// Adds whole months the same way <c>date + interval 'n months'</c> does in
    /// PostgreSQL (<c>public.detendeur_echeance()</c>), NOT the way
    /// <see cref="DateOnly.AddMonths"/> does — see <see cref="Cylinders.CylinderDueDateEngine"/>
    /// for the detailed rationale (month-end overflow vs. clamping).
    /// </summary>
    private static DateOnly AddMonthsPostgresStyle(DateOnly date, int months)
    {
        var totalMonths = (date.Year * 12) + (date.Month - 1) + months;
        var targetYear = FloorDiv(totalMonths, 12);
        var targetMonth = totalMonths - (targetYear * 12) + 1;

        return new DateOnly(targetYear, targetMonth, 1).AddDays(date.Day - 1);
    }

    private static int FloorDiv(int dividend, int divisor)
    {
        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
