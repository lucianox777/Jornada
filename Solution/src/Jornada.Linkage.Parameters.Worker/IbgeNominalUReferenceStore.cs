using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// O Monte Carlo IBGE e derivado imutavel de ref.frequencia_nome_*.
/// A chave inclui hash da fonte, versoes, recorte, seed e numero de pares.
/// Cache hit le ref; somente ENSURE pode recalcular e publicar nova versao.
/// </summary>
public sealed record IbgeNominalUStoredReference(
    long Id,
    string ResultSha256,
    IbgeNominalUBootstrapEstimate Estimate);

public static class IbgeNominalUReferenceStore
{
    public const string EnsureOperation = "ENSURE_IBGE_NOMINAL_U_REFERENCE";
    private const NameComparisonContract ComparisonContract =
        NameComparisonContract.WholeNameJaroWinklerV1;

    public static async Task<IbgeNominalUStoredReference> RequireAsync(
        SqlConnection connection,
        IbgeNominalUReferenceInfo source,
        string firstNameSex,
        IbgeNominalUBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ValidateKey(firstNameSex, options);
        return await ReadAsync(connection, null, source, firstNameSex, options, cancellationToken)
            ?? throw new InvalidOperationException(
                "Bootstrap nominal IBGE ausente para referencia/metodo/seed/PairCount correntes. " +
                "Execute ENSURE_IBGE_NOMINAL_U_REFERENCE antes de GENERATE_DRAFT; " +
                "o Worker nao recalibra derivados de ref implicitamente.");
    }

