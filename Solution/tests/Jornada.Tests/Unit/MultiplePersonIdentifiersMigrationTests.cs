namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class MultiplePersonIdentifiersMigrationTests
{
    private static string MigrationPath => Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..", "database", "migrations",
        "20260913_Pessoa_Identificadores_Multiplos.sql");

    [Test]
    public void Migration_models_zero_to_many_identifiers_without_minimum_constraint()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.That(sql, Does.Contain("silver.pessoa_identificador_observacao"));
        Assert.That(sql, Does.Contain("tipo_identificador_codigo"));
        Assert.That(sql, Does.Contain("namespace_codigo"));
        Assert.That(sql, Does.Contain("valor_normalizado"));
        Assert.That(sql, Does.Contain("Não existe constraint exigindo ao menos um identificador"));
    }

    [Test]
    public void Migration_keeps_rg_qualified_and_does_not_make_it_automatically_deterministic()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.That(sql, Does.Contain("('RG','Registro Geral / identidade estadual'"));
        Assert.That(sql, Does.Contain("'CONDICIONAL',60"));
        Assert.That(sql, Does.Contain("tipo_identificador_codigo<>'RG' OR (emissor_codigo IS NOT NULL AND uf_emissor IS NOT NULL)"));
    }

    [Test]
    public void Migration_backfills_legacy_cpf_and_origin_code_instead_of_dropping_them()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.That(sql, Does.Contain("'CPF','BR',po.cpf,po.cpf"));
        Assert.That(sql, Does.Contain("'CODIGO_BASE_ORIGEM',b.codigo"));
        Assert.That(sql, Does.Not.Contain("DROP COLUMN cpf"));
        Assert.That(sql, Does.Not.Contain("DROP COLUMN codigo_pessoa_origem"));
    }

    [Test]
    public void Cpf_is_the_highest_priority_identifier_without_suppressing_conflicts()
    {
        var sql = File.ReadAllText(MigrationPath);

        Assert.That(sql, Does.Contain("('CPF','CPF','BR','CPF_BR_11_V1','DETERMINISTICA',100)"));
        Assert.That(sql, Does.Contain("('UUID_JORNADA','UUID publicado pela Jornada','JORNADA','UUID_V1','DETERMINISTICA',90)"));
        Assert.That(sql, Does.Contain("outro.prioridade_resolucao>=cpf.prioridade_resolucao"));
        Assert.That(sql, Does.Contain("CPF deve permanecer como identificador de maior prioridade de resolução"));
        Assert.That(sql, Does.Contain("prioridade nunca apaga divergência"));
    }
}
