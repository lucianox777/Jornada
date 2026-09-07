using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Contracts;

[Flags]
public enum BirthBlockingPass
{
    None = 0,
    ExactDate = 1,
    MonthYearWithInitial = 2,
    DayYearWithInitial = 4,
    TransposedDayMonth = 8,
    NeighborYear = 16
}

/// <summary>
/// Frozen, provider-independent description of the published birth blocking rules.
/// This describes candidate eligibility, not identity evidence or a probability.
/// </summary>
public sealed class BirthBlockingPlan
{
    public const string Version = "BIRTH_BLOCKING_V2_20260907";
    private readonly HashSet<DateOnly> dayYearDates;
    private readonly HashSet<DateOnly> neighborYearDates;

    private BirthBlockingPlan(DateOnly sourceDate, bool useComponents, int yearTolerance,
        string nameInitial, string motherInitial, HashSet<DateOnly> dayYearDates, HashSet<DateOnly> neighborYearDates,
        DateOnly? transposedDate)
    {
        SourceDate = sourceDate;
        UseComponents = useComponents;
        YearTolerance = yearTolerance;
        NameInitial = nameInitial;
        MotherInitial = motherInitial;
        this.dayYearDates = dayYearDates;
        this.neighborYearDates = neighborYearDates;
        TransposedDate = transposedDate;
    }

    public DateOnly SourceDate { get; }
    public bool UseComponents { get; }
    public int YearTolerance { get; }
    public string NameInitial { get; }
    public string MotherInitial { get; }
    public bool HasInitials => NameInitial.Length > 0 || MotherInitial.Length > 0;
    public IReadOnlyList<DateOnly> DayYearDates => dayYearDates.OrderBy(x => x).ToArray();
    public IReadOnlyList<DateOnly> NeighborYearDates => neighborYearDates.OrderBy(x => x).ToArray();
    public DateOnly? TransposedDate { get; }

    public static BirthBlockingPlan Create(DateOnly date, string? name, string? mother,
        bool useComponents, int yearTolerance)
    {
        if (yearTolerance is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(yearTolerance));
        var nameInitial = Initial(name);
        var motherInitial = Initial(mother);
        var dayYearDates = new HashSet<DateOnly>();
        var neighborYearDates = new HashSet<DateOnly>();
        if (useComponents)
        {
            if (nameInitial.Length > 0 || motherInitial.Length > 0)
                for (var month = 1; month <= 12; month++)
                    if (TryDate(date.Year, month, date.Day) is { } value)
                        dayYearDates.Add(value);
            for (var offset = -yearTolerance; offset <= yearTolerance; offset++)
                if (offset != 0 && TryDate(date.Year + offset, date.Month, date.Day) is { } value)
                    neighborYearDates.Add(value);
        }
        var swapped = useComponents ? TryDate(date.Year, date.Day, date.Month) : null;
        if (swapped == date) swapped = null;
        return new BirthBlockingPlan(date, useComponents, yearTolerance, nameInitial, motherInitial,
            dayYearDates, neighborYearDates, swapped);
    }

    public BirthBlockingPass Match(DateOnly date, string? name, string? mother)
    {
        var passes = date == SourceDate ? BirthBlockingPass.ExactDate : BirthBlockingPass.None;
        if (!UseComponents) return passes;
        if (HasInitials && MatchesInitial(name, mother))
        {
            if (date.Year == SourceDate.Year && date.Month == SourceDate.Month)
                passes |= BirthBlockingPass.MonthYearWithInitial;
            if (dayYearDates.Contains(date)) passes |= BirthBlockingPass.DayYearWithInitial;
        }
        if (TransposedDate == date) passes |= BirthBlockingPass.TransposedDayMonth;
        if (neighborYearDates.Contains(date)) passes |= BirthBlockingPass.NeighborYear;
        return passes;
    }

    public static BirthBlockingPass PrimaryPass(BirthBlockingPass passes)
    {
        foreach (var pass in OrderedPasses)
            if ((passes & pass) != 0) return pass;
        return BirthBlockingPass.None;
    }

    public static IReadOnlyList<BirthBlockingPass> OrderedPasses { get; } = Array.AsReadOnly(new[]
    {
        BirthBlockingPass.ExactDate, BirthBlockingPass.MonthYearWithInitial,
        BirthBlockingPass.DayYearWithInitial, BirthBlockingPass.TransposedDayMonth,
        BirthBlockingPass.NeighborYear
    });

    public string ConfigurationFingerprint()
    {
        var canonical = string.Join("|", Version, SourceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UseComponents ? "1" : "0", YearTolerance.ToString(CultureInfo.InvariantCulture),
            NameInitial, MotherInitial) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private bool MatchesInitial(string? name, string? mother) =>
        (NameInitial.Length > 0 && NameInitial == Initial(name)) ||
        (MotherInitial.Length > 0 && MotherInitial == Initial(mother));

    private static string Initial(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..1].ToUpperInvariant();

    private static DateOnly? TryDate(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 ||
            day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }
}
