namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class LocalBlockingRefreshContractTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Solution")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Test]
    public void Local_refresh_preserves_the_original_failure_and_extends_only_its_sql_timeout()
    {
        var root = Root();
        var bootstrap = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Processor.Worker", "LocalBlockingProjectionBootstrap.cs"));
        var persistence = File.ReadAllText(Path.Combine(
            root, "Solution", "src", "Jornada.Operational.Sql", "BlockingProjectionPersistence.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(bootstrap, Does.Contain("LocalBlockingCommandTimeoutSeconds = 900"));
            Assert.That(bootstrap, Does.Contain("commandTimeoutSeconds: LocalBlockingCommandTimeoutSeconds"));
            Assert.That(bootstrap, Does.Contain("RollbackPreservingOriginalAsync(transaction)"));
            Assert.That(bootstrap, Does.Contain("catch (Exception) { /* Preserva a exceção da operação original. */ }"));
            Assert.That(persistence, Does.Contain("int commandTimeoutSeconds = 30"));
            Assert.That(persistence, Does.Contain("ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeoutSeconds)"));
            Assert.That(
                persistence.Split("CommandTimeout = commandTimeoutSeconds", StringSplitOptions.None).Length - 1,
                Is.EqualTo(7));
        });
    }
}
