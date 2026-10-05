using System.Net.Http.Headers;
using Jornada.Access.Security;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Ingestion;

namespace Jornada.Api;

public static class IngestionApi
{
    public static IEndpointRouteBuilder MapIngestionApi(this IEndpointRouteBuilder app)
    {
        // Uma única Entrega externa por ZIP, sempre com manifest.json + pessoas.jsonl + registros.jsonl.
        // registros.jsonl pode estar vazio; o contexto factual é opcional nesse caso. O nome do ZIP contém seu SHA-256.
        // O cliente não informa entregaId/loteSeq/loteTotal; lotes são internos.
        app.MapPost("/api/v1/ingestao/entregas", async (
            HttpRequest http,
            
            IPolicyEngine policy,
            IIngestionService service,
            IngestionStagingStore staging,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
        
            var idempotencyKey = http.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return Results.BadRequest(new { erro = "Idempotency-Key obrigatório." });
            if (idempotencyKey.Length > 200) return Results.BadRequest(new { erro = "Idempotency-Key excede 200 caracteres." });
            if (http.ContentType is null || !http.ContentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { erro = "Content-Type deve ser application/zip." });
            if (!ContentDispositionHeaderValue.TryParse(http.Headers["Content-Disposition"].ToString(), out var contentDisposition))
                return Results.BadRequest(new { erro = "Content-Disposition com filename canônico é obrigatório." });
            var rawFileName = (contentDisposition.FileNameStar ?? contentDisposition.FileName)?.Trim('"');
            if (string.IsNullOrWhiteSpace(rawFileName))
                return Results.BadRequest(new { erro = "filename do ZIP inválido." });
            var packageFileName = rawFileName;
            if (http.ContentLength is > IngestionPackageInspector.MaxCompressedBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        
            // Bloqueio genérico antes de receber bytes: uma credencial sem scope de ingestão não consome parsing/armazenamento temporário.
            if (!await policy.IsAllowedAsync(context, "jornada.ingestao.write", null, null, ct)) return Results.Forbid();
        
            StagedPackage temp;
            try
            {
                temp = await staging.ReceiveAsync(http.Body, IngestionPackageInspector.MaxCompressedBytes, ct);
            }
            catch (InvalidDataException)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            catch (IngestionStagingUnavailableException)
            {
                http.HttpContext.Response.Headers.RetryAfter = "60";
                return Results.Json(new { codigo = "INGESTAO_STAGING_INDISPONIVEL", erro = "Staging temporário da ingestão indisponível." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        
            try
            {
                IngestionPackageManifest manifest;
                await using (var minimal = File.OpenRead(temp.Path))
                {
                    try { manifest = IngestionPackageInspector.ParseManifestForAuthorization(minimal, temp.Length); }
                    catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }
                }
        
                var resourceCode = manifest.CodigoTipo;
                ApiAuditContext.SetResourceCode(http.HttpContext, resourceCode);
                if (!await policy.IsAllowedAsync(context, "jornada.ingestao.write", resourceCode, null, ct)) return Results.Forbid();
        
                // Só depois da autorização do recurso executamos a validação pesada do conteúdo descompactado.
                await using (var full = File.OpenRead(temp.Path))
                {
                    try { manifest = IngestionPackageInspector.ParseAndValidate(full, temp.Length); }
                    catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }
                }
        
                try { IngestionPackageInspector.ValidateCanonicalFileName(packageFileName, manifest, context, temp.Sha256); }
                catch (InvalidDataException ex) { return Results.BadRequest(new { erro = ex.Message }); }
        
                try
                {
                    await using var payload = File.OpenRead(temp.Path);
                    var receipt = await service.ReceivePackageAsync(context, idempotencyKey, manifest, packageFileName, payload, temp.Length, temp.Sha256, ct);
                    return Results.Accepted($"/api/v1/ingestao/entregas/{receipt.EntregaId}", receipt);
                }
                catch (IngestionConflictException ex) { return Results.Conflict(new { erro = ex.Message }); }
                catch (IngestionContractException ex) { return Results.BadRequest(new { erro = ex.Message }); }
                catch (BronzeObjectIntegrityException ex)
                {
                    var mapped = BronzeStorageHttpFailureMapper.Map(ex);
                    http.HttpContext.Response.Headers.RetryAfter = BronzeStorageHttpFailureMapper.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return Results.Json(new { codigo = mapped.Code, erro = mapped.Message }, statusCode: mapped.StatusCode);
                }
                catch (BronzeStorageUnavailableException ex)
                {
                    var mapped = BronzeStorageHttpFailureMapper.Map(ex);
                    http.HttpContext.Response.Headers.RetryAfter = BronzeStorageHttpFailureMapper.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return Results.Json(new { codigo = mapped.Code, erro = mapped.Message }, statusCode: mapped.StatusCode);
                }
            }
            finally
            {
                staging.TryDelete(temp.Path);
            }
        }).RequireRateLimiting("ingestion").RequireAuthorization("jornada.ingestao.write");
        
        app.MapGet("/api/v1/ingestao/entregas/{entregaId:guid}", async (
            HttpRequest http,
            Guid entregaId,
            
            IPolicyEngine policy,
            IIngestionService service,
            CancellationToken ct) =>
        {
            var context = http.HttpContext.RequireJornadaAccessContext();
            if (!await policy.IsAllowedAsync(context, "jornada.ingestao.status", null, null, ct)) return Results.Forbid();
            var result = await service.GetStatusAsync(context, entregaId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireRateLimiting("standard").RequireAuthorization("jornada.ingestao.status");

        return app;
    }
}
