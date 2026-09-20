using MoaMat.Infrastructure.Supabase;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// <see cref="SupabaseFailureTranslator.Categorize"/> must read the SQLSTATE
/// from the PostgREST error body's <c>code</c> field exactly — never by
/// searching the raw body for a matching substring, since this codebase's own
/// business-rule messages embed numeric ids (item/campaign/line ids) that can
/// themselves contain the digits of an unrelated SQLSTATE.
/// </summary>
public sealed class SupabaseFailureTranslatorTests
{
    [Fact]
    public void Check_violation_is_categorized_as_refused()
    {
        var category = SupabaseFailureTranslator.Categorize(
            400, """{"code":"23514","message":"Envoi refusé : 2 bouteille(s) sans prestation choisie."}""");

        Assert.Equal(SupabaseFailureCategory.Refused, category);
    }

    [Fact]
    public void An_id_that_contains_the_check_violation_digits_is_not_misclassified()
    {
        // The message text embeds an id (123514) that contains "23514" as a
        // substring — a naive Contains("23514") search would have wrongly
        // classified this unique-constraint conflict as a business-rule
        // refusal instead of a conflict.
        var category = SupabaseFailureTranslator.Categorize(
            409, """{"code":"23505","message":"La bouteille 123514 est déjà engagée dans la campagne active 42."}""");

        Assert.Equal(SupabaseFailureCategory.Conflict, category);
    }

    [Fact]
    public void An_id_that_contains_the_insufficient_privilege_digits_is_not_misclassified()
    {
        // Same risk for 42501: an id of 142501 (or similar) must not flip an
        // otherwise-unrelated, unmapped error into "Refused".
        var category = SupabaseFailureTranslator.Categorize(400, """{"code":"23503","message":"Bouteille 142501 introuvable."}""");

        Assert.Equal(SupabaseFailureCategory.Conflict, category);
    }

    [Fact]
    public void Insufficient_privilege_is_categorized_as_refused()
    {
        var category = SupabaseFailureTranslator.Categorize(400, """{"code":"42501","message":"insufficient_privilege"}""");

        Assert.Equal(SupabaseFailureCategory.Refused, category);
    }

    [Fact]
    public void A_403_status_is_refused_even_without_a_parseable_body()
    {
        var category = SupabaseFailureTranslator.Categorize(403, content: null);

        Assert.Equal(SupabaseFailureCategory.Refused, category);
    }

    [Fact]
    public void An_unmapped_code_is_categorized_as_unexpected()
    {
        var category = SupabaseFailureTranslator.Categorize(400, """{"code":"22001","message":"value too long"}""");

        Assert.Equal(SupabaseFailureCategory.Unexpected, category);
    }

    [Fact]
    public void A_non_JSON_body_never_throws_and_falls_back_by_status_code()
    {
        var category = SupabaseFailureTranslator.Categorize(500, "<html>Bad Gateway</html>");

        Assert.Equal(SupabaseFailureCategory.Unreachable, category);
    }
}
