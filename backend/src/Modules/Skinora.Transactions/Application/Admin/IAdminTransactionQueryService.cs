using Skinora.Shared.Enums;
using Skinora.Shared.Models;

namespace Skinora.Transactions.Application.Admin;

/// <summary>
/// Read-only port for admin transaction listing + detail surfaces (T63 —
/// 07 §9.6 AD6, §9.7 AD7, §9.17 AD16b). Implemented at the API composition
/// root because AD7 detail composes data from <c>Skinora.Notifications</c>,
/// <c>Skinora.Disputes</c> and <c>Skinora.Fraud</c> — modules that
/// <c>Skinora.Transactions</c> cannot reference without a project cycle.
/// </summary>
public interface IAdminTransactionQueryService
{
    /// <summary>
    /// AD6 — paged transaction list with filters. The query itself is
    /// transactional-data only; buyer/seller views are denormalized into
    /// the row DTO so the frontend doesn't need a second round-trip.
    /// </summary>
    Task<PagedResult<AdminTransactionListItemDto>> ListAsync(
        AdminTransactionListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// AD16b — same shape as <see cref="ListAsync"/>, narrowed to a specific
    /// user identified by their Steam ID. Returns <c>null</c> when the user
    /// does not exist (controller maps to 404 USER_NOT_FOUND).
    /// </summary>
    Task<PagedResult<AdminTransactionListItemDto>?> ListForUserAsync(
        string steamId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// AD7 — full admin transaction detail view (T5 base + 8 admin-only
    /// sections per 07 §9.7). Returns <c>null</c> when the id is unknown.
    /// </summary>
    Task<AdminTransactionDetailDto?> GetDetailAsync(
        Guid transactionId, CancellationToken cancellationToken);
}

/// <summary>
/// Coarse status grouping for the S15 admin transaction filter (04 §8.4
/// "Durum: Tümü / Aktif / Tamamlanan / İptal / Flag'lenmiş"). The
/// single-status <see cref="AdminTransactionListQuery.Status"/> param cannot
/// express the multi-status <c>ACTIVE</c> / <c>CANCELLED</c> buckets, so this
/// group is resolved to a status set server-side (07 §9.6).
/// </summary>
/// <remarks>
/// <c>ACTIVE</c> is defined as "not terminal" so it matches the AD1 dashboard
/// <c>activeTransactions</c> counter exactly (mirrors
/// <c>AdminDashboardService</c>'s terminal-state set) — that keeps the
/// dashboard "Active Transactions" card and its <c>?tab=active</c> deep-link
/// consistent. ACTIVE therefore includes <c>FLAGGED</c>; the separate
/// <c>FLAGGED</c> group narrows to just flagged transactions.
/// </remarks>
public enum AdminTransactionStatusGroup
{
    ACTIVE,
    COMPLETED,
    CANCELLED,
    FLAGGED,
}

/// <summary>Filter inputs for AD6 (07 §9.6).</summary>
/// <param name="Status">Optional single-status filter.</param>
/// <param name="StatusGroup">
/// Optional coarse status bucket (04 §8.4 S15 filter). Applied in addition to
/// <see cref="Status"/> when both are supplied — the S15 UI sends only one.
/// </param>
/// <param name="Stablecoin">Optional stablecoin filter.</param>
/// <param name="DateFrom">Optional inclusive lower bound on <c>CreatedAt</c>.</param>
/// <param name="DateTo">Optional inclusive upper bound on <c>CreatedAt</c>.</param>
/// <param name="MinAmount">Optional inclusive lower bound on <c>Price</c>.</param>
/// <param name="MaxAmount">Optional inclusive upper bound on <c>Price</c>.</param>
/// <param name="Search">
/// Free-text search over <c>ItemName</c>, seller/buyer Steam ID and display
/// name. <c>LIKE</c>-based match — see implementation for the escape rule.
/// </param>
/// <param name="SortBy">Sort column: <c>createdAt</c> (default), <c>price</c>, <c>status</c>.</param>
/// <param name="SortOrder"><c>asc</c> or <c>desc</c> (default).</param>
/// <param name="Page">1-indexed page number.</param>
/// <param name="PageSize">Page size (clamped to 1–100, default 20).</param>
public sealed record AdminTransactionListQuery(
    TransactionStatus? Status,
    AdminTransactionStatusGroup? StatusGroup,
    StablecoinType? Stablecoin,
    DateTime? DateFrom,
    DateTime? DateTo,
    decimal? MinAmount,
    decimal? MaxAmount,
    string? Search,
    string? SortBy,
    string? SortOrder,
    int Page,
    int PageSize);
