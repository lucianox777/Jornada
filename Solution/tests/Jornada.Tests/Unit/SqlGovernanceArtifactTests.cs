using NUnit.Framework.Legacy;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class SqlGovernanceArtifactTests
{
    private static string LoadDdl() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "database", "Jornada_Fase1.sql"));

    [Test]
    public void Historical_merge_validations_are_preflighted_before_first_apply_mutation()
    {
        var sql = LoadDdl();
        var block = LastProcedureBlock(sql, "identidade.sp_aplicar_caso_conflito_identidade");
        var mutation = block.IndexOf("UPDATE vf SET ativo=0", StringComparison.Ordinal);
        Assert.That(mutation, Is.GreaterThan(0));
        foreach (var code in new[] { "THROW 51117", "THROW 51118", "THROW 51119" })
        {
            var pos = block.IndexOf(code, StringComparison.Ordinal);
            Assert.That(pos, Is.GreaterThan(0), $"{code} deve existir dentro da procedure.");
            Assert.That(pos, Is.LessThan(mutation), $"{code} deve ocorrer antes da primeira mutação da aplicação.");
        }
    }

    [Test]
    public void Governed_identity_and_cross_table_sync_procedures_are_self_transactional_and_caller_safe()
    {
        var sql = LoadDdl();
        foreach (var procedure in new[]
        {
            "identidade.sp_abrir_caso_conflito_identidade",
            "identidade.sp_aplicar_caso_conflito_identidade",
            "identidade.sp_aplicar_correcao_identidade",
            "identidade.sp_sincronizar_atribuicao_fatos",
            "ingestao.sp_recalcular_entrega"
        })
        {
            var block = LastProcedureBlock(sql, procedure);
            Assert.Multiple(() =>
            {
                StringAssert.Contains("SET XACT_ABORT ON", block, procedure);
                StringAssert.Contains("DECLARE @jornada_own_tran BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END", block, procedure);
                StringAssert.Contains("IF @jornada_own_tran=1 BEGIN TRANSACTION", block, procedure);
                StringAssert.Contains("BEGIN TRY", block, procedure);
                StringAssert.Contains("IF @jornada_own_tran=1 COMMIT TRANSACTION", block, procedure);
                StringAssert.Contains("IF @jornada_own_tran=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION", block, procedure);
                StringAssert.Contains("BEGIN CATCH", block, procedure);
                StringAssert.Contains("THROW;", block, procedure);
            });
        }
    }

    [Test]
    public void Recompose_gold_person_materializes_source_and_never_references_cte_after_merge()
    {
        var sql = LoadDdl();
        var block = LastProcedureBlock(sql, "identidade.sp_recompor_gold_pessoa");
        Assert.Multiple(() =>
        {
            StringAssert.Contains("SET XACT_ABORT ON", block);
            StringAssert.Contains("DECLARE @jornada_own_tran BIT=CASE WHEN @@TRANCOUNT=0 THEN 1 ELSE 0 END", block);
            StringAssert.Contains("DECLARE @src TABLE", block);
            StringAssert.Contains("INSERT @src", block);
            StringAssert.Contains("MERGE gold.pessoa WITH (HOLDLOCK)", block);
            StringAssert.Contains("IF NOT EXISTS(SELECT 1 FROM @src)", block);
            StringAssert.DoesNotContain("SELECT 1 FROM obs", block);
        });
    }

    [Test]
    public void Phone_v2_upgrade_recomputes_keys_from_original_value_and_has_explicit_shared_trim_contract()
    {
        var sql = LoadDdl();
        StringAssert.Contains("CREATE OR ALTER FUNCTION ref.fn_telefone_br_canonico_v2", sql);
        StringAssert.Contains("UNICODE(LEFT(@v,1)) IN(9,10,13,32,160)", sql);
        StringAssert.Contains("UNICODE(SUBSTRING(@v,@n,1)) IN(9,10,13,32,160)", sql);
        StringAssert.Contains("SET atributo_instancia_chave=ref.fn_telefone_br_canonico_v2(valor)", sql);
        StringAssert.Contains("COALESCE(pao.atributo_instancia_chave,ref.fn_telefone_br_canonico_v2(ga.valor))", sql);
        StringAssert.Contains("Upgrade de TELEFONE_CONTATO encontrou valor legado incompatível", sql);
    }


    [Test]
    public void Governed_error_contract_51110_through_51119_is_preflighted_and_51114_precedes_temp_pk_insert()
    {
        var sql = LoadDdl();
        var block = LastProcedureBlock(sql, "identidade.sp_aplicar_caso_conflito_identidade");
        foreach (var code in Enumerable.Range(51110, 10))
            StringAssert.Contains($"THROW {code}", block, $"Contrato {code} ausente.");

        var throw51114 = block.IndexOf("THROW 51114", StringComparison.Ordinal);
        var insertO = block.IndexOf("INSERT @o", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(throw51114, Is.GreaterThan(0));
            Assert.That(insertO, Is.GreaterThan(0));
            Assert.That(throw51114, Is.LessThan(insertO), "51114 deve ser alcançável antes da PK(obs) de @o.");
        });
    }

    [Test]
    public void Email_v2_upgrade_is_fail_closed_and_schema_marker_is_exact()
    {
        var sql = LoadDdl();
        Assert.Multiple(() =>
        {
            StringAssert.Contains("CREATE OR ALTER FUNCTION ref.fn_email_canonico_v2", sql);
            StringAssert.Contains("Upgrade de EMAIL_CONTATO encontrou valor legado incompatível com EMAIL_CANONICO_V2", sql);
            StringAssert.Contains("SET atributo_instancia_chave=ref.fn_email_canonico_v2(valor)", sql);
            StringAssert.Contains("chave_instancia_codigo='EMAIL_CANONICO_V2'", sql);
            StringAssert.Contains("Jornada.BaseNormativa", sql);
            StringAssert.Contains("@value=N'3.62'", sql);
            StringAssert.Contains("Jornada.SolutionSchema", sql);
            StringAssert.Contains("@value=N'3.69'", sql);
        });
    }

    private static string LastProcedureBlock(string sql, string name)
    {
        var marker = "CREATE OR ALTER PROCEDURE " + name;
        var start = sql.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Procedure ausente: {name}");
        var end = sql.IndexOf("\nGO", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"GO final ausente: {name}");
        return sql[start..end];
    }
}
