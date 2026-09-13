namespace MoaMat.Domain.Cylinders;

/// <summary>
/// One dated entry of the regulatory periodicity referential, mirroring a row
/// of <c>public.ref_periodicite_bouteille</c> (<c>db/item_bouteille.sql</c>).
/// </summary>
/// <param name="ReferenceTypeCode">
/// One of the 6 codes resolved by <see cref="CylinderReferenceType.Resolve"/>.
/// </param>
/// <param name="ControlType">Which of the two independent controls this rule applies to.</param>
/// <param name="PeriodicityInMonths">How many months after the last control the next one falls due.</param>
/// <param name="EffectiveOn">
/// Date from which this value is in force. The referential is historized
/// (append-only, admin-editable): several rules can share the same
/// <see cref="ReferenceTypeCode"/>/<see cref="ControlType"/> at different
/// <see cref="EffectiveOn"/> dates — <see cref="CylinderDueDateEngine"/>
/// always picks the one in force at the date of the control being dated, not
/// the one in force today.
/// </param>
public sealed record CylinderPeriodicityRule(
    string ReferenceTypeCode,
    CylinderControlType ControlType,
    int PeriodicityInMonths,
    DateOnly EffectiveOn);
