using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public sealed record BirthBlockingSqlQuery(string Predicate, string PassMaskExpression);

/// <summary>
/// Builds parameterized PostgreSQL predicates from the same plan used by the scorer
/// and the calibration-universe diagnostic. Column identifiers are fixed by the caller.
/// </summary>
public static partial class PostgreSqlBirthBlockingQuery
{
    public static BirthBlockingSqlQuery Build(DbCommand command, BirthBlockingPlan plan,
        string alias = "g", string prefix = "bb")
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(plan);
        if (!Identifier().IsMatch(alias) || !Identifier().IsMatch(prefix))
            throw new ArgumentException("Alias ou prefixo SQL inválido.");
        var date = alias + ".data_nascimento";
        var expressions = new List<(BirthBlockingPass Pass, string Predicate)>();
        var exact = AddDate(command, prefix + "_exact", plan.SourceDate);
        expressions.Add((BirthBlockingPass.ExactDate, date + "=" + exact));
        if (plan.UseComponents)
        {
            var initialPredicate = string.Empty;
            if (plan.HasInitials)
            {
                var initialPredicates = new List<string>();
                if (plan.NameInitial.Length > 0)
                    initialPredicates.Add("UPPER(LEFT(LTRIM(" + alias + ".nome_completo),1))=" +
                        Add(command, prefix + "_name_initial", DbType.String, plan.NameInitial));
                if (plan.MotherInitial.Length > 0)
                    initialPredicates.Add("UPPER(LEFT(LTRIM(" + alias + ".nome_mae),1))=" +
                        Add(command, prefix + "_mother_initial", DbType.String, plan.MotherInitial));
                initialPredicate = "(" + string.Join(" OR ", initialPredicates) + ")";
                var monthStart = new DateOnly(plan.SourceDate.Year, plan.SourceDate.Month, 1);
                var monthEnd = monthStart == new DateOnly(9999, 12, 1) ? (DateOnly?)null : monthStart.AddMonths(1);
                var range = date + ">=" + AddDate(command, prefix + "_month_start", monthStart);
                range += monthEnd is { } end
                    ? " AND " + date + "<" + AddDate(command, prefix + "_month_end", end)
                    : " AND " + date + "<=" + AddDate(command, prefix + "_month_last", DateOnly.MaxValue);
                expressions.Add((BirthBlockingPass.MonthYearWithInitial, "(" + range + " AND " + initialPredicate + ")"));
                var dayYear = DatePredicate(command, date, prefix + "_day_year", plan.DayYearDates);
                if (dayYear is not null)
                    expressions.Add((BirthBlockingPass.DayYearWithInitial, "(" + dayYear + " AND " + initialPredicate + ")"));
            }
            if (plan.TransposedDate is { } swapped)
                expressions.Add((BirthBlockingPass.TransposedDayMonth,
                    date + "=" + AddDate(command, prefix + "_swapped", swapped)));
            var neighbors = DatePredicate(command, date, prefix + "_neighbor", plan.NeighborYearDates);
            if (neighbors is not null)
                expressions.Add((BirthBlockingPass.NeighborYear, neighbors));
        }
        var predicate = "(" + string.Join(" OR ", expressions.Select(x => x.Predicate)) + ")";
        var mask = "(" + string.Join(" + ", expressions.Select(x =>
            "CASE WHEN " + x.Predicate + " THEN " + (int)x.Pass + " ELSE 0 END")) + ")";
        return new BirthBlockingSqlQuery(predicate, mask);
    }

    private static string? DatePredicate(DbCommand command, string column, string prefix, IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0) return null;
        var names = dates.Select((date, index) => AddDate(command, prefix + "_" + index, date));
        return column + " IN (" + string.Join(",", names) + ")";
    }

    private static string AddDate(DbCommand command, string name, DateOnly date) =>
        Add(command, name, DbType.Date, date.ToDateTime(TimeOnly.MinValue));

    private static string Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return "@" + name;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
