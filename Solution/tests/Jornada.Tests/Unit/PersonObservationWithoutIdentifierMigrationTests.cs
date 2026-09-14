using Xunit;

namespace Jornada.Tests.Unit;

public sealed class PersonObservationWithoutIdentifierMigrationTests
{
    private static readonly string MigrationPath = Path.Combine(
        FindSolutionRoot(),
        "database",
        "migrations",
        "20260913_Pessoa_Observacao_Sem_Identificador.sql");

    [Fact]
    public void Migration_Allows_Observation_Without_Origin_Without_Synthesizing_Key()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("ALTER COLUMN pessoa_origem_id BIGINT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ck_pessoa_observacao_origem_coerente", sql, StringComparison.Ordinal);
        Assert.Contains("pessoa_origem_id IS NULL AND codigo_pessoa_origem IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(codigo_pessoa_origem,cpf)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codigo_pessoa_origem=cpf", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_Preserves_Fact_Reference_Constraint_As_Separate_Concern()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.Contains("Fatos continuam exigindo uma referência local estável", sql, StringComparison.Ordinal);
        Assert.Contains("não autoriza inventar codigoPessoaOrigem", sql, StringComparison.Ordinal);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", "Jornada_Fase1.sql");
            if (File.Exists(candidate))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
