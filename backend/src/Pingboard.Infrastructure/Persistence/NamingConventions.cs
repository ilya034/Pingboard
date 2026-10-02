namespace Pingboard.Infrastructure.Persistence;

/// <summary>Именование в snake_case: как в SQL-схеме из §4 PLAN.md.</summary>
public static class NamingConventions
{
    public const string UsersTable = "users";
    public const string MonitorsTable = "monitors";
    public const string ChecksTable = "checks";
}
