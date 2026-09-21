namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticCorpusNominalSource(
    SyntheticFrequencySampler FirstNames,
    SyntheticFrequencySampler Surnames,
    string ReferenceCode,
    string SourcePath,
    string PhysicalSha256,
    string CanonicalContentSha256,
    long SourceRowCount,
    long FirstNameCount,
    long SurnameCount);

public static class SyntheticCorpusSourceLoader
{
    public static async Task<SyntheticCorpusNominalSource> LoadBrasilTotalAsync(
        string referenceRoot,
        SyntheticCorpusOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceRoot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var manifest = await IbgeProjectionReader.ReadManifestAsync(referenceRoot, cancellationToken);
        var file = manifest.Files.SingleOrDefault(x =>
            string.Equals(x.Kind, "BRASIL_TOTAL", StringComparison.Ordinal))
            ?? throw new InvalidDataException("projection-manifest não contém BRASIL_TOTAL único.");

        var read = await IbgeProjectionReader.ReadFilteredAsync(
            referenceRoot,
            file,
            row => row.Frequencia >= options.MinFrequency
                && (string.Equals(row.Tipo, "NOME", StringComparison.Ordinal)
                    || string.Equals(row.Tipo, "SOBRENOME", StringComparison.Ordinal)),
            cancellationToken);

        var first = read.Rows
            .Where(x => string.Equals(x.Tipo, "NOME", StringComparison.Ordinal))
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();
        var surnames = read.Rows
            .Where(x => string.Equals(x.Tipo, "SOBRENOME", StringComparison.Ordinal))
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();

        if (first.Length == 0)
            throw new InvalidDataException("Vocabulário NOME vazio após min_freq.");
        if (surnames.Length == 0)
            throw new InvalidDataException("Vocabulário SOBRENOME vazio após min_freq.");

        return new SyntheticCorpusNominalSource(
            new SyntheticFrequencySampler(first, options.TailOversample, .25),
            new SyntheticFrequencySampler(surnames, options.TailOversample, .25),
            manifest.ReferenceCode,
            file.Path,
            read.PhysicalSha256,
            read.CanonicalContentSha256,
            read.RowCount,
            first.LongLength,
            surnames.LongLength);
    }
}
