using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Jornada.Ingestion;
using Microsoft.VisualBasic.FileIO;

namespace Jornada.Tests.ExternalRealData;

[TestFixture]
[Category("ExternalRealData")]
[Category("Integration")]
public sealed class Sehab20240717CharacterizationTests
{
    private const string ZipEnvironmentVariable = "JORNADA_SEHAB_ASIS_ZIP";
    private string _zipPath = null!;

    private static readonly IReadOnlyDictionary<string, long> ExpectedRows = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["beneficios_cencedidos_aa.csv"] = 2_484_518,
        ["beneficios_cencedidos_ce.csv"] = 6_410,
        ["con_cbp_aa.csv"] = 48_679,
        ["con_contatos_emails_aa.csv"] = 18_097,
        ["con_contatos_emails_ce.csv"] = 34,
        ["con_contatos_endereco_aa.csv"] = 48_688,
        ["con_contatos_endereco_ce.csv"] = 1_060,
        ["con_contatos_telefones_aa.csv"] = 43_967,
        ["con_contatos_telefones_ce.csv"] = 1_325,
        ["con_informações_complementares_aa.csv"] = 48_670,
        ["con_informações_complementares_ce.csv"] = 1_060,
        ["con_relações_familiares_aa.csv"] = 59_323,
        ["con_relações_familiares_ce.csv"] = 242,
        ["dados_base_central_aa.csv"] = 45_774,
        ["dados_base_central_ce.csv"] = 6_828,
        ["dados_cbp_ce.csv"] = 5_947
    };

    [OneTimeSetUp]
    public void ResolveExternalSnapshot()
    {
        _zipPath = Environment.GetEnvironmentVariable(ZipEnvironmentVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_zipPath) || !File.Exists(_zipPath))
            Assert.Ignore($"Defina {ZipEnvironmentVariable} apontando para DADOS_SEHAB_20240717.zip para executar a caracterização real.");
    }

    [Test]
    public void Raw_sehab_zip_is_intentionally_not_a_jornada_v2_envelope()
    {
        using var stream = File.OpenRead(_zipPath);
        var ex = Assert.Throws<InvalidDataException>(() => IngestionPackageInspector.ParseAndValidate(stream, stream.Length));
        Assert.That(ex!.Message, Does.Contain("exatamente manifest.json, pessoas.jsonl e registros.jsonl"));
    }

    [Test]
    public void Snapshot_inventory_and_row_counts_are_frozen_as_is()
    {
        using var zip = ZipFile.OpenRead(_zipPath);
        Assert.That(zip.Entries.Select(e => e.FullName), Is.EquivalentTo(ExpectedRows.Keys));

        foreach (var pair in ExpectedRows)
        {
            var entry = zip.GetEntry(pair.Key);
            Assert.That(entry, Is.Not.Null, pair.Key);
            Assert.That(CountRows(entry!), Is.EqualTo(pair.Value), pair.Key);
        }
    }

    [Test]
    public void Current_solution_has_AA01_but_not_AE01_contract()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        var aa = Path.Combine(root, "config", "contracts", "registros", "AA01", "v1", "registro.schema.json");
        var ae = Path.Combine(root, "config", "contracts", "registros", "AE01", "v1", "registro.schema.json");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(aa), Is.True, "AA01 deve permanecer contratado na Solution atual.");
            Assert.That(File.Exists(ae), Is.False, "Caracterização v4.05: AE01 existe na origem real, mas ainda não possui contrato factual.");
        });
    }

    [Test]
    public void AA01_source_does_not_supply_required_concession_status_without_inference()
    {
        var schemaPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "contracts", "registros", "AA01", "v1", "registro.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath, Encoding.UTF8));
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.That(required, Does.Contain("situacaoVigencia"));

        using var zip = ZipFile.OpenRead(_zipPath);
        var headers = ReadHeader(zip.GetEntry("dados_base_central_aa.csv")!);
        Assert.Multiple(() =>
        {
            Assert.That(headers, Does.Contain("con_data_ini_vig_beneficio"));
            Assert.That(headers, Does.Contain("con_fim_vig_beneficio"));
            Assert.That(headers, Does.Not.Contain("situacaoVigencia"));
            Assert.That(headers, Does.Not.Contain("situacao_vigencia"));
        });
    }

    [Test]
    public void Payment_files_are_characterized_as_a_distinct_unmodeled_concept_in_phase1_contract()
    {
        var schemaPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "contracts", "registros", "AA01", "v1", "registro.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath, Encoding.UTF8));
        var properties = schema.RootElement.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        using var zip = ZipFile.OpenRead(_zipPath);
        var headers = ReadHeader(zip.GetEntry("beneficios_cencedidos_aa.csv")!);
        Assert.Multiple(() =>
        {
            Assert.That(headers, Does.Contain("bene_dt_pagamento"));
            Assert.That(headers, Does.Contain("bene_ano_mes"));
            Assert.That(headers, Does.Contain("bene_numero_beneficio"));
            Assert.That(properties, Does.Not.Contain("dataPagamento"));
            Assert.That(properties, Does.Not.Contain("competenciaPagamento"));
            Assert.That(properties, Does.Not.Contain("numeroPagamento"));
        });
    }

    [Test]
    public void Required_person_core_exposes_real_source_incompleteness_instead_of_being_filled_artificially()
    {
        using var zip = ZipFile.OpenRead(_zipPath);
        var aa = AnalyzePersonCore(zip, "dados_base_central_aa.csv", "con_cbp_aa.csv");
        var ae = AnalyzePersonCore(zip, "dados_base_central_ce.csv", "dados_cbp_ce.csv");

        Assert.Multiple(() =>
        {
            Assert.That(aa.UniqueBeneficiaries, Is.EqualTo(45_774));
            Assert.That(aa.EligibleWithoutInventingData, Is.EqualTo(45_639));
            Assert.That(aa.MissingSourcePerson, Is.EqualTo(25));
            Assert.That(aa.MissingMotherName, Is.EqualTo(109));
            Assert.That(aa.MissingOrInvalidBirthDate, Is.EqualTo(3));

            Assert.That(ae.UniqueBeneficiaries, Is.EqualTo(5_492));
            Assert.That(ae.EligibleWithoutInventingData, Is.EqualTo(2_673));
            Assert.That(ae.MissingSourcePerson, Is.EqualTo(19));
            Assert.That(ae.MissingMotherName, Is.EqualTo(2_800));
        });
    }

    private static long CountRows(ZipArchiveEntry entry)
    {
        using var parser = CreateParser(entry);
        _ = parser.ReadFields();
        long count = 0;
        while (!parser.EndOfData)
        {
            _ = parser.ReadFields();
            count++;
        }
        return count;
    }

    private static string[] ReadHeader(ZipArchiveEntry entry)
    {
        using var parser = CreateParser(entry);
        return parser.ReadFields() ?? [];
    }

    private static TextFieldParser CreateParser(ZipArchiveEntry entry)
    {
        var parser = new TextFieldParser(entry.Open(), Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        return parser;
    }

    private static PersonCoreSummary AnalyzePersonCore(ZipArchive zip, string baseFile, string cbpFile)
    {
        var source = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        var cbp = zip.GetEntry(cbpFile)!;
        using (var parser = CreateParser(cbp))
        {
            var header = parser.ReadFields()!;
            var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields() ?? [];
                var cpf = Digits(Value(row, index, "cbp_pes_cpf"));
                if (!source.TryGetValue(cpf, out var list)) source[cpf] = list = [];
                list.Add(row);
            }
        }

        var beneficiaries = new HashSet<string>(StringComparer.Ordinal);
        var baseEntry = zip.GetEntry(baseFile)!;
        using (var parser = CreateParser(baseEntry))
        {
            var header = parser.ReadFields()!;
            var index = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
            while (!parser.EndOfData)
            {
                var row = parser.ReadFields() ?? [];
                beneficiaries.Add(Digits(Value(row, index, "con_cpf_beneficiario")));
            }
        }

        var cbpHeader = ReadHeader(zip.GetEntry(cbpFile)!);
        var cbpIndex = cbpHeader.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        var eligible = 0; var missingSource = 0; var missingMother = 0; var badBirth = 0;
        foreach (var cpf in beneficiaries)
        {
            if (!source.TryGetValue(cpf, out var rows)) { missingSource++; continue; }
            var anyValid = false; var localMissingMother = false; var localBadBirth = false;
            foreach (var row in rows)
            {
                var name = Value(row, cbpIndex, "cbp_pes_nome");
                var birth = Value(row, cbpIndex, "cbp_pes_nascimento");
                var mother = Value(row, cbpIndex, "cbp_pes_nom_mae");
                localMissingMother |= string.IsNullOrWhiteSpace(mother);
                localBadBirth |= !DateOnly.TryParse((birth ?? string.Empty).Trim(), out _);
                if (cpf.Length == 11 && !string.IsNullOrWhiteSpace(name) && DateOnly.TryParse((birth ?? string.Empty).Trim(), out _) && !string.IsNullOrWhiteSpace(mother))
                { anyValid = true; break; }
            }
            if (anyValid) eligible++;
            else
            {
                if (localMissingMother) missingMother++;
                if (localBadBirth) badBirth++;
            }
        }
        return new PersonCoreSummary(beneficiaries.Count, eligible, missingSource, missingMother, badBirth);
    }

    private static string? Value(string[] row, IReadOnlyDictionary<string, int> index, string field) =>
        index.TryGetValue(field, out var i) && i < row.Length ? row[i] : null;

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private sealed record PersonCoreSummary(int UniqueBeneficiaries, int EligibleWithoutInventingData, int MissingSourcePerson, int MissingMotherName, int MissingOrInvalidBirthDate);
}
