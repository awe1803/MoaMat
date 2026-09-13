using System.Text;
using MoaMat.Domain.Accounts;
using MoaMat.Infrastructure.Supabase;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// The role drives whether a signed-in user sees the application or the
/// pending-account screen. It must come from the access token claim written by
/// <c>public.custom_access_token_hook</c>, and fall back to "no role" otherwise.
/// </summary>
public sealed class SupabaseAccessTokenInspectorTests
{
    [Theory]
    [InlineData("en_attente")]
    [InlineData("lecture")]
    [InlineData("gestion")]
    [InlineData("admin")]
    [InlineData("super-admin")]
    public void The_role_is_read_from_the_app_metadata_claim(string code)
    {
        var token = Jwt($"{{\"sub\":\"u1\",\"role\":\"authenticated\",\"app_metadata\":{{\"provider\":\"email\",\"role\":\"{code}\"}}}}");

        Assert.Equal(AppRole.FromCode(code), SupabaseAccessTokenInspector.ReadRole(token));
    }

    [Fact]
    public void The_top_level_postgres_role_claim_is_never_taken_for_the_application_role()
    {
        var token = Jwt("{\"sub\":\"u1\",\"role\":\"admin\",\"app_metadata\":{\"provider\":\"email\"}}");

        Assert.Equal(AppRole.None, SupabaseAccessTokenInspector.ReadRole(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("aaa.!!!not-base64!!!.ccc")]
    public void An_unreadable_token_yields_no_role(string? token)
    {
        Assert.Equal(AppRole.None, SupabaseAccessTokenInspector.ReadRole(token));
    }

    [Theory]
    [InlineData("{\"sub\":\"u1\"}")]
    [InlineData("{\"app_metadata\":\"admin\"}")]
    [InlineData("{\"app_metadata\":{\"role\":3}}")]
    [InlineData("[1,2,3]")]
    public void A_token_without_a_string_role_in_app_metadata_yields_no_role(string payloadJson)
    {
        Assert.Equal(AppRole.None, SupabaseAccessTokenInspector.ReadRole(Jwt(payloadJson)));
    }

    private static string Jwt(string payloadJson) =>
        $"{Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}")}.{Base64Url(payloadJson)}.signature";

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
