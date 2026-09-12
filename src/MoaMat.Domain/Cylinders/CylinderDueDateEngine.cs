namespace MoaMat.Domain.Cylinders;

/// <summary>
/// Pure computation of a cylinder's two independent requalification due
/// dates, mirroring <c>public.bouteille_echeance()</c> (<c>db/item_bouteille.sql</c>)
/// so the same 6 profiles produce the same due dates in the database and here.
/// </summary>
/// <remarks>
/// No control type ever alternates with, or depends on, the other: each due
/// date is resolved independently from <paramref name="rules"/> against its
/// own last-control date. This class has no I/O — the caller supplies the
/// periodicity rules (normally read from <c>public.ref_periodicite_bouteille</c>)
/// so the engine stays testable without a database.
/// </remarks>
public static class CylinderDueDateEngine
{
    /// <summary>Computes the two independent due dates for one cylinder.</summary>
    /// <param name="family">Usage family of the cylinder.</param>
    /// <param name="material">
    /// Material of the cylinder; required when <paramref name="family"/> is
    /// <see cref="CylinderFamily.Diving"/> (see <see cref="CylinderReferenceType.Resolve"/>).
    /// </param>
    /// <param name="lastOpticalControlOn">Date of the last visual inspection, or <c>null</c> if none was ever recorded.</param>
    /// <param name="lastHydraulicControlOn">Date of the last hydraulic requalification, or <c>null</c> if none was ever recorded.</param>
    /// <param name="rules">
    /// The periodicity referential, historized: several rules may share a
    /// reference type and control type at different <see cref="CylinderPeriodicityRule.EffectiveOn"/>
    /// dates. The rule applied to each counter is the one in force at that
    /// counter's own last-control date — not at today's date.
    /// </param>
    public static CylinderDueDates ComputeDueDates(
        CylinderFamily family,
        CylinderMaterial? material,
        DateOnly? lastOpticalControlOn,
        DateOnly? lastHydraulicControlOn,
        IReadOnlyCollection<CylinderPeriodicityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(rules);

        var referenceTypeCode = CylinderReferenceType.Resolve(family, material);

        return new CylinderDueDates(
            OpticalDueOn: ComputeDueDate(referenceTypeCode, CylinderControlType.Optical, lastOpticalControlOn, rules),
            HydraulicDueOn: ComputeDueDate(referenceTypeCode, CylinderControlType.Hydraulic, lastHydraulicControlOn, rules));
    }

    private static DateOnly? ComputeDueDate(
        string referenceTypeCode,
        CylinderControlType controlType,
        DateOnly? lastControlOn,
        IReadOnlyCollection<CylinderPeriodicityRule> rules)
    {
        if (lastControlOn is not { } controlOn)
        {
            return null;
        }

        // The rule in force AT THE DATE OF THE CONTROL, not today's rule —
        // consistent with the historized, admin-editable referential.
        var rule = rules
            .Where(r => r.ReferenceTypeCode == referenceTypeCode
                     && r.ControlType == controlType
                     && r.EffectiveOn <= controlOn)
            .OrderByDescending(r => r.EffectiveOn)
            .FirstOrDefault();

        // No rule = this control does not apply to this profile (e.g. no
        // optical control for carbon cylinders) — never a default value.
        return rule is null ? null : AddMonthsPostgresStyle(controlOn, rule.PeriodicityInMonths);
    }

    /// <summary>
    /// Adds whole months the same way <c>date + interval 'n months'</c> does in
    /// PostgreSQL (<c>public.bouteille_echeance()</c>), NOT the way
    /// <see cref="DateOnly.AddMonths"/> does.
    /// </summary>
    /// <remarks>
    /// When the target month is shorter than the original day-of-month,
    /// PostgreSQL overflows into the following month(s) rather than clamping:
    /// <c>date '2024-01-31' + interval '1 month'</c> is <c>2024-03-02</c>, not
    /// <c>2024-02-29</c> — see the PostgreSQL manual, §9.9.3. <see cref="DateOnly.AddMonths"/>
    /// does the opposite (clamps to the last valid day of the target month), so
    /// using it here would silently disagree with the database's actual
    /// enforcement of <c>public.item_bouteille_appliquer_hors_validite()</c> for
    /// any control performed on the 29th, 30th or 31st of a month.
    /// </remarks>
    private static DateOnly AddMonthsPostgresStyle(DateOnly date, int months)
    {
        var totalMonths = (date.Year * 12) + (date.Month - 1) + months;
        var targetYear = FloorDiv(totalMonths, 12);
        var targetMonth = totalMonths - (targetYear * 12) + 1;

        // Land on the 1st of the target month, then walk forward the same
        // number of extra days the original day-of-month represents — this is
        // what lets the result overflow into a later month instead of clamping.
        return new DateOnly(targetYear, targetMonth, 1).AddDays(date.Day - 1);
    }

    private static int FloorDiv(int dividend, int divisor)
    {
        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        return remainder != 0 && (remainder < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
