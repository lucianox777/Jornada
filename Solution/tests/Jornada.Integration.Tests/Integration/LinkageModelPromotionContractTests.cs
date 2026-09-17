using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LinkageModelPromotionContractTests
{
    [Test]
    public async Task Sql_server_migration_materializes_v5_v6_promotion_contract_in_isolated_database()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ApplyContractAsync(connection);

        await using var triggerCommand = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('identidade.tr_modelo_linkage_promotion_contract'));",
            connection);
        var triggerDefinition = Convert.ToString(
            await triggerCommand.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);

        await using var functionCommand = new SqlCommand(
            "SELECT OBJECT_DEFINITION(OBJECT_ID('identidade.fn_linkage_birth_semantic_reachability'));",
            connection);
        var functionDefinition = Convert.ToString(
            await functionCommand.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(triggerDefinition, Is.Not.Null.And.Not.Empty);
            Assert.That(triggerDefinition, Does.Contain("FELLEGI_SUNTER_SEMANTIC_BIRTH_V5"));
            Assert.That(triggerDefinition, Does.Contain("FELLEGI_SUNTER_DECISION_EVIDENCE_V6"));
            Assert.That(triggerDefinition, Does.Contain("SCORING_BIRTH_SEMANTIC_EVIDENCE_V5"));
            Assert.That(triggerDefinition, Does.Contain("SCORING_DECISION_EVIDENCE_V6"));
            Assert.That(triggerDefinition, Does.Contain("CONFLICT_MARGIN_LOG_ODDS"));
            Assert.That(triggerDefinition, Does.Contain("M_NOME_MAE_MISSING"));
            Assert.That(triggerDefinition, Does.Contain("U_NOME_MAE_MISSING"));
            Assert.That(triggerDefinition, Does.Contain("SUPPORT_U_NASCIMENTO_SEMANTICO_"));
            Assert.That(triggerDefinition, Does.Contain("POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_"));
            Assert.That(triggerDefinition, Does.Contain("fn_linkage_birth_semantic_reachability"));
            Assert.That(functionDefinition, Is.Not.Null.And.Not.Empty);
            Assert.That(functionDefinition, Does.Contain("birth_day"));
            Assert.That(functionDefinition, Does.Contain("birth_month"));
            Assert.That(functionDefinition, Does.Contain("birth_year"));
            Assert.That(functionDefinition, Does.Contain("DAY_MONTH_SWAP"));
            Assert.That(functionDefinition, Does.Contain("OTHER_DISAGREEMENT"));
        });
    }

    [Test]
    public async Task Promotion_rejects_only_when_u_sample_loses_state_present_in_candidate_pool()
    {
        var connectionString = RequireIntegrationConnection();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ApplyContractAsync(connection);

        const string sql = """
            DECLARE @all_model UNIQUEIDENTIFIER=NEWID();
            DECLARE @all_ruleset UNIQUEIDENTIFIER=NEWID();
            DECLARE @filtered_model UNIQUEIDENTIFIER=NEWID();
            DECLARE @filtered_ruleset UNIQUEIDENTIFIER=NEWID();
            DECLARE @base_version INT=(SELECT ISNULL(MAX(versao),0)+100 FROM identidade.modelo_linkage);

            DECLARE @states TABLE(estado NVARCHAR(80) NOT NULL PRIMARY KEY);
            INSERT @states(estado) VALUES
                (N'EXACT'),
                (N'DAY_MONTH_SWAP'),
                (N'CENTURY_SHIFT'),
                (N'ONE_DIGIT_ERROR'),
                (N'TWO_DIGIT_ERROR'),
                (N'PARTIAL_COMPONENT_AGREEMENT'),
                (N'OTHER_DISAGREEMENT');

            INSERT identidade.modelo_linkage(
                modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                deduplicacao_metodo,base_referencia,registros_lidos,pessoas_unicas,
                gerado_em,amostra_metodo,amostra_pool_tamanho,amostra_m_tamanho,amostra_u_tamanho)
            VALUES
                (@all_model,@base_version,'RASCUNHO','FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',
                 'IDENTITY_NORMALIZATION_V1','TEST','TEST',1,1,SYSDATETIMEOFFSET(),'TEST',1,1,1),
                (@filtered_model,@base_version+1,'RASCUNHO','FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',
                 'IDENTITY_NORMALIZATION_V1','TEST','TEST',1,1,SYSDATETIMEOFFSET(),'TEST',1,1,1);

            INSERT identidade.linkage_ruleset(
                ruleset_id,modelo_id,ruleset_versao,algoritmo_versao,fingerprint_sha256)
            VALUES
                (@all_ruleset,@all_model,N'TEST_ALL_REACHABLE','FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',REPLICATE('a',64)),
                (@filtered_ruleset,@filtered_model,N'TEST_BIRTH_MONTH_YEAR','FELLEGI_SUNTER_SEMANTIC_BIRTH_V5',REPLICATE('b',64));

            INSERT identidade.linkage_ruleset_passe(ruleset_id,passe_ordem,passe_id)
            VALUES
                (@all_ruleset,0,N'P_NAME'),
                (@filtered_ruleset,0,N'P_BIRTH_MY');

            INSERT identidade.linkage_ruleset_passe_campo(ruleset_id,passe_ordem,campo_ordem,atributo)
            VALUES(@all_ruleset,0,0,N'mother_name_last');

            INSERT identidade.linkage_ruleset_passe_campo(ruleset_id,passe_ordem,campo_ordem,atributo)
            VALUES
                (@filtered_ruleset,0,0,N'birth_month'),
                (@filtered_ruleset,0,1,N'birth_year');

            -- Modelo A: DAY_MONTH_SWAP existe no pool, mas sumiu da amostra u => rejeitar.
            INSERT identidade.parametro_linkage(modelo_id,nome,valor)
            SELECT @all_model,N'M_NASCIMENTO_SEMANTICO_'+estado,CONVERT(DECIMAL(30,12),0.142857) FROM @states
            UNION ALL
            SELECT @all_model,N'U_NASCIMENTO_SEMANTICO_'+estado,CONVERT(DECIMAL(30,12),0.142857) FROM @states
            UNION ALL
            SELECT @all_model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+estado,
                   CONVERT(DECIMAL(30,12),CASE WHEN estado=N'DAY_MONTH_SWAP' THEN 0 ELSE 1 END) FROM @states
            UNION ALL
            SELECT @all_model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_'+estado,
                   CONVERT(DECIMAL(30,12),1) FROM @states;
            INSERT identidade.parametro_linkage(modelo_id,nome,valor)
            VALUES(@all_model,N'SCORING_BIRTH_SEMANTIC_EVIDENCE_V5',1);

            -- Modelo B: birth_month+birth_year só admite estados em que mês e ano
            -- permanecem iguais. Os demais ficam ausentes tanto da amostra quanto do
            -- pool; smoothing nesses estados é aceitável.
            INSERT identidade.parametro_linkage(modelo_id,nome,valor)
            SELECT @filtered_model,N'M_NASCIMENTO_SEMANTICO_'+estado,CONVERT(DECIMAL(30,12),0.142857) FROM @states
            UNION ALL
            SELECT @filtered_model,N'U_NASCIMENTO_SEMANTICO_'+estado,CONVERT(DECIMAL(30,12),0.142857) FROM @states
            UNION ALL
            SELECT @filtered_model,N'SUPPORT_U_NASCIMENTO_SEMANTICO_'+estado,
                   CONVERT(DECIMAL(30,12),CASE WHEN estado IN (N'EXACT',N'ONE_DIGIT_ERROR') THEN 1 ELSE 0 END) FROM @states
            UNION ALL
            SELECT @filtered_model,N'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO_'+estado,
                   CONVERT(DECIMAL(30,12),CASE WHEN estado IN (N'EXACT',N'ONE_DIGIT_ERROR') THEN 1 ELSE 0 END) FROM @states;
            INSERT identidade.parametro_linkage(modelo_id,nome,valor)
            VALUES(@filtered_model,N'SCORING_BIRTH_SEMANTIC_EVIDENCE_V5',1);

            IF EXISTS(
                SELECT 1 FROM identidade.fn_linkage_birth_semantic_reachability(@all_model)
                WHERE alcancavel=0)
                THROW 51980,'Passe sem nascimento deveria tornar todos os estados semânticos alcançáveis.',1;

            IF (SELECT alcancavel FROM identidade.fn_linkage_birth_semantic_reachability(@filtered_model)
                WHERE estado=N'DAY_MONTH_SWAP')<>0
                THROW 51981,'DAY_MONTH_SWAP não deveria ser alcançável por birth_month+birth_year.',1;

            DECLARE @lost_pool_state_rejected BIT=0;
            BEGIN TRY
                UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@all_model;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER()=51032 SET @lost_pool_state_rejected=1; ELSE THROW;
            END CATCH;

            IF @lost_pool_state_rejected=0
                THROW 51984,'Promoção deveria rejeitar estado presente no pool e ausente da amostra u.',1;

            UPDATE identidade.parametro_linkage
            SET valor=1
            WHERE modelo_id=@all_model
              AND nome=N'SUPPORT_U_NASCIMENTO_SEMANTICO_DAY_MONTH_SWAP';
            UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@all_model;

            UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@filtered_model;

            IF (SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@all_model)<>'VALIDADO'
                THROW 51985,'Modelo com cobertura da amostra corrigida não foi validado.',1;
            IF (SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@filtered_model)<>'VALIDADO'
                THROW 51986,'Modelo com zero ausente do pool não foi validado.',1;
            """;

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        Assert.DoesNotThrowAsync(async () => await command.ExecuteNonQueryAsync());
    }

    private static async Task ApplyContractAsync(SqlConnection connection)
    {
        var databaseDir = Path.Combine(AppContext.BaseDirectory, "database");
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "Jornada_Fase1.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260910_Linkage_RuleSet_Passes.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260915_Linkage_Model_Promotion_Contract.sql"));
        await SqlBatchRunner.ExecuteFileAsync(connection, Path.Combine(databaseDir, "migrations", "20260916_Linkage_U_Support_Reachability.sql"));
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("dev", StringComparison.OrdinalIgnoreCase)
            && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString;
    }
}
