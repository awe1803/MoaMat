using MoaMat.Domain.Notifications;

namespace MoaMat.UnitTests.Notifications;

/// <summary>
/// The VAPID public key ships to every browser. The check must accept a real
/// public key and refuse anything else - above all the private key, which is a
/// valid base64url string too.
/// </summary>
public sealed class VapidPublicKeyTests
{
    // Throwaway key pair generated for these tests only; not used by any deployment.
    private const string PublicKey =
        "BFznGPE6viZ9nIoC910du3UpuPI3TEd9iZKc4t90h9ASr9_DZ7hR9W_UtcsI5pnZLhRKstYKE_WWl3jCVA6WfL4";
    private const string PrivateKey = "hmR7RXxVZInifEkBOvoXbm9ajumEmF6kBIRXeZDIiQQ";

    [Fact]
    public void A_public_key_is_accepted()
    {
        Assert.True(VapidPublicKey.IsValid(PublicKey));
    }

    [Fact]
    public void Surrounding_whitespace_is_tolerated()
    {
        Assert.True(VapidPublicKey.IsValid($"  {PublicKey}\n"));
    }

    [Fact]
    public void The_private_key_is_refused()
    {
        Assert.False(VapidPublicKey.IsValid(PrivateKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 at all !")]
    public void Unusable_input_is_refused(string? value)
    {
        Assert.False(VapidPublicKey.IsValid(value));
    }

    [Fact]
    public void A_65_byte_value_that_is_not_an_uncompressed_point_is_refused()
    {
        var bytes = new byte[65];
        bytes[0] = 0x02;
        var encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.False(VapidPublicKey.IsValid(encoded));
    }
}
