namespace MoaMat.Web.Pwa;

/// <summary>
/// Family of browsers an installation tip must be written for.
/// </summary>
/// <remarks>
/// Only the families whose manual steps really differ are told apart. Anything
/// else is <see cref="Unknown"/> and gets a wording that works everywhere,
/// rather than instructions naming a menu the user does not have.
/// </remarks>
internal enum PwaInstallPlatform
{
    /// <summary>Browser whose installation path we cannot name precisely.</summary>
    Unknown = 0,

    /// <summary>iPhone or iPad: installation goes through the Safari share sheet.</summary>
    Ios,

    /// <summary>Android phone or tablet: installation goes through the browser menu.</summary>
    Android,

    /// <summary>Desktop browser: installation goes through the address bar.</summary>
    Desktop,
}
