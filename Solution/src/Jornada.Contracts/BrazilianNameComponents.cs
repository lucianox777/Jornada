using System.Collections.ObjectModel;

namespace Jornada.Contracts;

/// <summary>
/// Classificação conservadora e não destrutiva de componentes nominais brasileiros.
/// Inspiração: catálogo de partículas, agnomes e títulos do nomesbr/Ipea 0.1.1,
/// commit 3b4a9eb10d994d70af3cc95d6c342e6038d2b573 (MIT).
/// Isto NÃO porta a simplificação destrutiva upstream: o nome integral permanece
/// disponível e nenhum agnome é apagado da identidade da Pessoa.
/// </summary>
public static class BrazilianNameComponents
{
    public const string MethodVersion = "PERSON_NAME_BRAZILIAN_COMPONENTS_V1";

    private static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
    {
        "DA", "DAS", "DE", "DO", "DOS", "DI", "DU", "DEL", "D"
    };

    // Termos exatos, nunca substrings: "NETO" não casa com "NETOLOGIA".
    // A abreviação é canonizada na projeção auxiliar, NÃO no nome integral.
    private static readonly IReadOnlyDictionary<string, string> TerminalAgnomes =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FILHO"] = "FILHO", ["FILHA"] = "FILHA", ["FL"] = "FILHO",
            ["JUNIOR"] = "JUNIOR", ["JR"] = "JUNIOR",
            ["NETO"] = "NETO", ["NETA"] = "NETA",
            ["BISNETO"] = "BISNETO", ["BISNETA"] = "BISNETA",
            ["SOBRINHO"] = "SOBRINHO", ["SOBRINHA"] = "SOBRINHA",
            ["SEGUNDO"] = "SEGUNDO", ["SEGUNDA"] = "SEGUNDA",
            ["TERCEIRO"] = "TERCEIRO", ["TERCEIRA"] = "TERCEIRA"
        });

    private static readonly HashSet<string> PrefixTitles = new(StringComparer.Ordinal)
    {
        "DR", "DRA", "DOUTOR", "DOUTORA", "SGTO", "SARGENTO",
        "TENENTE", "MAJOR", "CAPITAO", "CORONEL", "GENERAL",
        "GOVERNADOR", "GOVERNADORA"
    };

    public static BrazilianNameComponentProjection? Project(string? value)
    {
        var full = IdentityComparison.NormalizeText(value);
        if (full is null)
            return null;

        var tokens = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        // "DR."/"JR." são reconhecidos apenas como tokens inteiros.
        var lexical = tokens.Select(static token => token.Trim('.', ',')).ToArray();
        var title = tokens.Length > 2 && PrefixTitles.Contains(lexical[0])
            ? lexical[0] : null;
        var start = title is null ? 0 : 1;
        var end = tokens.Length;

        // "JOAO FILHO" é ambíguo: não inferir agnome sem nome + sobrenome
        // anteriores. Para "JOAO SILVA FILHO", preservamos o sinal terminal.
        string? agnome = null;
        var precedingContentTokens = 0;
        for (var i = start; i < end - 1; i++)
        {
            if (Particles.Contains(lexical[i]))
                continue;
            if (i > start && lexical[i - 1] == "DE" &&
                lexical[i] is ("LA" or "LAS" or "LOS"))
                continue;
            precedingContentTokens++;
        }

        if (precedingContentTokens >= 2 &&
            TerminalAgnomes.TryGetValue(lexical[^1], out var canonicalAgnome))
        {
            agnome = canonicalAgnome;
            end--;
        }

        // Partículas só são ignoradas para derivar o último sobrenome de
        // conteúdo; DE LA/DE LOS/DE LAS são pares posicionais.
        var content = new List<string>();
        for (var i = start; i < end; i++)
        {
            if (Particles.Contains(lexical[i]))
                continue;
            if (i > start && lexical[i - 1] == "DE" &&
                lexical[i] is ("LA" or "LAS" or "LOS"))
                continue;
            content.Add(lexical[i]);
        }

        // Não chamar o único prenome de "sobrenome".
        var lastSurname = content.Count >= 2 ? content[^1] : null;
        var repeatedParticle = false;
        for (var i = start + 1; i < end; i++)
            repeatedParticle |= Particles.Contains(lexical[i]) &&
                                lexical[i] == lexical[i - 1];

        return new BrazilianNameComponentProjection(
            full, agnome, lastSurname, title, repeatedParticle);
    }
}

/// <param name="NormalizedFull">Nome integral normalizado, SEM remover agnome ou título.</param>
/// <param name="Agnome">Classificação auxiliar de sufixo: pode ser ambígua, nunca identidade determinística.</param>
/// <param name="LastContentSurname">Sobrenome auxiliar sem partículas e agnome terminal; inadequado para decidir vínculo isoladamente.</param>
/// <param name="TitlePrefix">Título/patente prefixal, apenas diagnóstico.</param>
/// <param name="RepeatedParticle">Diagnóstico de repetição exata de partícula, sem corrigir o valor original.</param>
public sealed record BrazilianNameComponentProjection(
    string NormalizedFull,
    string? Agnome,
    string? LastContentSurname,
    string? TitlePrefix,
    bool RepeatedParticle);
