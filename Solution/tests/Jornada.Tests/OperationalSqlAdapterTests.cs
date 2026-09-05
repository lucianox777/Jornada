using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests;

[TestFixture]
public sealed class OperationalSqlAdapterTests
{
    [Test]
    public void Rejects_empty_connection_string()
    {
        Assert.That(
            () => new OperationalSqlAdapter("  "),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Normal_connection_preserves_configured_sql_properties()
    {
        var adapter = new OperationalSqlAdapter(
            "Server=example.invalid;Database=JornadaDev;Integrated Security=True;Encrypt=True;Pooling=True;Enlist=True;Application Name=Jornada.Tests");

        using var connection = adapter.CreateConnection();
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);

        Assert.Multiple(() =>
        {
            Assert.That(builder.InitialCatalog, Is.EqualTo("JornadaDev"));
            Assert.That(builder.Pooling, Is.True);
            Assert.That(builder.Enlist, Is.True);
            Assert.That(builder.ApplicationName, Is.EqualTo("Jornada.Tests"));
        });
    }

    [Test]
    public void Dedicated_session_disables_pooling_and_automatic_enlistment()
    {
        var adapter = new OperationalSqlAdapter(
            "Server=example.invalid;Database=JornadaDev;Integrated Security=True;Encrypt=True;Pooling=True;Enlist=True");

        using var connection = adapter.CreateDedicatedSessionConnection();
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);

        Assert.Multiple(() =>
        {
            Assert.That(builder.Pooling, Is.False);
            Assert.That(builder.Enlist, Is.False);
            Assert.That(builder.InitialCatalog, Is.EqualTo("JornadaDev"));
        });
    }
}
