using System.Data;
using System.Text.Json;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal sealed class SqlIdentityCorrectionService(SqlConnectionFactory connections) : IIdentityCorrectionService
{
    public async Task<IdentityConflictDetailResponse?> GetConflictAsync(
        AccessContext context, IdentityConflictDetailRequest request, CancellationToken ct)
    {
        var cpf = CpfRules.NormalizeAndValidate(request.Cpf)
            ?? throw new ArgumentException("CPF inválido.", nameof(request));
        await using var connection = await connections.OpenAsync(ct);
        Guid? prior = null;
        string state;
        string? reason;
        await using (var map = connection.CreateCommand())
        {
            map.CommandText = """
                SELECT TOP(1) pessoa_uuid,estado,estado_motivo
                FROM identidade.identity_map
                WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;
                """;
            map.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            await using var reader = await map.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            prior = reader.GetGuid(0);
            state = reader.GetString(1);
            reason = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        var nuclei = new List<IdentityConflictCoreDto>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT po.pessoa_observacao_id,vc.pessoa_uuid,g.codigo,po.codigo_pessoa_origem,
                       po.nome_completo,po.data_nascimento,po.nome_mae,COALESCE(vc.status,'NAO_RESOLVIDO'),vc.motivo
                FROM silver.pessoa_observacao po
                JOIN ref.gestor g ON g.gestor_id=po.gestor_id
                LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                WHERE po.cpf=@cpf
                ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC;
                """;
            command.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                nuclei.Add(new IdentityConflictCoreDto(
                    reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), DateOnly.FromDateTime(reader.GetDateTime(5)), reader.GetString(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }
        return new IdentityConflictDetailResponse(state, reason, prior, nuclei);
    }

    public async Task<IdentityCorrectionResponse> ApplyAsync(
        AccessContext context, IdentityCorrectionRequest request, Guid? correlationId, CancellationToken ct)
    {
        var cpf = CpfRules.NormalizeAndValidate(request.Cpf)
            ?? throw new ArgumentException("CPF inválido.", nameof(request));
        if (request.Grupos.Count == 0 || request.Grupos.Any(g => g.PessoaObservacaoIds.Count == 0))
            throw new ArgumentException("Informe ao menos um grupo e uma observação por grupo.", nameof(request));
        if (request.Grupos.Select(g => g.GrupoCodigo).Distinct(StringComparer.Ordinal).Count() != request.Grupos.Count)
            throw new ArgumentException("GrupoCodigo deve ser único.", nameof(request));
        if (!request.Grupos.Any(g => string.Equals(g.GrupoCodigo, request.GrupoTitularCpf, StringComparison.Ordinal)))
            throw new ArgumentException("GrupoTitularCpf deve existir em Grupos.", nameof(request));

        var groupsJson = JsonSerializer.Serialize(request.Grupos.Select(g => new
        {
            grupoCodigo = g.GrupoCodigo,
            pessoaUuidDestino = g.PessoaUuidDestino,
            pessoaObservacaoIds = g.PessoaObservacaoIds
        }));

        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "identidade.sp_aplicar_correcao_identidade";
            command.Parameters.Add(new SqlParameter("@gestor_codigo", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            command.Parameters.Add(new SqlParameter("@cpf", SqlDbType.Char, 11) { Value = cpf });
            command.Parameters.Add(new SqlParameter("@grupo_titular", SqlDbType.NVarChar, 80) { Value = request.GrupoTitularCpf });
            command.Parameters.Add(new SqlParameter("@grupos_json", SqlDbType.NVarChar, -1) { Value = groupsJson });
            command.Parameters.Add(new SqlParameter("@ato_referencia", SqlDbType.NVarChar, 300) { Value = request.AtoReferencia });
            command.Parameters.Add(new SqlParameter("@justificativa", SqlDbType.NVarChar, 2000) { Value = request.Justificativa });
            command.Parameters.Add(new SqlParameter("@correlation_id", SqlDbType.UniqueIdentifier) { Value = (object?)correlationId ?? DBNull.Value });
            var correction = command.Parameters.Add("@correcao_id", SqlDbType.UniqueIdentifier);
            correction.Direction = ParameterDirection.Output;
            var titular = command.Parameters.Add("@pessoa_uuid_titular", SqlDbType.UniqueIdentifier);
            titular.Direction = ParameterDirection.Output;
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);

            var correctionId = (Guid)correction.Value;
            var titularUuid = (Guid)titular.Value;
            var destinations = await LoadDestinationsAsync(connection, correctionId, ct);
            return new IdentityCorrectionResponse(correctionId, titularUuid, destinations, "APLICADA");
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IdentityGovernedCaseOpenResponse> OpenCaseAsync(
        AccessContext context, IdentityGovernedCaseOpenRequest request, Guid? correlationId, CancellationToken ct)
    {
        if (request.PessoaObservacaoIds.Count == 0)
            throw new ArgumentException("Informe ao menos uma observação.", nameof(request));
        var ids = request.PessoaObservacaoIds.Distinct().ToArray();
        if (ids.Length != request.PessoaObservacaoIds.Count)
            throw new ArgumentException("Observações duplicadas não são permitidas.", nameof(request));
        var json = JsonSerializer.Serialize(ids);
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "identidade.sp_abrir_caso_conflito_identidade";
            command.Parameters.Add(new SqlParameter("@gestor_codigo", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            command.Parameters.Add(new SqlParameter("@motivo", SqlDbType.NVarChar, 50) { Value = request.Motivo });
            command.Parameters.Add(new SqlParameter("@observacoes_json", SqlDbType.NVarChar, -1) { Value = json });
            command.Parameters.Add(new SqlParameter("@ato_referencia", SqlDbType.NVarChar, 300) { Value = request.AtoReferencia });
            command.Parameters.Add(new SqlParameter("@justificativa", SqlDbType.NVarChar, 2000) { Value = request.Justificativa });
            command.Parameters.Add(new SqlParameter("@correlation_id", SqlDbType.UniqueIdentifier) { Value = (object?)correlationId ?? DBNull.Value });
            var output = command.Parameters.Add("@caso_id", SqlDbType.UniqueIdentifier);
            output.Direction = ParameterDirection.Output;
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return new IdentityGovernedCaseOpenResponse((Guid)output.Value, "ABERTO");
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IdentityGovernedCaseApplyResponse> ApplyCaseAsync(
        AccessContext context, Guid caseId, IdentityGovernedCaseApplyRequest request, Guid? correlationId, CancellationToken ct)
    {
        if (request.Grupos.Count == 0 || request.Grupos.Any(g => g.PessoaObservacaoIds.Count == 0))
            throw new ArgumentException("Informe ao menos um grupo e uma observação por grupo.", nameof(request));
        if (request.Grupos.Select(g => g.GrupoCodigo).Distinct(StringComparer.Ordinal).Count() != request.Grupos.Count)
            throw new ArgumentException("GrupoCodigo deve ser único.", nameof(request));
        var json = JsonSerializer.Serialize(request.Grupos.Select(g => new
        {
            grupoCodigo = g.GrupoCodigo,
            pessoaUuidDestino = g.PessoaUuidDestino,
            pessoaObservacaoIds = g.PessoaObservacaoIds
        }));
        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "identidade.sp_aplicar_caso_conflito_identidade";
            command.Parameters.Add(new SqlParameter("@gestor_codigo", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
            command.Parameters.AddWithValue("@caso_id", caseId);
            command.Parameters.Add(new SqlParameter("@grupos_json", SqlDbType.NVarChar, -1) { Value = json });
            command.Parameters.Add(new SqlParameter("@correlation_id", SqlDbType.UniqueIdentifier) { Value = (object?)correlationId ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return new IdentityGovernedCaseApplyResponse(caseId, "APLICADO");
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<IdentityDivergenceDto>> ListDivergencesAsync(
        AccessContext context, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var result = new List<IdentityDivergenceDto>();
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(@limit) d.divergencia_id,d.tipo,d.motivo,d.pessoa_observacao_id,d.registro_observacao_id,
                   d.codigo_pessoa_origem,d.correlation_id,d.aberta_em
            FROM qualidade.divergencia_gestor d
            JOIN ref.gestor g ON g.gestor_id=d.gestor_id
            WHERE g.codigo=@gestor AND d.status='ABERTA'
            ORDER BY d.aberta_em,d.divergencia_id;
            """;
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new IdentityDivergenceDto(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
                reader.IsDBNull(3)?null:reader.GetInt64(3),reader.IsDBNull(4)?null:reader.GetInt64(4),
                reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetGuid(6),reader.GetDateTimeOffset(7)));
        return result;
    }

    public async Task ResolveDivergenceAsync(
        AccessContext context, long divergenceId, IdentityDivergenceDispositionRequest request, Guid? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Desfecho))
            throw new ArgumentException("Desfecho é obrigatório.", nameof(request));
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = "qualidade.sp_registrar_desfecho_divergencia";
        command.Parameters.Add(new SqlParameter("@gestor_codigo", SqlDbType.NVarChar, 30) { Value = context.GestorCodigo });
        command.Parameters.AddWithValue("@divergencia_id", divergenceId);
        command.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 20) { Value = request.Status });
        command.Parameters.Add(new SqlParameter("@desfecho", SqlDbType.NVarChar, 80) { Value = request.Desfecho });
        command.Parameters.Add(new SqlParameter("@observacao", SqlDbType.NVarChar, 2000) { Value = (object?)request.Observacao ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@correlation_id", SqlDbType.UniqueIdentifier) { Value = (object?)correlationId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyDictionary<string, Guid>> LoadDestinationsAsync(SqlConnection connection, Guid correctionId, CancellationToken ct)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT grupo_codigo,pessoa_uuid_destino FROM identidade.correcao_identidade_item WHERE correcao_id=@id;";
        command.Parameters.AddWithValue("@id", correctionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[reader.GetString(0)] = reader.GetGuid(1);
        return result;
    }
}
