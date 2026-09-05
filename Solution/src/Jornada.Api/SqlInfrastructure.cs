using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal static class SqlReaderExtensions
{
    public static string? NullableString(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static DateTimeOffset? NullableDateTimeOffset(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTimeOffset(ordinal);

    public static DateTime? NullableDateTime(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    public static decimal? NullableDecimal(this SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
