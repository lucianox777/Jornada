using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;

namespace Jornada.Tests.Unit;

[TestFixture,Category("Unit")]
public sealed class PostgreSqlCalibrationPolicyTests
{
    [Test]
    public void ProductionDefaultsRequireIndependentEvidence()
    {
        var options=new PostgreSqlCalibrationOptions(250_000,1_000_000,5_000,0.5m,0.95m,0.03m,900);
        Assert.DoesNotThrow(options.Validate);
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {MinimumIndependentMatchedPairs=99}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {MinimumIndependentMatchedPairs=250_001}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {PoolSize=249_999}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {PoolSize=5_000_001}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {Threshold=1m}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {ConflictMargin=0m}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {SmoothingAlpha=0m}).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(options with {CommandTimeoutSeconds=0}).Validate());
    }

    [Test]
    public void ParameterFingerprintIsDeterministicAndUsesPersistedDecimalScale()
    {
        var a=new Dictionary<string,decimal>{{"T_LINKAGE",0.95m},{"M_NOME_EXACT",0.1234567890124m}};
        var b=new Dictionary<string,decimal>{{"M_NOME_EXACT",0.123456789012m},{"T_LINKAGE",0.950000000000m}};
        // A calibração arredonda antes de persistir: valores já arredondados são estáveis.
        var rounded=a.ToDictionary(p=>p.Key,p=>decimal.Round(p.Value,12,MidpointRounding.AwayFromZero));
        Assert.That(PostgreSqlLinkageCalibrator.ParameterHash(rounded),Is.EqualTo(PostgreSqlLinkageCalibrator.ParameterHash(b)));
        Assert.That(PostgreSqlLinkageCalibrator.ParameterHash(b),Has.Length.EqualTo(64));
        Assert.That(PostgreSqlLinkageCalibrator.ParameterHash(new Dictionary<string,decimal>{{"T_LINKAGE",0.96m}}),
            Is.Not.EqualTo(PostgreSqlLinkageCalibrator.ParameterHash(b)));
    }

    [Test]
    public void CalibrationRejectsSqlServerProvider()
    {
        var adapter=new OperationalSqlAdapter("Server=localhost;Database=Jornada;Integrated Security=true");
        Assert.Throws<ArgumentException>(()=>new PostgreSqlLinkageCalibrator(adapter));
    }
}
