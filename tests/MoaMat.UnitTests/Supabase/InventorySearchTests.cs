using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Campaigns;
using MoaMat.Infrastructure.Supabase;
using MoaMat.Infrastructure.Supabase.Mapping;
using MoaMat.Infrastructure.Supabase.Records;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// The search text now travels to PostgREST inside an <c>or=(…)</c> group: what
/// the user types must never be able to change the structure of that group.
/// </summary>
public sealed class InventorySearchTests
{
    [Theory]
    [InlineData("BOUT-F-52", "BOUT-F-52")]
    [InlineData("  faber  ", "faber")]
    [InlineData("a,b", "ab")]
    [InlineData("x),code_club.eq.1", "xcode_club.eq.1")]
    [InlineData("50%", "50")]
    [InlineData("*", "")]
    [InlineData("q\"uote", "quote")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Characters_that_carry_meaning_in_an_or_group_are_removed(string? typed, string expected) =>
        Assert.Equal(expected, SupabaseInventoryRepository.SanitizeSearchTerm(typed));

    [Fact]
    public void Accents_and_spaces_survive()
    {
        Assert.Equal("Piscine Seraing é", SupabaseInventoryRepository.SanitizeSearchTerm("Piscine Seraing é"));
    }
}

public sealed class CylinderMapperTests
{
    [Theory]
    [InlineData("mise_en_service", CylinderEventType.Commissioning)]
    [InlineData("controle_optique", CylinderEventType.OpticalControl)]
    [InlineData("controle_hydraulique", CylinderEventType.HydraulicControl)]
    [InlineData("ecartee", CylinderEventType.Withdrawn)]
    [InlineData("rebut", CylinderEventType.Scrapped)]
    [InlineData("incident", CylinderEventType.Incident)]
    public void Every_database_event_type_maps_to_its_domain_type(string code, CylinderEventType expected)
    {
        var entry = CylinderMapper.ToDomain(new CylinderEventRecord { Id = 1, Type = code });

        Assert.Equal(expected, entry.Type);
    }

    [Theory]
    [InlineData("conforme", CampaignLineOutcome.Passed)]
    [InlineData("echec", CampaignLineOutcome.Failed)]
    public void The_outcome_round_trips(string code, CampaignLineOutcome outcome)
    {
        var entry = CylinderMapper.ToDomain(new CylinderEventRecord { Id = 1, Type = "controle_optique", Resultat = code });

        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal(code, CylinderMapper.ToCode(outcome));
    }

    [Fact]
    public void Imported_history_has_no_outcome_and_an_undated_row_stays_undated()
    {
        var entry = CylinderMapper.ToDomain(new CylinderEventRecord { Id = 1, Type = "rebut", Resultat = null, DateEvenement = null });

        Assert.Null(entry.Outcome);
        Assert.Null(entry.OccurredOn);
    }

    [Theory]
    [InlineData(CylinderControlType.Optical, "controle_optique")]
    [InlineData(CylinderControlType.Hydraulic, "controle_hydraulique")]
    public void Control_types_use_the_codes_the_database_function_accepts(CylinderControlType control, string code) =>
        Assert.Equal(code, CylinderMapper.ToCode(control));
}

public sealed class CylinderMapperUnknownTypeTests
{
    [Fact]
    public void A_type_added_later_in_the_database_is_neutral_not_an_incident()
    {
        var entry = CylinderMapper.ToDomain(new CylinderEventRecord { Id = 1, Type = "futur_type" });

        Assert.Equal(CylinderEventType.Unknown, entry.Type);
        Assert.False(entry.IsAdverse);
    }
}
