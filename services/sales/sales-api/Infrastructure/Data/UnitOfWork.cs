namespace SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>
/// One commit boundary for a data change and the <c>outbox_events</c> row it produces
/// (CLAUDE.md rule 3, NFR-5). A single <see cref="SaveChangesAsync"/> is already one transaction;
/// <see cref="BeginTransactionAsync"/> is for writes that need multiple statements in order.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);

    Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct);

    Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct);

    /// <summary>Raw scalar read, used by the purge and erasure paths.</summary>
    IQueryable<T> SqlQuery<T>(FormattableString sql);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public class UnitOfWork(SalesDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null)
            return new AmbientScope();

        return new TransactionScope(await context.Database.BeginTransactionAsync(ct));
    }

    public Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct)
        => context.Database.ExecuteSqlInterpolatedAsync(sql, ct);

    public IQueryable<T> SqlQuery<T>(FormattableString sql) => context.Database.SqlQuery<T>(sql);

    private sealed class TransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed class AmbientScope : ITransactionScope
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
