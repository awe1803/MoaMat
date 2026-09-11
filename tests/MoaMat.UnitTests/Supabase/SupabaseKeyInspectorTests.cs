using System.Text;
using System.Text.Json;
using MoaMat.Infrastructure.Supabase;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// This is the last guard before a key is baked into a static WebAssembly
/// bundle. A privileged key shipped there hands every visitor full read/write
/// access, bypassing every RLS policy, so the classification is pinned here.
/// </summary>
public sealed class SupabaseKeyInspectorTests
{
    [Fact]
    public void A_secret_key_is_recognised_as_privileged()
    {
        Assert.Equal(SupabaseKeyKind.SecretKey, SupabaseKeyInspector.Classify("sb_secret_abcdef123456"));
    }

    [Fact]
    public void A_publishable_key_is_safe_to_ship()
    {
        Assert.Equal(
            SupabaseKeyKind.PublishableKey,
            SupabaseKeyInspector.Classify("sb_publishable_abcdef123456"));
    }

    [Fact]
    public void A_legacy_anon_jwt_is_safe_to_ship()
    {
        Assert.Equal(SupabaseKeyKind.AnonymousJwt, SupabaseKeyInspector.Classify(JwtWithRole("anon")));
    }

    [Theory]
    [InlineData("service_role")]
    [InlineData("authenticated")]
    [InlineData("postgres")]
    public void A_legacy_jwt_with_any_other_role_is_privileged(string role)
    {
        Assert.Equal(SupabaseKeyKind.PrivilegedJwt, SupabaseKeyInspector.Classify(JwtWithRole(role)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("aaa.!!!not-base64!!!.ccc")]
    public void Anything_unrecognisable_is_reported_as_unknown(string? key)
    {
        Assert.Equal(SupabaseKeyKind.Unknown, SupabaseKeyInspector.Classify(key));
    }

    [Fact]
    public void A_jwt_without_a_role_claim_is_reported_as_unknown()
    {
        Assert.Equal(SupabaseKeyKind.Unknown, SupabaseKeyInspector.Classify(Jwt("{\"sub\":\"nobody\"}")));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_hide_a_secret_key()
    {
        Assert.Equal(SupabaseKeyKind.SecretKey, SupabaseKeyInspector.Classify("  sb_secret_abc  "));
    }

    private static string JwtWithRole(string role) =>
        Jwt(JsonSerializer.Serialize(new Dictionary<string, string> { ["role"] = role }));

    private static string Jwt(string payloadJson)
    {
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64Url(payloadJson);
        return $"{header}.{payload}.signature";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
