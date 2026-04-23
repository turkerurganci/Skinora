namespace Skinora.Users.Application.Account;

/// <summary>Request body for <c>DELETE /users/me</c> (07 §5.17 U14).</summary>
public sealed record DeleteAccountRequest(string? Confirmation);

/// <summary>Response for <c>POST /users/me/deactivate</c> (07 §5.17 U13).</summary>
public sealed record AccountDeactivateResponse(DateTime DeactivatedAt, string Message);

/// <summary>Response for <c>DELETE /users/me</c> (07 §5.17 U14).</summary>
public sealed record AccountDeleteResponse(DateTime DeletedAt, string Message);
