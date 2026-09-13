using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class NullableMotherNameContractTests
{
    private static readonly string[] Managers = ["SMADS", "SEHAB", "SMS", "SMDET"];

    [Test]
    public void Pessoa_v3_must_make_nomeMae_optional_without_rewriting_v2()
    {
        var root = FindRepositoryRoot();

        foreach (var manager in Managers)
        {
            var v2Path = Path.Combine(root, "Solution", "config", "contracts", "gestores", manager, "pessoa", "v2", "pessoa.schema.json");
            var v3Path = Path.Combine(root, "Solution", "config", "contracts", "gestores", manager, "pessoa", "v3", "pessoa.schema.json");

            using var v2 = JsonDocument.Parse(File.ReadAllText(v2Path));
            using var v3 = JsonDocument.Parse(File.ReadAllText(v3Path));

            var v2Required = v2.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            var v3Required = v3.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            var v2MotherType = v2.RootElement.GetProperty("properties").GetProperty("nomeMae").GetProperty("type");
            var v3MotherType = v3.RootElement.GetProperty("properties").GetProperty("nomeMae").GetProperty("type");

            Assert.Multiple(() =>
            {
                Assert.That(v2Required, Does.Contain("nomeMae"), $"{manager}: v2 deve permanecer histórico e imutável.");
                Assert.That(v2MotherType.ValueKind, Is.EqualTo(JsonValueKind.String));
                Assert.That(v2MotherType.GetString(), Is.EqualTo("string"));

                Assert.That(v3Required, Does.Not.Contain("nomeMae"), $"{manager}: v3 não pode exigir nomeMae.");
                Assert.That(v3MotherType.ValueKind, Is.EqualTo(JsonValueKind.Array));
                var allowed = v3MotherType.EnumerateArray().Select(x => x.GetString()).ToArray();
                Assert.That(allowed, Does.Contain("string"));
                Assert.That(allowed, Does.Contain("null"));
            });
        }
    }

    [Test]
    public void Current_relational_contracts_must_preserve_missing_mother_name_as_null()
    {
        var root = FindRepositoryRoot();
        var sqlServerMigration = File.ReadAllText(Path.Combine(root, "Solution", "database", "migrations", "20260912_Nome_Mae_Anulavel.sql"));
        var postgresCore = File.ReadAllText(Path.Combine(root, "Solution", "database", "postgresql", "Jornada_Processor_Persistence_Core.sql"));
        var seed = File.ReadAllText(Path.Combine(root, "Solution", "database", "Jornada_Seed_Dev.sql"));

        Assert.Multiple(() =>
        {
            Assert.That(sqlServerMigration, Does.Contain("ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae NVARCHAR(500) NULL"));
            Assert.That(sqlServerMigration, Does.Contain("ALTER TABLE silver.pessoa_observacao ALTER COLUMN nome_mae_cmp NVARCHAR(500) NULL"));
            Assert.That(sqlServerMigration, Does.Contain("ALTER TABLE gold.pessoa ALTER COLUMN nome_mae NVARCHAR(500) NULL"));

            Assert.That(postgresCore, Does.Contain("ALTER COLUMN nome_mae DROP NOT NULL"));
            Assert.That(postgresCore, Does.Contain("ALTER COLUMN nome_mae_cmp DROP NOT NULL"));
            Assert.That(postgresCore, Does.Not.Contain("nome_mae VARCHAR(500) NOT NULL"));

            Assert.That(seed, Does.Contain("/pessoa/v3/pessoa.schema.json"));
            Assert.That(seed, Does.Contain("WHERE v.versao=3 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET')"));
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt")) &&
                Directory.Exists(Path.Combine(current.FullName, "Solution")))
                return current.FullName;
            current = current.Parent;
        }

        Assert.Fail("Raiz do repositório Jornada não encontrada.");
        return string.Empty;
    }
}
