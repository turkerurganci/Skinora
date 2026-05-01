namespace Skinora.Admin.Application.Users;

/// <summary>Discriminated outcome for 07 §9.18 — <c>PUT /admin/users/:id/role</c>.</summary>
public abstract record AssignRoleOutcome
{
    public sealed record Success(AssignRoleResponse Response) : AssignRoleOutcome;
    public sealed record UserNotFound : AssignRoleOutcome;
    public sealed record RoleNotFound : AssignRoleOutcome;
}
