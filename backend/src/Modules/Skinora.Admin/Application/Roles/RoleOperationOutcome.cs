namespace Skinora.Admin.Application.Roles;

/// <summary>Discriminated outcome for create / update flows (07 §9.12 / §9.13).</summary>
public abstract record RoleOperationOutcome
{
    public sealed record Success(RoleDetailDto Role) : RoleOperationOutcome;
    public sealed record NotFound : RoleOperationOutcome;
    public sealed record NameConflict : RoleOperationOutcome;
    public sealed record InvalidPermission(string Key) : RoleOperationOutcome;
    public sealed record ValidationFailed(string Message) : RoleOperationOutcome;
}

/// <summary>Discriminated outcome for delete (07 §9.14).</summary>
public abstract record RoleDeleteOutcome
{
    public sealed record Success : RoleDeleteOutcome;
    public sealed record NotFound : RoleDeleteOutcome;
    public sealed record HasUsers(int AssignedUserCount) : RoleDeleteOutcome;
}
