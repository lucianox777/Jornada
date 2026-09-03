using System.IO.Compression;
using System.Text;

namespace Jornada.Ingestion;

/// <summary>
/// Escritor de referência para pacotes determinísticos da Fase 1.
/// Para os mesmos bytes de entrada e a mesma versão homologada do runtime/gerador,
/// fixa ordem, nomes, timestamp e atributos das entries para produzir o mesmo ZIP/SHA-256.
/// Geradores externos certificados devem reproduzir este perfil ou demonstrar equivalência byte a byte.
/// </summary>
public static class DeterministicIngestionZipWriter
{
    public static readonly DateTimeOffset CanonicalEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Create(
        ReadOnlyMemory<byte> manifestJson,
        ReadOnlyMemory<byte> pessoasJsonl,
        ReadOnlyMemory<byte> registrosJsonl)
    {
        using var output = new MemoryStream();
        Write(output, manifestJson, pessoasJsonl, registrosJsonl);
        return output.ToArray();
    }

    public static void Write(
        Stream output,
        ReadOnlyMemory<byte> manifestJson,
        ReadOnlyMemory<byte> pessoasJsonl,
        ReadOnlyMemory<byte> registrosJsonl)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new InvalidDataException("Stream de saída do ZIP deve ser gravável.");
        if (manifestJson.IsEmpty) throw new InvalidDataException("manifest.json não pode estar vazio.");
        if (pessoasJsonl.IsEmpty) throw new InvalidDataException("pessoas.jsonl não pode estar vazio.");

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8);
        WriteEntry(zip, "manifest.json", manifestJson);
        WriteEntry(zip, "pessoas.jsonl", pessoasJsonl);
        WriteEntry(zip, "registros.jsonl", registrosJsonl);
    }

    private static void WriteEntry(ZipArchive zip, string name, ReadOnlyMemory<byte> bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = CanonicalEntryTimestamp;
        entry.ExternalAttributes = 0;
        using var target = entry.Open();
        target.Write(bytes.Span);
    }
}
