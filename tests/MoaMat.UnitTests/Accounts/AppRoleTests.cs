using MoaMat.Domain.Accounts;

namespace MoaMat.UnitTests.Accounts;

/// <summary>
/// The role code arrives in a JWT claim the client does not control, so parsing
/// must never throw and must never over-grant.
/// </summary>
public sealed class AppRoleTests
{
    [Theory]
    [InlineData("en_attente", 0)]
    [InlineData("lecture", 1)]
    [InlineData("gestion", 2)]
    [InlineData("admin", 3)]
    [InlineData("super-admin", 4)]
    [InlineData("superadmin", 4)]
    public void FromCode_recognises_every_known_role(string code, int expectedRank)
    {
        Assert.Equal(expectedRank, AppRole.FromCode(code).Rank);
    }

    [Fact]
    public void FromCode_maps_the_pending_default_role_distinctly_from_no_role()
    {
        var pending = AppRole.FromCode("en_attente");

        Assert.Equal(AppRole.Pending, pending);
        Assert.NotEqual(AppRole.None, pending);
        Assert.Equal(0, pending.Rank);
    }

    [Theory]
    [InlineData("  ADMIN  ")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    public void FromCode_ignores_case_and_surrounding_whitespace(string code)
    {
        Assert.Equal(AppRole.Administrator, AppRole.FromCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("service_role")]
    [InlineData("root")]
    public void FromCode_degrades_unknown_input_to_no_role(string? code)
    {
        var role = AppRole.FromCode(code);

        Assert.Equal(AppRole.None, role);
        Assert.Equal(0, role.Rank);
    }

    [Fact]
    public void IsAtLeast_orders_roles_from_reader_to_super_administrator()
    {
        Assert.True(AppRole.SuperAdministrator.IsAtLeast(AppRole.Administrator));
        Assert.True(AppRole.Administrator.IsAtLeast(AppRole.Administrator));
        Assert.False(AppRole.Manager.IsAtLeast(AppRole.Administrator));
        Assert.False(AppRole.None.IsAtLeast(AppRole.Reader));
    }

    [Fact]
    public void Assignable_excludes_the_absent_and_pending_roles()
    {
        Assert.DoesNotContain(AppRole.None, AppRole.Assignable);
        Assert.DoesNotContain(AppRole.Pending, AppRole.Assignable);
        Assert.Equal(4, AppRole.Assignable.Count);
    }
}
