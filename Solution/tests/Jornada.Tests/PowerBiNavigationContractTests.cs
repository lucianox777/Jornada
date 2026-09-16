using System.Text.Json;

namespace Jornada.Tests;

[TestFixture]
public sealed class PowerBiNavigationContractTests
{
    private const string Home = "ed655fe5183bb5da3b53";

    private static readonly string[] PageOrder =
    [
        Home,
        "55a19660cec7f86a0c13",
        "d1a2b3c4d5e607182931",
        "cf894572e9aa44bc44cc",
        "4c53cd53a1018bd589c5",
        "f3800000000000000021",
        "f3800000000000000022",
        "f3300000000000000001",
        "c46231d4b456f16413ca",
        "8c1ee6741eed18e3e2ad",
        "e127b3f90a1c4d778901",
        "e227b3f90a1c4d778902",
        "b33b97c3fde2ead7ddf2",
        "f3900000000000000001",
        "f3300000000000000002",
        "8e8d7ed637ac33e98d92",
        "018ba5495999f581272d",
        "a1b2c3d4e5f607182930",
        "732324a998e5afced324",
        "7df6e21bce60a01e9a60",
        "f9f35295e5174cfb6245",
        "f3800000000000000020",
        "6c5141d5538aeabc626c"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> Groups =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["55a19660cec7f86a0c13"] =
            [
                "d1a2b3c4d5e607182931", "cf894572e9aa44bc44cc", "4c53cd53a1018bd589c5",
                "f3800000000000000021", "f3800000000000000022"
            ],
            ["f3300000000000000001"] =
            [
                "c46231d4b456f16413ca", "8c1ee6741eed18e3e2ad",
                "e127b3f90a1c4d778901", "e227b3f90a1c4d778902"
            ],
            ["b33b97c3fde2ead7ddf2"] =
            [
                "f3900000000000000001", "f3300000000000000002", "8e8d7ed637ac33e98d92"
            ],
            ["018ba5495999f581272d"] =
            [
                "a1b2c3d4e5f607182930", "732324a998e5afced324", "7df6e21bce60a01e9a60"
            ],
            ["f9f35295e5174cfb6245"] = [],
            ["f3800000000000000020"] = ["6c5141d5538aeabc626c"]
        };

    [Test]
    [Category("Unit")]
    public void HierarchicalNavigation_IsCompleteAndFailClosed()
    {
        var solutionRoot = FindSolutionRoot();
        var pagesRoot = Path.Combine(solutionRoot, "bi", "Jornada.Report", "definition", "pages");

        using var pagesMetadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(pagesRoot, "pages.json")));
        var actualOrder = pagesMetadata.RootElement.GetProperty("pageOrder")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.That(actualOrder, Is.EqualTo(PageOrder), "A ordem física deve manter cada submenu junto do seu domínio.");
        Assert.That(pagesMetadata.RootElement.GetProperty("activePageName").GetString(), Is.EqualTo(Home));

        var rootPages = new HashSet<string>(Groups.Keys.Append(Home), StringComparer.Ordinal);
        var hiddenPages = new HashSet<string>(PageOrder.Where(page => !rootPages.Contains(page)), StringComparer.Ordinal);
        Assert.That(rootPages.Count, Is.EqualTo(7));
        Assert.That(hiddenPages.Count, Is.EqualTo(16));

        foreach (var pageId in PageOrder)
        {
            var path = Path.Combine(pagesRoot, pageId, "page.json");
            using var page = JsonDocument.Parse(File.ReadAllText(path));
            Assert.That(page.RootElement.GetProperty("name").GetString(), Is.EqualTo(pageId));

            var hasVisibility = page.RootElement.TryGetProperty("visibility", out var visibility);
            if (hiddenPages.Contains(pageId))
            {
                Assert.That(hasVisibility, Is.True, $"Página de detalhe {pageId} deve ficar oculta no modo de visualização.");
                Assert.That(visibility.GetString(), Is.EqualTo("HiddenInViewMode"));
            }
            else
            {
                Assert.That(hasVisibility, Is.False, $"Página-raiz {pageId} deve permanecer visível.");
            }
        }

        var links = PageOrder.ToDictionary(page => page, _ => new List<string>(), StringComparer.Ordinal);
        var navigationButtons = 0;
        foreach (var visualPath in Directory.EnumerateFiles(pagesRoot, "visual.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(visualPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("visual", out var visual) ||
                !visual.TryGetProperty("visualType", out var type) ||
                type.GetString() != "actionButton")
            {
                continue;
            }

            if (!TryGetPageNavigationDestination(visual, out var destination))
            {
                continue;
            }

            var sourcePage = Directory.GetParent(Directory.GetParent(visualPath)!.FullName)!.Parent!.Name;
            Assert.That(links.ContainsKey(sourcePage), Is.True, $"Botão fora de página conhecida: {visualPath}");
            Assert.That(links.ContainsKey(destination), Is.True,
                $"Destino de navegação inexistente em {visualPath}: {destination}");
            links[sourcePage].Add(destination);
            navigationButtons++;
        }

        Assert.That(navigationButtons, Is.EqualTo(44), "O menu hierárquico deve ter exatamente 44 botões nativos.");
        Assert.That(links[Home], Is.EquivalentTo(Groups.Keys), "Visão Geral deve apontar para os seis domínios.");

        foreach (var (rootPage, children) in Groups)
        {
            var expectedFromRoot = children.Prepend(Home).ToArray();
            Assert.That(links[rootPage], Is.EquivalentTo(expectedFromRoot),
                $"Submenu incompleto para o domínio {rootPage}.");

            foreach (var child in children)
            {
                Assert.That(links[child], Is.EqualTo(new[] { rootPage }),
                    $"Página de detalhe {child} deve retornar somente ao seu domínio.");
            }
        }
    }

    private static bool TryGetPageNavigationDestination(JsonElement visual, out string destination)
    {
        destination = string.Empty;
        if (!visual.TryGetProperty("visualContainerObjects", out var objects) ||
            !objects.TryGetProperty("visualLink", out var links) ||
            links.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (!link.TryGetProperty("properties", out var properties) ||
                !TryGetLiteral(properties, "type", out var type) ||
                !string.Equals(type, "PageNavigation", StringComparison.Ordinal) ||
                !TryGetLiteral(properties, "navigationSection", out destination))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetLiteral(JsonElement properties, string name, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetProperty(name, out var property) ||
            !property.TryGetProperty("expr", out var expr) ||
            !expr.TryGetProperty("Literal", out var literal) ||
            !literal.TryGetProperty("Value", out var raw) ||
            raw.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = raw.GetString()!;
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return true;
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "bi", "Jornada.Report", "definition", "pages")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        Assert.Fail("Não foi possível localizar a raiz Solution para validar o PBIR.");
        return string.Empty;
    }
}
