namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Three equality anchors actually executed by five SQL birth-year passes:
/// person phonetic, mother phonetic, birth year. An eligible M pair with
/// >=2 agreeing anchors must be in the full eleven-pass SQL union.
/// This is a bounded-family identity check, NOT a global blocking ceiling.
/// TEST is forbidden; unknown phonetic values never count as equality.
/// </summary>
public static class V9SqlTwoSignalResidualAudit
{
    public const string Version = "V9_SQL_PHYSICAL_THREE_ANCHORS_RESIDUAL_IDENTIFIABILITY_V1";
    public const string Status = "DIAGNOSTIC_ONLY_NO_TEST_NO_PRODUCTION_OR_GLOBAL_BOUND";

    public sealed record Signal(
        int Id, string Partition, string? PersonPhonetic,
        string? MotherPhonetic, int BirthYear);

    public sealed record Pair(int A, int B, string Partition);
    public sealed record Cell(
        string Partition, bool PersonPhoneticAgrees, bool MotherPhoneticAgrees,
        bool BirthYearAgrees, int AgreementCount,
        long EligibleM, long RecoveredElevenM, long ResidualM);
    public sealed record Split(
        string Partition, long EligibleM, long RecoveredElevenM, long ResidualM,
        long TwoOrMoreAnchorsEligibleM, long TwoOrMoreAnchorsResidualM,
        long ResidualWithNoMatchingAnchorM, long ResidualWithOnlyOneMatchingAnchorM,
        long ResidualWithMissingMotherPhoneticM, long ResidualWithDifferentBirthYearM,
        bool PhysicalSqlTwoAnchorJoinComplete,
        bool MayPromoteBlockingPolicy);
    public sealed record Report(
        string Version, string Status, string Scope,
        IReadOnlyList<Split> Splits, IReadOnlyList<Cell> Cells,
        bool IsGlobalBlockingCeiling, bool MayPromoteBlockingPolicy);

    public static Report Analyze(
        IReadOnlyList<Signal> eligibleObservations,
        IReadOnlyList<Pair> independentlyEligibleM,
        IReadOnlyCollection<(int A, int B)> retrievedElevenM)
    {
        ArgumentNullException.ThrowIfNull(eligibleObservations);
        ArgumentNullException.ThrowIfNull(independentlyEligibleM);
        ArgumentNullException.ThrowIfNull(retrievedElevenM);

        var signals = new Dictionary<int,Signal>();
        foreach (var row in eligibleObservations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || row.BirthYear is < 1 or > 9999
                || !signals.TryAdd(row.Id,row))
                throw new InvalidDataException(
                    "Sinais SQL inválidos, ausentes ou contaminados por TEST.");
        }
        var seen = new HashSet<(int A,int B)>();
        var actual = new HashSet<(int A,int B)>();
        foreach (var recovered in retrievedElevenM)
            if (recovered.A < 0 || recovered.A >= recovered.B || !actual.Add(recovered))
                throw new InvalidDataException("SQL onze passes contém M duplicado/invertido.");

        var counts = new Dictionary<(string Part,bool Person,bool Mother,bool Birth),
            (long Eligible,long Recovered,long MotherMissing)>();
        foreach (var pair in independentlyEligibleM)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.A >= pair.B
                || pair.Partition is not ("TRAIN" or "VALIDATION")
                || !seen.Add((pair.A,pair.B))
                || !signals.TryGetValue(pair.A,out var a)
                || !signals.TryGetValue(pair.B,out var b)
                || a.Partition != pair.Partition || b.Partition != pair.Partition)
                throw new InvalidDataException("Gabarito M fora do SQL elegível/duplicado.");

            // Null/empty is an unknown value, never an equality. SQL NULL
            // semantics for the physical predicates are identical here.
            var person = !string.IsNullOrEmpty(a.PersonPhonetic)
                && string.Equals(a.PersonPhonetic,b.PersonPhonetic,StringComparison.Ordinal);
            var mother = !string.IsNullOrEmpty(a.MotherPhonetic)
                && string.Equals(a.MotherPhonetic,b.MotherPhonetic,StringComparison.Ordinal);
            var birth = a.BirthYear == b.BirthYear;
            var covered = actual.Contains((pair.A,pair.B));
            var agreementCount = (person ? 1 : 0)+(mother ? 1 : 0)+(birth ? 1 : 0);
            if (agreementCount >= 2 && !covered)
                throw new InvalidDataException(
                    "SQL físico deixou escapar M com >=2 âncoras reais iguais.");
            var key=(pair.Partition,person,mother,birth);
            var previous=counts.GetValueOrDefault(key);
            counts[key]=(checked(previous.Eligible+1),
                checked(previous.Recovered+(covered?1:0)),
                checked(previous.MotherMissing+
                    (!covered && (string.IsNullOrEmpty(a.MotherPhonetic)
                        || string.IsNullOrEmpty(b.MotherPhonetic))?1:0)));
        }
        if (actual.Any(x=>!seen.Contains(x)))
            throw new InvalidDataException("SQL classificou M fora do gabarito elegível.");

        var cells = new List<Cell>(16);
        var summaries = new List<Split>(2);
        foreach (var part in new[] {"TRAIN","VALIDATION"})
        {
            long eligible=0,recovered=0,twoEligible=0,twoMissing=0;
            long zeroMissing=0,oneMissing=0,missingMother=0,differentYear=0;
            foreach (var person in new[] {false,true})
            foreach (var mother in new[] {false,true})
            foreach (var birth in new[] {false,true})
            {
                var c=counts.GetValueOrDefault((part,person,mother,birth));
                var anchors=(person?1:0)+(mother?1:0)+(birth?1:0);
                if (c.Recovered>c.Eligible || c.MotherMissing>c.Eligible-c.Recovered)
                    throw new InvalidDataException("Máscara de âncoras SQL inconsistente.");
                var missing=c.Eligible-c.Recovered;
                cells.Add(new Cell(part,person,mother,birth,anchors,
                    c.Eligible,c.Recovered,missing));
                eligible+=c.Eligible;recovered+=c.Recovered;
                if(anchors>=2){twoEligible+=c.Eligible;twoMissing+=missing;}
                if(anchors==0)zeroMissing+=missing;
                if(anchors==1)oneMissing+=missing;
                missingMother+=c.MotherMissing;
                if(!birth)differentYear+=missing;
            }
            if(eligible<=0 || recovered<=0 || twoMissing!=0
                || zeroMissing+oneMissing!=eligible-recovered)
                throw new InvalidDataException(
                    "Denominador ou cobertura de duas âncoras SQL inconsistente.");
            summaries.Add(new Split(part,eligible,recovered,eligible-recovered,
                twoEligible,twoMissing,zeroMissing,oneMissing,missingMother,
                differentYear,true,false));
        }
        if (cells.Count!=16 || summaries.Count!=2
            || summaries.Sum(x=>x.EligibleM)!=seen.Count
            || summaries.Sum(x=>x.RecoveredElevenM)!=actual.Count)
            throw new InvalidDataException("Somas de M não reconciliadas entre máscaras e SQL.");

        return new Report(Version,Status,"TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            Array.AsReadOnly(summaries.ToArray()),
            Array.AsReadOnly(cells.ToArray()),false,false);
    }
}
