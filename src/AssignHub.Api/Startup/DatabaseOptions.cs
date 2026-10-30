namespace AssignHub.Api.Startup;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool SeedDemoData { get; set; } = true;
}
