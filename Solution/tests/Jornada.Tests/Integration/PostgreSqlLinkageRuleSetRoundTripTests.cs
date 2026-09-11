using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlLinkage"), NonParallelizable]
public sealed class PostgreSqlLinkageRuleSetRoundTripTests
{
    [Test]
    public async Task Writer_and_reader_preserve_exact_version_passes_parameters_fingerprint_and_projection_contract()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION é obrigatória.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_LINKAGE_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgLinkageTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgLinkageTest.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var modelId = Guid.Parse("8f000000-0000-4000-8000-000000000069");
            const string algorithm = "ROUNDTRIP_RULESET_TEST_V1";
            await using (var model = new NpgsqlCommand("""
                INSERT INTO identidade.modelo_linkage(
                    modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                    deduplicacao_metodo,base_referencia,gerado_em)
                VALUES(@id,690069,'RASCUNHO',@algorithm,@normalization,
                    'ROUNDTRIP_TEST','CI_ROUNDTRIP',CURRENT_TIMESTAMP);
                """, connection, transaction))
            {
                model.Parameters.AddWithValue("id", modelId);
                model.Parameters.AddWithValue("algorithm", algorithm);
                model.Parameters.AddWithValue("normalization", IdentityComparison.NormalizationVersion);
                await model.ExecuteNonQueryAsync();
            }

            var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["CONFLICT_MARGIN"] = 0.03m,
                ["T_LINKAGE"] = 0.95m
            };
            foreach (var parameter in parameters)
            {
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@id,@name,@value);",
                    connection,
                    transaction);
                insert.Parameters.AddWithValue("id", modelId);
                insert.Parameters.AddWithValue("name", parameter.Key);
                insert.Parameters.AddWithValue("value", parameter.Value);
                await insert.ExecuteNonQueryAsync();
            }

            var expected = LinkageDynamicRuleSet.CreateWithPasses(
                "ROUNDTRIP_BLOCKING_V1",
                algorithm,
                new[]
                {
                    LinkageBlockingPass.Create("NAME_BIRTH", new[]
                    {
                        BlockingFeatureNames.FirstName,
                        BlockingFeatureNames.BirthYear
                    }),
                    LinkageBlockingPass.Create("MOTHER_BIRTH", new[]
                    {
                        BlockingFeatureNames.MotherFirstName,
                        BlockingFeatureNames.BirthYear
                    })
                },
                parameters);

            await LinkageRuleSetWriter.WriteAsync(connection, transaction, modelId, expected, CancellationToken.None);

            await using (var validate = new NpgsqlCommand(
                "UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@id;",
                connection,
                transaction))
            {
                validate.Parameters.AddWithValue("id", modelId);
                Assert.That(await validate.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }

            var actual = await LinkageRuleSetReader.TryLoadAsync(
                connection,
                modelId,
                CancellationToken.None,
                transaction);

            Assert.That(actual, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(actual!.RuleSetVersion, Is.EqualTo(expected.RuleSetVersion));
                Assert.That(actual.AlgorithmVersion, Is.EqualTo(expected.AlgorithmVersion));
                Assert.That(actual.FingerprintSha256, Is.EqualTo(expected.FingerprintSha256));
                Assert.That(actual.Parameters, Is.EqualTo(expected.Parameters));
                Assert.That(actual.ProjectionSchemaVersion, Is.EqualTo(PersonResolutionProjectionContract.SchemaVersion));
                Assert.That(actual.ProjectionFingerprintSha256, Is.EqualTo(PersonResolutionProjectionContract.FingerprintSha256));
                Assert.That(actual.BlockingPasses.Select(x => x.PassId),
                    Is.EqualTo(expected.BlockingPasses.Select(x => x.PassId)));
                Assert.That(actual.BlockingPasses.Select(x => string.Join("+", x.Fields)),
                    Is.EqualTo(expected.BlockingPasses.Select(x => string.Join("+", x.Fields))));
            });
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
