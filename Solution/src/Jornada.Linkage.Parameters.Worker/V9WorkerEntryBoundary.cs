namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// The single public entry-point exception boundary for all Program.cs routes,
/// including host construction/startup. No exception text, stack, connection
/// string, path or inner exception is ever printed by this boundary.
/// </summary>
public static class V9WorkerEntryBoundary
{
    public static async Task<int> ExecuteAsync(
        string[] commandArgs, Func<string[], Task> dispatch,
        TextWriter? publicError = null)
    {
        ArgumentNullException.ThrowIfNull(commandArgs);
        ArgumentNullException.ThrowIfNull(dispatch);
        try
        {
            await dispatch(commandArgs).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                (publicError ?? Console.Error).WriteLine(
                    V9CalibradorLastErrorLog.DetailedErrorCode(error));
                V9WorkerDevelopmentDiagnostics.WriteException(error, publicError);
            }
            catch
            {
                // A broken public log stream must never propagate an exception
                // to .NET's unhandled-exception handler (which prints secrets).
            }
            return 1;
        }
    }
}
