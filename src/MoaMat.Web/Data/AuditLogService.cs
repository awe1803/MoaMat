using Supabase.Postgrest;

namespace MoaMat.Web.Data;

/// <summary>
/// Accès en lecture seule au journal d'audit. Le contrôle d'accès réel est
/// porté par la policy RLS <c>audit_log_sel</c> (permission <c>audit.read</c>,
/// c.-à-d. rôles <c>admin</c> / <c>super-admin</c>) : un appel effectué par un
/// utilisateur non habilité renvoie simplement 0 ligne.
/// </summary>
public sealed class AuditLogService
{
    private const int DefaultLimit = 200;

    private readonly Supabase.Client _supabase;

    public AuditLogService(Supabase.Client supabase) => _supabase = supabase;

    /// <summary>Dernières entrées, les plus récentes d'abord.</summary>
    /// <param name="actionFilter">Filtre exact sur la colonne <c>action</c> (optionnel).</param>
    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(
        int limit = DefaultLimit,
        string? actionFilter = null)
    {
        var query = _supabase
            .From<AuditLogEntry>()
            .Order("occurred_at", Constants.Ordering.Descending)
            .Limit(limit);

        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            query = query.Filter("action", Constants.Operator.Equals, actionFilter);
        }

        var response = await query.Get();
        return response.Models;
    }
}
