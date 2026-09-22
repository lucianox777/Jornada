using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.SyntheticCorpus;

public static class SyntheticCorpusInputIdentity
{
    public const string GeneratorVersion = "JORNADA_SYNTH_CORPUS_CSHARP_V1";

    public static string ComputeFingerprint(
        ulong seed,
        IEnumerable<IbgeProjectionFile> files,
        SyntheticBrazilianNameErrorConfig? brazilianNameErrors = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var builder = new StringBuilder();
        builder.Append("generator=").Append(GeneratorVersion).Append('\n');
        builder.Append("rng=").Append(Xoshiro256StarStar.AlgorithmVersion).Append('\n');
        builder.Append("seed=").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (brazilianNameErrors is not null)
        {
            builder.Append("brazilian_name_errors_version=")
                .Append(brazilianNameErrors.Version).Append('\n');
            builder.Append("brazilian_name_errors_config_sha256=")
                .Append(brazilianNameErrors.ConfigSha256()).Append('\n');
        }

        foreach (var file in files.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            builder.Append(file.Path).Append('|')
                .Append(file.Kind).Append('|')
                .Append(file.Required ? "1" : "0").Append('|')
                .Append(file.Sha256.ToUpperInvariant()).Append('|')
                .Append(file.CanonicalContentSha256.ToUpperInvariant()).Append('|')
                .Append(file.RowCount.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }
}
