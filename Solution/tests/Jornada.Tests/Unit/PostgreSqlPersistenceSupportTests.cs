using System.Data;
using System.Data.Common;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PostgreSqlPersistenceSupportTests
{
    [TestCase(-3)]
    [TestCase(0)]
    [TestCase(5)]
    public void Timestamp_binding_preserves_instant_and_uses_utc(int offsetHours)
    {
        var original = new DateTimeOffset(2026, 8, 29, 8, 30, 0, TimeSpan.FromHours(offsetHours));
        var normalized = (DateTimeOffset)PostgreSqlPersistenceSupport.NormalizeParameterValue(DbType.DateTimeOffset, original);
        Assert.Multiple(() =>
        {
            Assert.That(normalized.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(normalized.UtcTicks, Is.EqualTo(original.UtcTicks));
        });
    }

    [Test]
    public void Nullable_timestamp_and_calendar_date_preserve_their_semantics()
    {
        DateTimeOffset? absent = null;
        var civilDate = new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.Multiple(() =>
        {
            Assert.That(PostgreSqlPersistenceSupport.NormalizeParameterValue(DbType.DateTimeOffset, absent), Is.SameAs(DBNull.Value));
            Assert.That(PostgreSqlPersistenceSupport.NormalizeParameterValue(DbType.Date, civilDate), Is.EqualTo(civilDate));
            Assert.That(PostgreSqlPersistenceSupport.NormalizeParameterValue(DbType.Date, civilDate), Is.TypeOf<DateTime>());
            Assert.That(PostgreSqlPersistenceSupport.NormalizeParameterValue(DbType.DateTimeOffset, DBNull.Value), Is.SameAs(DBNull.Value));
        });
    }

    [Test]
    public void Rollback_cleanup_does_not_replace_the_original_failure()
    {
        var original = new InvalidDataException("original persistence failure");
        var actual = Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            try { throw original; }
            catch
            {
                await PostgreSqlPersistenceSupport.RollbackPreservingOriginalAsync(new DisposedTransaction());
                throw;
            }
        });
        Assert.That(actual, Is.SameAs(original));
    }

    private sealed class DisposedTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;
        protected override DbConnection DbConnection => null!;
        public override void Commit() => throw new ObjectDisposedException(nameof(DisposedTransaction));
        public override void Rollback() => throw new ObjectDisposedException(nameof(DisposedTransaction));
        public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new ObjectDisposedException(nameof(DisposedTransaction)));
    }
}
