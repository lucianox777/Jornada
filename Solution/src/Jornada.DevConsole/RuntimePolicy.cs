enum JornadaConsoleMode
{
    Hml,
    Dev,
    Prod
}

sealed record RuntimePolicy(JornadaConsoleMode Mode)
{
    public string Name => Mode switch
    {
        JornadaConsoleMode.Dev => "DEV",
        JornadaConsoleMode.Prod => "PROD",
        _ => "HML"
    };

    public bool IsDev => Mode == JornadaConsoleMode.Dev;
    public bool IsProduction => Mode == JornadaConsoleMode.Prod;
    public bool DestructiveUiEnabled => !IsProduction;
    public bool SyntheticExtraEnabled => IsDev;

    public static RuntimePolicy Parse(string[] args)
    {
        var dev=args.Any(x=>string.Equals(x,"--dev",StringComparison.OrdinalIgnoreCase));
        var prod=args.Any(x=>string.Equals(x,"--prod",StringComparison.OrdinalIgnoreCase));
        if(dev&&prod)throw new ArgumentException("Use apenas um modo: --dev ou --prod.");
        return new RuntimePolicy(dev?JornadaConsoleMode.Dev:prod?JornadaConsoleMode.Prod:JornadaConsoleMode.Hml);
    }
}