    public static async Task<IbgeNominalUStoredReference> EnsureAsync(
        SqlConnection connection,
        IbgeNominalUReferenceInfo source,
        string firstNameSex,
        IbgeNominalUBootstrapOptions options,
        CancellationToken cancellationToken)
    {
        ValidateKey(firstNameSex, options);
        var already = await ReadAsync(connection, null, source, firstNameSex, options, cancellationToken);
        if (already is not null)
            return already;

        var input = await IbgeNominalUReferenceReader.ReadBrazilPublishedMarginalsAsync(
            connection, source.Id, firstNameSex, cancellationToken);
        var raw = IbgeNominalUBootstrapEstimator.Estimate(input, options, ComparisonContract);
        var normalized = Normalize(raw);
        var fingerprint = ComputeFingerprint(source, firstNameSex, normalized);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Dois ENSURE concorrentes podem computar em paralelo, mas so um publica.
            var another = await ReadAsync(
                connection, transaction, source, firstNameSex, options, cancellationToken);
            if (another is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return another;
            }

            var insert = new SqlCommand(
                """
                INSERT ref.ibge_u_referencia(
                  frequencia_nome_versao_id,conteudo_origem_sha256,metodo_versao,
                  construcao_versao,canal_versao,comparador_versao,recorte_prenome,
                  seed,pares,vocabulario_prenomes,vocabulario_sobrenomes,
                  ocorrencias_prenomes,ocorrencias_sobrenomes,
                  colisoes_prenome,colisoes_sobrenome,colisoes_nome_completo,
                  status)
                VALUES(@ref,@source_sha,@method,@joint,@channel,@contract,@sex,
                  @seed,@pairs,@first_vocab,@surname_vocab,@first_occ,@surname_occ,
                  @first_collision,@surname_collision,@full_collision,N'CARREGANDO');
                SELECT CONVERT(BIGINT,SCOPE_IDENTITY());
                """, connection, transaction);
            insert.Parameters.Add("@ref", SqlDbType.BigInt).Value = source.Id;
            insert.Parameters.Add("@source_sha", SqlDbType.Binary, 32).Value =
                Convert.FromHexString(source.ContentSha256);
            insert.Parameters.Add("@method", SqlDbType.NVarChar, 80).Value = raw.MethodVersion;
            insert.Parameters.Add("@joint", SqlDbType.NVarChar, 100).Value = raw.JointConstructionVersion;
            insert.Parameters.Add("@channel", SqlDbType.NVarChar, 100).Value = raw.ObservationChannelVersion;
            insert.Parameters.Add("@contract", SqlDbType.NVarChar, 60).Value = ComparisonContract.ToString();
            insert.Parameters.Add("@sex", SqlDbType.NVarChar, 12).Value = firstNameSex;
            insert.Parameters.Add("@seed", SqlDbType.Int).Value = raw.Seed;
            insert.Parameters.Add("@pairs", SqlDbType.Int).Value = raw.PairCount;
            insert.Parameters.Add("@first_vocab", SqlDbType.Int).Value = raw.FirstNameVocabularySize;
            insert.Parameters.Add("@surname_vocab", SqlDbType.Int).Value = raw.SurnameVocabularySize;
            insert.Parameters.Add("@first_occ", SqlDbType.BigInt).Value = raw.FirstNamePublishedOccurrences;
            insert.Parameters.Add("@surname_occ", SqlDbType.BigInt).Value = raw.SurnamePublishedOccurrences;
            AddDecimal(insert, "@first_collision", normalized.AnalyticExactFirstNameProbability);
            AddDecimal(insert, "@surname_collision", normalized.AnalyticExactSurnameProbability);
            AddDecimal(insert, "@full_collision", normalized.AnalyticExactSyntheticFullNameProbability);
            var id = Convert.ToInt64(
                await insert.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            var states = new SqlCommand(
                """
                INSERT ref.ibge_u_referencia_estado(
                  ibge_u_referencia_id,estado,suporte,probabilidade,erro_padrao)
                VALUES(@id,@state,@support,@probability,@error);
                """, connection, transaction);
            states.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            var stateParameter = states.Parameters.Add("@state", SqlDbType.NVarChar, 20);
            var supportParameter = states.Parameters.Add("@support", SqlDbType.BigInt);
            var probabilityParameter = states.Parameters.Add("@probability", SqlDbType.Decimal);
            probabilityParameter.Precision = 30; probabilityParameter.Scale = 12;
            var errorParameter = states.Parameters.Add("@error", SqlDbType.Decimal);
            errorParameter.Precision = 30; errorParameter.Scale = 12;

            foreach (var row in normalized.States)
            {
                stateParameter.Value = row.State;
                supportParameter.Value = row.Support;
                probabilityParameter.Value = row.Probability;
                errorParameter.Value = row.StandardError;
                await states.ExecuteNonQueryAsync(cancellationToken);
            }

            var publish = new SqlCommand(
                """
                UPDATE ref.ibge_u_referencia
                   SET status=N'PRONTA',resultado_sha256=@hash,publicado_em=SYSDATETIMEOFFSET()
                 WHERE ibge_u_referencia_id=@id AND status=N'CARREGANDO';
                IF @@ROWCOUNT<>1 THROW 52086,'Bootstrap IBGE nao publicado de forma unica.',1;
                """, connection, transaction);
            publish.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            publish.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = fingerprint;
            await publish.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await RequireAsync(connection, source, firstNameSex, options, cancellationToken);
    }

    private static async Task<IbgeNominalUStoredReference?> ReadAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        IbgeNominalUReferenceInfo source,
        string firstNameSex,
        IbgeNominalUBootstrapOptions options,
        CancellationToken ct)
    {
        var command = new SqlCommand(
            """
            SELECT ibge_u_referencia_id,status,resultado_sha256,
                   vocabulario_prenomes,vocabulario_sobrenomes,
                   ocorrencias_prenomes,ocorrencias_sobrenomes,
                   colisoes_prenome,colisoes_sobrenome,colisoes_nome_completo
            FROM ref.ibge_u_referencia
            WHERE frequencia_nome_versao_id=@ref
              AND conteudo_origem_sha256=@source_sha
              AND metodo_versao=@method AND construcao_versao=@joint
              AND canal_versao=@channel AND comparador_versao=@contract
              AND recorte_prenome=@sex AND seed=@seed AND pares=@pairs;
            """, connection, transaction);
        command.Parameters.Add("@ref", SqlDbType.BigInt).Value = source.Id;
        command.Parameters.Add("@source_sha", SqlDbType.Binary, 32).Value =
            Convert.FromHexString(source.ContentSha256);
        command.Parameters.Add("@method", SqlDbType.NVarChar, 80).Value =
            IbgeNominalUBootstrapOptions.MethodVersion;
        command.Parameters.Add("@joint", SqlDbType.NVarChar, 100).Value =
            IbgeNominalUBootstrapOptions.JointConstructionVersion;
        command.Parameters.Add("@channel", SqlDbType.NVarChar, 100).Value =
            IbgeNominalUBootstrapOptions.ObservationChannelVersion;
        command.Parameters.Add("@contract", SqlDbType.NVarChar, 60).Value = ComparisonContract.ToString();
        command.Parameters.Add("@sex", SqlDbType.NVarChar, 12).Value = firstNameSex;
        command.Parameters.Add("@seed", SqlDbType.Int).Value = options.Seed;
        command.Parameters.Add("@pairs", SqlDbType.Int).Value = options.PairCount;

        long id;
        byte[] hash;
        int firstVocabulary, surnameVocabulary;
        long firstOccurrences, surnameOccurrences;
        decimal firstCollision, surnameCollision, fullCollision;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return null;
            id = reader.GetInt64(0);
            if (!string.Equals(reader.GetString(1), "PRONTA", StringComparison.Ordinal)
                || reader.IsDBNull(2))
                throw new InvalidDataException("Referencia u nominal incompleta: publicacao nao atomica.");
            hash = (byte[])reader.GetValue(2);
            firstVocabulary = reader.GetInt32(3);
            surnameVocabulary = reader.GetInt32(4);
            firstOccurrences = reader.GetInt64(5);
            surnameOccurrences = reader.GetInt64(6);
            firstCollision = reader.GetDecimal(7);
            surnameCollision = reader.GetDecimal(8);
            fullCollision = reader.GetDecimal(9);
            if (await reader.ReadAsync(ct))
                throw new InvalidDataException("Referencia u nominal possui chave duplicada.");
        }

