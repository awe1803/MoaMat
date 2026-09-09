using MoaMat.Infrastructure.Supabase;

namespace MoaMat.UnitTests.Supabase;

/// <summary>
/// Start-up validation. Booting with a blank URL only defers the failure to the
/// first query, where it looks like a network outage; booting with a privileged
/// key is a leak. Both must stop the application immediately.
/// </summary>
public sealed class SupabaseSettingsTests
{
    private const string ValidUrl = "https://project.supabase.co";
    private const string ValidKey = "sb_publishable_abcdef123456";

    [Fact]
    public void Valid_settings_pass()
    {
        var settings = new SupabaseSettings { Url = ValidUrl, AnonKey = ValidKey };

        settings.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("project.supabase.co")]
    [InlineData("ftp://project.supabase.co")]
    public void An_unusable_url_stops_start_up(string url)
    {
        var settings = new SupabaseSettings { Url = url, AnonKey = ValidKey };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    [Fact]
    public void A_missing_key_stops_start_up()
    {
        var settings = new SupabaseSettings { Url = ValidUrl, AnonKey = "  " };

        Assert.Throws<InvalidOperationException>(settings.Validate);
    }

    [Fact]
    public void A_secret_key_stops_start_up_rather_than_shipping()
    {
        var settings = new SupabaseSettings { Url = ValidUrl, AnonKey = "sb_secret_abcdef123456" };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("SecretKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognisable_key_is_allowed_through()
    {
        // Refusing everything unknown would break the moment Supabase changes a
        // key format. Unknown is suspect, not proven dangerous - the deployment
        // pipeline carries the second check.
        var settings = new SupabaseSettings { Url = ValidUrl, AnonKey = "some-future-format" };

        settings.Validate();
    }
}
