using MoaMat.Domain.Accounts;

namespace MoaMat.UnitTests.Accounts;

/// <summary>
/// These rules used to live in the account screen markup. They decide what an
/// administrator can do to other people's accounts, so every branch is pinned
/// here - including the ones that must stay refused.
/// </summary>
public sealed class AccountAdministrationPolicyTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static UserAccount AccountWith(AppRole role, Guid? id = null) =>
        new(id ?? OtherId, "member@club.be", role, IsDisabled: false, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void A_manager_can_administer_nothing()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.Manager);

        Assert.False(policy.CanAdministerAccounts);
        Assert.False(policy.CanChangeRoleOf(AccountWith(AppRole.Reader)));
        Assert.False(policy.CanAssignRole(AppRole.Reader));
        Assert.False(policy.CanChangeActivationOf(AccountWith(AppRole.Manager)));
    }

    [Fact]
    public void Nobody_can_change_their_own_role_or_lock_themselves_out()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.SuperAdministrator);
        var self = AccountWith(AppRole.SuperAdministrator, ActorId);

        Assert.False(policy.CanChangeRoleOf(self));
        Assert.False(policy.CanChangeActivationOf(self));
    }

    [Fact]
    public void An_administrator_cannot_act_on_a_privileged_account()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.Administrator);

        Assert.False(policy.CanChangeRoleOf(AccountWith(AppRole.Administrator)));
        Assert.False(policy.CanChangeRoleOf(AccountWith(AppRole.SuperAdministrator)));
        Assert.False(policy.CanChangeActivationOf(AccountWith(AppRole.Administrator)));
    }

    [Fact]
    public void An_administrator_cannot_deactivate_a_board_member_but_can_promote_one()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.Administrator);
        var boardMember = AccountWith(AppRole.Reader);

        Assert.True(boardMember.IsBoardMember);
        Assert.False(policy.CanChangeActivationOf(boardMember));
        Assert.True(policy.CanChangeRoleOf(boardMember));
    }

    [Fact]
    public void An_administrator_cannot_hand_out_a_privileged_role()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.Administrator);

        Assert.True(policy.CanAssignRole(AppRole.Reader));
        Assert.True(policy.CanAssignRole(AppRole.Manager));
        Assert.False(policy.CanAssignRole(AppRole.Administrator));
        Assert.False(policy.CanAssignRole(AppRole.SuperAdministrator));
    }

    [Fact]
    public void A_super_administrator_can_act_on_every_other_account()
    {
        var policy = new AccountAdministrationPolicy(ActorId, AppRole.SuperAdministrator);

        Assert.True(policy.CanChangeRoleOf(AccountWith(AppRole.Administrator)));
        Assert.True(policy.CanChangeActivationOf(AccountWith(AppRole.Reader)));
        Assert.True(policy.CanAssignRole(AppRole.SuperAdministrator));
    }
}
