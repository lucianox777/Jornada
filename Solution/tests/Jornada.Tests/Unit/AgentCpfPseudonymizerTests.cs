using Jornada.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class AgentCpfPseudonymizerTests
{
    [Test]
    public void Same_cpf_produces_same_32_byte_hmac_and_different_cpf_changes_it()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["AgentAudit:HmacKeyBase64"] = Convert.ToBase64String(Enumerable.Range(1,32).Select(i => (byte)i).ToArray()),
            ["AgentAudit:KeyVersion"] = "7"
        }).Build();
        var sut = new AgentCpfPseudonymizer(config, new FakeEnvironment(), Path.GetTempPath());
        var a = sut.ComputeHash("52998224725");
        var b = sut.ComputeHash("52998224725");
        var c = sut.ComputeHash("11144477735");
        Assert.Multiple(() =>
        {
            Assert.That(a, Has.Length.EqualTo(32));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
            Assert.That(sut.KeyVersion, Is.EqualTo(7));
        });
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
