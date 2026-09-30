using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

[TestFixture]
public sealed class LinkageModelConfigurationBundleValidatorTests
{
    [Test]
    public void RepositoryBundle_IsReferentiallyConsistentAndFingerprintable()
    {
        var directory = FindBundleDirectory();

        var bundle = LinkageModelConfigurationBundleValidator.LoadAndValidate(directory);

        Assert.Multiple(() =>
        {
            Assert.That(bundle.BundleVersion, Is.EqualTo("LINKAGE_MODEL_CONFIG_BUNDLE_V1"));
            Assert.That(bundle.BaseCatalogVersion, Is.EqualTo("LINKAGE_BASE_CATALOG_V1"));
            Assert.That(bundle.BlockingCatalogVersion, Is.EqualTo("LINKAGE_BLOCKING_CATALOG_V1"));
            Assert.That(bundle.FsCatalogVersion, Is.EqualTo("LINKAGE_FS_CATALOG_V2_IBGE_BOOTSTRAP"));
            Assert.That(bundle.FingerprintSha256, Has.Length.EqualTo(64));
        });
    }

    private static string FindBundleDirectory()
    {
        var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "config", "linkage");
            if (File.Exists(Path.Combine(candidate, "model-config-bundle.json")))
                return candidate;
            candidate = Path.Combine(current.FullName, "Solution", "config", "linkage");
            if (File.Exists(Path.Combine(candidate, "model-config-bundle.json")))
                return candidate;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Solution/config/linkage não encontrado.");
    }
}
