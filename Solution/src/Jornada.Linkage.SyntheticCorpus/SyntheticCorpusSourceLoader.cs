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


public sealed record SyntheticCorpusDemographicNominalSource(
    SyntheticFrequencySampler PersonFirstNames,
    SyntheticFrequencySampler PersonSurnames,
    SyntheticFrequencySampler MotherFirstNames,
    SyntheticFrequencySampler MotherSurnames,
    string ReferenceCode,
    string PersonSourcePath,
    string PersonPhysicalSha256,
    string PersonCanonicalContentSha256,
    long PersonSourceRowCount,
    string MotherFirstSourcePath,
    string MotherFirstPhysicalSha256,
    string MotherFirstCanonicalContentSha256,
    long MotherFirstSourceRowCount,
    string MotherSurnameSourcePath,
    string MotherSurnamePhysicalSha256,
    string MotherSurnameCanonicalContentSha256,
    long MotherSurnameSourceRowCount,
    long PersonFirstNameCount,
    long PersonSurnameCount,
    long MotherFirstNameCount,
    long MotherSurnameCount);

public static class SyntheticCorpusSourceLoader
{

    public static async Task<SyntheticCorpusDemographicNominalSource> LoadDemographicPrimaryAsync(
        string referenceRoot,
        SyntheticCorpusOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceRoot);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var manifest = await IbgeProjectionReader.ReadManifestAsync(referenceRoot, cancellationToken);
        var municipality = manifest.Files.SingleOrDefault(x =>
            string.Equals(x.Kind, "MUNICIPIO", StringComparison.Ordinal))
            ?? throw new InvalidDataException("projection-manifest não contém MUNICIPIO único.");
        var brazilSex = manifest.Files.SingleOrDefault(x =>
            string.Equals(x.Kind, "BRASIL_SEXO", StringComparison.Ordinal))
            ?? throw new InvalidDataException("projection-manifest não contém BRASIL_SEXO único.");
        var brazilTotal = manifest.Files.SingleOrDefault(x =>
            string.Equals(x.Kind, "BRASIL_TOTAL", StringComparison.Ordinal))
            ?? throw new InvalidDataException("projection-manifest não contém BRASIL_TOTAL único.");

        var personRead = await IbgeProjectionReader.ReadFilteredAsync(
            referenceRoot,
            municipality,
            row => row.Frequencia >= options.MinFrequency
                && string.Equals(row.MunicipioCodigo, "3550308", StringComparison.Ordinal)
                && (string.Equals(row.Tipo, "NOME", StringComparison.Ordinal)
                    || string.Equals(row.Tipo, "SOBRENOME", StringComparison.Ordinal)),
            cancellationToken);
        var motherFirstRead = await IbgeProjectionReader.ReadFilteredAsync(
            referenceRoot,
            brazilSex,
            row => row.Frequencia >= options.MinFrequency
                && string.Equals(row.Tipo, "NOME", StringComparison.Ordinal)
                && string.Equals(row.Sexo, "FEMININO", StringComparison.Ordinal),
            cancellationToken);
        var motherSurnameRead = await IbgeProjectionReader.ReadFilteredAsync(
            referenceRoot,
            brazilTotal,
            row => row.Frequencia >= options.MinFrequency
                && string.Equals(row.Tipo, "SOBRENOME", StringComparison.Ordinal),
            cancellationToken);

        var personFirst = personRead.Rows
            .Where(x => string.Equals(x.Tipo, "NOME", StringComparison.Ordinal))
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();
        var personSurname = personRead.Rows
            .Where(x => string.Equals(x.Tipo, "SOBRENOME", StringComparison.Ordinal))
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();
        var motherFirst = motherFirstRead.Rows
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();
        var motherSurname = motherSurnameRead.Rows
            .Select(x => new SyntheticFrequencyValue(x.Valor!, x.Frequencia))
            .ToArray();

        if (personFirst.Length == 0 || personSurname.Length == 0)
            throw new InvalidDataException("Recorte municipal 3550308 não contém NOME/TODOS e SOBRENOME/TODOS após min_freq.");
        if (motherFirst.Length == 0)
            throw new InvalidDataException("Brasil/FEMININO não contém prenomes maternos após min_freq.");
        if (motherSurname.Length == 0)
            throw new InvalidDataException("Brasil/TODOS não contém sobrenomes maternos após min_freq.");

        return new SyntheticCorpusDemographicNominalSource(
            new SyntheticFrequencySampler(personFirst, options.TailOversample, .25),
            new SyntheticFrequencySampler(personSurname, options.TailOversample, .25),
            new SyntheticFrequencySampler(motherFirst, options.TailOversample, .25),
            new SyntheticFrequencySampler(motherSurname, options.TailOversample, .25),
            manifest.ReferenceCode,
            municipality.Path,
            personRead.PhysicalSha256,
            personRead.CanonicalContentSha256,
            personRead.RowCount,
            brazilSex.Path,
            motherFirstRead.PhysicalSha256,
            motherFirstRead.CanonicalContentSha256,
            motherFirstRead.RowCount,
            brazilTotal.Path,
            motherSurnameRead.PhysicalSha256,
            motherSurnameRead.CanonicalContentSha256,
            motherSurnameRead.RowCount,
            personFirst.LongLength,
            personSurname.LongLength,
            motherFirst.LongLength,
            motherSurname.LongLength);
    }

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