        var readStates = new SqlCommand(
            """
            SELECT estado,suporte,probabilidade,erro_padrao
            FROM ref.ibge_u_referencia_estado WHERE ibge_u_referencia_id=@id
            ORDER BY estado;
            """, connection, transaction);
        readStates.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        var rows = new List<IbgeNominalUStateEstimate>(4);
        await using (var reader = await readStates.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                rows.Add(new(
                    reader.GetString(0), reader.GetInt64(1),
                    reader.GetDecimal(2), reader.GetDecimal(3)));
        var expected = Enum.GetNames<NameComparisonState>()
            .Order(StringComparer.Ordinal).ToArray();
        if (!rows.Select(x => x.State).SequenceEqual(expected, StringComparer.Ordinal)
            || rows.Sum(x => x.Support) != options.PairCount)
            throw new InvalidDataException("Derivado IBGE com suporte ou estados incompletos.");
        var estimate = new IbgeNominalUBootstrapEstimate(
            IbgeNominalUBootstrapOptions.MethodVersion,
            IbgeNominalUBootstrapOptions.JointConstructionVersion,
            IbgeNominalUBootstrapOptions.ObservationChannelVersion,
            options.Seed, options.PairCount, firstOccurrences, surnameOccurrences,
            firstVocabulary, surnameVocabulary,
            firstCollision, surnameCollision, fullCollision,
            rows.ToArray());
        var computed = ComputeFingerprint(source, firstNameSex, estimate);
        if (!hash.AsSpan().SequenceEqual(computed))
            throw new InvalidDataException("SHA-256 do bootstrap IBGE nao confere com os estados persistidos.");
        return new(id, Convert.ToHexString(hash).ToLowerInvariant(), estimate);
    }

    private static byte[] ComputeFingerprint(
        IbgeNominalUReferenceInfo source,
        string sex,
        IbgeNominalUBootstrapEstimate estimate)
    {
        var canonical = string.Join("\n", new[]
        {
            source.Id.ToString(CultureInfo.InvariantCulture),
            source.ContentSha256.ToUpperInvariant(),
            estimate.MethodVersion, estimate.JointConstructionVersion,
            estimate.ObservationChannelVersion, ComparisonContract.ToString(), sex,
            estimate.Seed.ToString(CultureInfo.InvariantCulture),
            estimate.PairCount.ToString(CultureInfo.InvariantCulture),
            estimate.FirstNameVocabularySize.ToString(CultureInfo.InvariantCulture),
            estimate.SurnameVocabularySize.ToString(CultureInfo.InvariantCulture),
            estimate.FirstNamePublishedOccurrences.ToString(CultureInfo.InvariantCulture),
            estimate.SurnamePublishedOccurrences.ToString(CultureInfo.InvariantCulture),
            Num(estimate.AnalyticExactFirstNameProbability),
            Num(estimate.AnalyticExactSurnameProbability),
            Num(estimate.AnalyticExactSyntheticFullNameProbability)
        }.Concat(estimate.States.OrderBy(x => x.State, StringComparer.Ordinal)
            .Select(x => string.Join("|",
                x.State, x.Support.ToString(CultureInfo.InvariantCulture),
                Num(x.Probability), Num(x.StandardError)))) + "\n";
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static IbgeNominalUBootstrapEstimate Normalize(IbgeNominalUBootstrapEstimate raw) =>
        raw with
        {
            AnalyticExactFirstNameProbability = Round12(raw.AnalyticExactFirstNameProbability),
            AnalyticExactSurnameProbability = Round12(raw.AnalyticExactSurnameProbability),
            AnalyticExactSyntheticFullNameProbability = Round12(raw.AnalyticExactSyntheticFullNameProbability),
            States = raw.States.Select(x => x with
            {
                Probability = Round12(x.Probability),
                StandardError = Round12(x.StandardError)
            }).ToArray()
        };

    private static string Num(decimal x) =>
        x.ToString("G29", CultureInfo.InvariantCulture);
    private static decimal Round12(decimal value) =>
        decimal.Round(value, 12, MidpointRounding.AwayFromZero);
    private static void AddDecimal(SqlCommand cmd, string name, decimal value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 30; p.Scale = 12; p.Value = value;
    }
    private static void ValidateKey(string firstNameSex, IbgeNominalUBootstrapOptions options)
    {
        if (firstNameSex is not ("TODOS" or "FEMININO"))
            throw new ArgumentOutOfRangeException(nameof(firstNameSex));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PairCount);
    }
}
