namespace Jornada.Linkage.Evaluation;

public static partial class V9SqlTwoPrefix3Audit
{
    public const string Version="V9_SQL_TWO_MORE_PREFIX3_FAMILIES_TRAIN_VALIDATION_V1";
    public const string MotherDate="MOTHER_PREFIX3_PLUS_EXACT_BIRTH";
    public const string PersonMother="PERSON_PREFIX3_PLUS_MOTHER_PREFIX3";
    public sealed record Observation(int Id,string Partition,string Gestor,bool Eligible,
        string? Person3,string? Mother3,DateOnly? Birth);
    public sealed record Pass(string Name,IReadOnlyList<V9SqlNamePrefix3BirthAudit.Pair> Edges);
    public sealed record PassSplit(string Name,string Partition,long M,long U,
        long NewMVsTwelve,long NewUVsTwelve,long TruthMWithPredicate,long TruthMNotRetrieved);
    public sealed record Split(string Partition,long EligibleM,long TwelveM,long TwelveU,
        long AddedM,long AddedU,long FourteenM,long FourteenU,decimal Recall,
        long MoreMNeededFor95Percent,int EligibleObservations,int ZeroCandidateObservations,
        int P95Degree,int P99Degree,int MaxDegree,
        int P95UDegree,int P99UDegree,int MaxUDegree);
    public sealed record Report(string Version,string Status,bool MayPromotePolicy,
        IReadOnlyList<PassSplit> Passes,IReadOnlyList<Split> Splits)
    {
        public V9TwoFieldSqlCoverageAudit.Report? TwoFieldSqlCoverage { get; init; }
        public V9NameRequiredSqlCellAudit.Report? NameRequiredSqlCells { get; init; }
    }
}
