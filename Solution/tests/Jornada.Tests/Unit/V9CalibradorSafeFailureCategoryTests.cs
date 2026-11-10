using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9CalibradorSafeFailureCategoryTests
{
    private const string Sensitive = "SENHA_CANARY_SQL_PASSWORD_CPF_11122233344";

    [Test]
    public void Identifies_internal_resource_codes_only()
    {
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_RUNTIME_RAM_BELOW_ONE_WORKER")),
            Is.EqualTo("RESOURCE_RUNTIME_RAM"));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE")),
            Is.EqualTo("RESOURCE_RUNTIME_DISK"));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_RESOURCE_SQL_CONNECTION_CAPACITY")),
            Is.EqualTo("RESOURCE_SQL_POOL"));
    }

    [Test]
    public void Does_not_echo_untrusted_exception_messages_or_data()
    {
        var malicious = new InvalidDataException("V9_RESOURCE_RUNTIME_DISK_BELOW_RESERVE " + Sensitive);
        malicious.Data["JornadaV9SafeFailureCategory"] = Sensitive;
        Assert.That(V9CalibradorSafeFailureCategory.Classify(malicious),
            Is.EqualTo(V9CalibradorSafeFailureCategory.Unclassified));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(new IOException(Sensitive)),
            Is.EqualTo(V9CalibradorSafeFailureCategory.Unclassified));
    }

    [Test]
    public void Allows_only_compiled_failure_scopes()
    {
        var error = new InvalidDataException(Sensitive);
        V9CalibradorSafeFailureCategory.Mark(error,
            V9CalibradorSafeFailureCategory.PhysicalSqlFrame);
        var category = V9CalibradorSafeFailureCategory.Classify(error);
        Assert.That(category, Is.EqualTo("PHYSICAL_SQL_FRAME"));
        Assert.That(category, Does.Not.Contain(Sensitive));
        Assert.Throws<ArgumentException>((Action)(() =>
            V9CalibradorSafeFailureCategory.Mark(error, Sensitive)));
        Assert.That(V9CalibradorSafeFailureCategory.Classify(error),
            Is.EqualTo(category));
    }

    [Test]
    public void Matches_exact_structural_frame_error_as_safe_category()
    {
        Assert.That(V9CalibradorSafeFailureCategory.Classify(
            new InvalidDataException("V9_SQL_COST_ORDER_DUPLICATE_STABLE_KEY")),
            Is.EqualTo("PHYSICAL_FRAME_ORDER"));
    }
}
