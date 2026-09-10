using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlLinkage"), NonParallelizable]
public sealed class PostgreSqlHistoricalAliasBlockingTests
{
    private static readonly Guid PersonId = Guid.Parse("83000000-0000-4000-8000-000000000069");
    private static readonly DateOnly Birth = new(1984, 7, 19);
    private const string CurrentName = "Maria Oliveira Santos";
    private const string HistoricalName = "Maria Silva Santos";
    private const string MotherName = "Ana Pereira";
    private string connectionString = null!;

    [OneTimeSetUp]
    public void Initialize()
    {
        connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION é obrigatória.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_LINKAGE_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgLinkageTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgLinkageTest.");
    }

    [Test]
    public async Task Historical_name_alias_recovers_candidate_but_gold_current_name_is_returned()
    {
        await CleanupAsync();
        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    INSERT INTO identidade.pessoa(pessoa_uuid,status)
                    VALUES(@id,'ATIVO');

                    INSERT INTO gold.pessoa(
                        pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
                        fontes_distintas,estado_concordancia)
                    VALUES(@id,NULL,'AUSENTE',@current_name,@birth,@mother,1,'BASELINE_FONTE_UNICA');

                    INSERT INTO identidade.blocking_chave(
                        pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                        semantica_temporal,vigencia_inicio,vigencia_fim)
                    VALUES
                    (@id,@normalization,@name_feature,@historical_name,'VERSIONED_ALIAS',
                        TIMESTAMPTZ '2020-01-01 00:00:00+00',TIMESTAMPTZ '2022-01-01 00:00:00+00'),
                    (@id,@normalization,@birth_feature,@birth_year,'STABLE_IDENTITY_DATUM',
                        TIMESTAMPTZ '2020-01-01 00:00:00+00',NULL);
                    """, connection);
                command.Parameters.AddWithValue("id", PersonId);
                command.Parameters.AddWithValue("current_name", CurrentName);
                command.Parameters.AddWithValue("birth", Birth);
                command.Parameters.AddWithValue("mother", MotherName);
                command.Parameters.AddWithValue("normalization", IdentityComparison.NormalizationVersion);
                command.Parameters.AddWithValue("name_feature", BlockingFeatureNames.FullName);
                command.Parameters.AddWithValue("historical_name", IdentityComparison.NormalizeText(HistoricalName));
                command.Parameters.AddWithValue("birth_feature", BlockingFeatureNames.BirthYear);
                command.Parameters.AddWithValue("birth_year", Birth.Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync();
            }

            var ruleSet = LinkageDynamicRuleSet.CreateWithPasses(
                "CI_HISTORICAL_ALIAS_BLOCKING_V1",
                "FELLEGI_SUNTER_BIRTH_COMPONENTS_V2",
                new[]
                {
                    LinkageBlockingPass.Create("P001", new[]
                    {
                        BlockingFeatureNames.FullName,
                        BlockingFeatureNames.BirthYear
                    })
                },
                Array.Empty<KeyValuePair<string, decimal>>());

            // Caminho propositalmente sem CPF: CPF presente pertence exclusivamente à resolução determinística.
            var observation = new IdentityObservation(
                null,
                "NAO_INFORMADO",
                HistoricalName,
                Birth,
                MotherName);

            await using var lookupConnection = new NpgsqlConnection(connectionString);
            await lookupConnection.OpenAsync();
            var candidates = await BlockingProjectionCandidateLoader.LoadAsync(
                lookupConnection,
                ruleSet,
                observation,
                1000,
                60,
                BlockingQueryDialect.PostgreSql,
                CancellationToken.None);

            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(candidates[0].PessoaUuid, Is.EqualTo(PersonId));
                Assert.That(candidates[0].NomeCompleto, Is.EqualTo(CurrentName),
                    "O alias histórico só recupera o candidato; não substitui o nome corrente da Gold.");
                Assert.That(candidates[0].DataNascimento, Is.EqualTo(Birth));
                Assert.That(candidates[0].NomeMae, Is.EqualTo(MotherName));
            });
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task CleanupAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@id;
            DELETE FROM gold.pessoa WHERE pessoa_uuid=@id;
            DELETE FROM identidade.pessoa WHERE pessoa_uuid=@id;
            """, connection);
        command.Parameters.AddWithValue("id", PersonId);
        await command.ExecuteNonQueryAsync();
    }
}
