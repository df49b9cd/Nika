namespace Nika.Migrations;

public static class MigrationEngine
{
    public static MigrationRunner New(IMigrationSource source, IMigrationDriver driver)
        => new(source, driver);

    public static Task<MigrationRunner> NewAsync(
        IMigrationSource source,
        IMigrationDriver driver)
    {
        var runner = new MigrationRunner(source, driver);
        return Task.FromResult(runner);
    }
}
