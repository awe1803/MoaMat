namespace MoaMat.Web.Pages.Auth.Models;

/// <summary>Steps the password reset screen goes through.</summary>
public enum ResetPasswordStage
{
    /// <summary>The recovery link is being validated.</summary>
    Checking = 0,

    /// <summary>The link was invalid or has expired.</summary>
    LinkInvalid = 1,

    /// <summary>The user can enter a new password.</summary>
    Form = 2,

    /// <summary>The password has been changed.</summary>
    Done = 3,
}
