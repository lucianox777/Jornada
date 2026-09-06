namespace Jornada.Integrador.Tests;

internal static class HelpTests
{
    public static void Run()
    {
        AssertHelp("/help");
        AssertHelp("--help");
        AssertHelp("-h");

        if (JornadaIntegrator.IsHelpRequest([]))
            throw new InvalidOperationException("Array vazio não pode ser tratado como ajuda explícita.");

        if (JornadaIntegrator.IsHelpRequest(["--help", "extra"]))
            throw new InvalidOperationException("Ajuda explícita deve aceitar exatamente um argumento.");
    }

    private static void AssertHelp(string alias)
    {
        if (!JornadaIntegrator.IsHelpRequest([alias]))
            throw new InvalidOperationException($"Alias de ajuda não reconhecido: {alias}");
    }
}
