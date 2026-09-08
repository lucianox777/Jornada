using Jornada.Operational.Sql;
using Jornada.Processor.Worker;

var provider = Environment.GetEnvironmentVariable("JORNADA_PROGRESSIVE_PROVIDER")
    ?? throw new InvalidOperationException("JORNADA_PROGRESSIVE_PROVIDER não definido.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_PROGRESSIVE_CONNECTION")
    ?? throw new InvalidOperationException("JORNADA_PROGRESSIVE_CONNECTION não definido.");
var pageSizeText = Environment.GetEnvironmentVariable("JORNADA_PROGRESSIVE_PAGE_SIZE");
var pageSize = string.IsNullOrWhiteSpace(pageSizeText) ? 100 : int.Parse(pageSizeText, System.Globalization.CultureInfo.InvariantCulture);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 1000);

var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var store = new ProgressiveIdentityOriginStore(database);
var total = 0;
var pages = 0;
while (true)
{
    var processed = await store.BackfillPageAsync(pageSize);
    pages++;
    total += processed;
    if (processed == 0) break;
    if (pages > 100_000)
        throw new InvalidOperationException("Backfill não convergiu dentro do limite de segurança.");
}

Console.WriteLine($"PROGRESSIVE IDENTITY BACKFILL: OK provider={database.Provider} total={total} pages={pages}");
