namespace RonAuth.Infrastructure.Persistence;

public sealed class SqlitePersistenceOptions
{
    public const string SectionName = "Persistence";

    public string DatabasePath { get; set; } = Path.Combine("App_Data", "ronauth.db");
}