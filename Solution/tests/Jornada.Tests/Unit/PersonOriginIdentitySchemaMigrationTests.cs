namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PersonOriginIdentitySchemaMigrationTests
{
    private static string Root => FindSolutionRoot();

    [Test]
    public void Base_origin_migration_separates_transmitter_from_identity_namespace_without_breaking_legacy_runtime()
    {
        var sql = ReadMigration("20260913_Base_Pessoa_Origem.sql");

        Assert.That(sql, Does.Contain("ref.base_pessoa_origem"));
        Assert.That(sql, Does.Contain("ref.sistema_origem_base_pessoa"));
        Assert.That(sql, Does.Contain("uq_pessoa_origem_base_codigo"));
        Assert.That(sql, Does.Contain("silver.pessoa_origem_sistema"));
        Assert.That(sql, Does.Contain("CONCAT('SYS_',CONVERT(VARCHAR(20),s.sistema_origem_id))"));
        Assert.That(sql, Does.Contain("WHERE base_pessoa_origem_id IS NOT NULL"));
        Assert.That(sql, Does.Not.Contain("ALTER COLUMN base_pessoa_origem_id BIGINT NOT NULL"));
        Assert.That(sql, Does.Not.Contain("DROP CONSTRAINT uq_pessoa_origem"));
    }

    [Test]
    public void Multiple_identifier_migration_preserves_cpf_anchor_and_does_not_enable_cns_or_rg_automatically()
    {
        var sql = ReadMigration("20260913_Pessoa_Identificadores_Multiplos.sql");

        Assert.That(sql, Does.Contain("silver.pessoa_identificador_observacao"));
        Assert.That(sql, Does.Contain("('CPF','CPF','BR','CPF_BR_11_V1','DETERMINISTICA','EXTERNO_HIERARQUICO',CAST(100 AS SMALLINT))"));
        Assert.That(sql, Does.Contain("('CNS','Cartão Nacional de Saúde','BR','CNS_BR_V1','CONDICIONAL'"));
        Assert.That(sql, Does.Contain("('RG','Registro Geral / identidade estadual','SSP_UF','RG_QUALIFICADO_V1','CONDICIONAL'"));
        Assert.That(sql, Does.Contain("mesmo CPF -> mesmo UUID âncora"));
        Assert.That(sql, Does.Not.Contain("DROP COLUMN cpf"));
    }

    [Test]
    public void Observation_without_identifier_is_allowed_without_synthetic_origin_key()
    {
        var sql = ReadMigration("20260913_Pessoa_Observacao_Sem_Identificador.sql");

        Assert.That(sql, Does.Contain("ALTER COLUMN pessoa_origem_id BIGINT NULL"));
        Assert.That(sql, Does.Contain("ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL"));
        Assert.That(sql, Does.Contain("ck_pessoa_observacao_origem_coerente"));
        Assert.That(sql, Does.Contain("CREATE UNIQUE INDEX uq_pessoa_observacao_versao"));
        Assert.That(sql, Does.Contain("WHERE pessoa_origem_id IS NOT NULL"));
        Assert.That(sql, Does.Not.Contain("COALESCE(codigo_pessoa_origem,cpf)").IgnoreCase);
    }

    [Test]
    public void Runtime_v4_cutover_makes_base_authoritative_and_authorizes_internal_uuid_feedback()
    {
        var sql = ReadMigration("20260919_Pessoa_Origem_Runtime_V4_Cutover.sql");

        Assert.That(sql, Does.Contain("DROP CONSTRAINT uq_pessoa_origem"));
        Assert.That(sql, Does.Contain("ALTER COLUMN base_pessoa_origem_id BIGINT NOT NULL"));
        Assert.That(sql, Does.Contain("uq_pessoa_origem_base_codigo"));
        Assert.That(sql, Does.Contain("UUID_JORNADA_RETROALIMENTACAO"));
        Assert.That(sql, Does.Not.Contain("INSERT identidade.identity_map").IgnoreCase);
    }

    [Test]
    public void Canonical_manifest_orders_origin_before_identifiers_and_keeps_consolidation_last()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "database", "migrations", "manifest.txt"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .ToArray();

        var origin = Array.IndexOf(lines, "migrations/20260913_Base_Pessoa_Origem.sql");
        var identifiers = Array.IndexOf(lines, "migrations/20260913_Pessoa_Identificadores_Multiplos.sql");
        var nullableObservation = Array.IndexOf(lines, "migrations/20260913_Pessoa_Observacao_Sem_Identificador.sql");
        var deliveryPersonLink = Array.IndexOf(lines, "migrations/20260919_Fato_Referencia_Pessoa_Entrega.sql");
        var runtimeCutover = Array.IndexOf(lines, "migrations/20260919_Pessoa_Origem_Runtime_V4_Cutover.sql");

        Assert.That(origin, Is.GreaterThanOrEqualTo(0));
        Assert.That(identifiers, Is.GreaterThan(origin));
        Assert.That(nullableObservation, Is.GreaterThan(identifiers));
        Assert.That(deliveryPersonLink, Is.GreaterThan(nullableObservation));
        Assert.That(runtimeCutover, Is.GreaterThan(deliveryPersonLink));
        Assert.That(lines[^1], Is.EqualTo("migrations/20260910_Schema_Consolidation_370.sql"));
    }

    private static string ReadMigration(string name) =>
        File.ReadAllText(Path.Combine(Root, "database", "migrations", name));

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jornada.sln"))
                && File.Exists(Path.Combine(directory.FullName, "database", "Jornada_Fase1.sql")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Solution root not found.");
    }
}
