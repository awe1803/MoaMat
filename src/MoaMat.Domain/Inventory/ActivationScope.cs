namespace MoaMat.Domain.Inventory;

/// <summary>Which items an inventory query should return, by activation state.</summary>
public enum ActivationScope
{
    /// <summary>Only items still in the active inventory. Default.</summary>
    ActiveOnly = 0,

    /// <summary>Only items that were logically deactivated.</summary>
    InactiveOnly = 1,

    /// <summary>Active and inactive items alike.</summary>
    All = 2,
}
