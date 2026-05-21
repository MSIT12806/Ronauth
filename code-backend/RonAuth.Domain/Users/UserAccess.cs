namespace RonAuth.Domain.Users;

public sealed class UserAccess
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public string ScopeName { get; set; } = string.Empty;
}