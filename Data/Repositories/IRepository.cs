using System.Linq.Expressions;

namespace TournamentScheduler.Api.Data.Repositories;

/// <summary>
/// Generic database access for one entity (one table). The only type besides the unit of work
/// that talks to <see cref="TournamentDbContext"/>; everything above it — data services, then
/// services, then controllers — reaches the database through here.
///
/// Queries are described as a function over the table (<paramref name="query"/>) and executed here,
/// so callers compose plain LINQ and never touch Entity Framework themselves.
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>By primary key; a tracked entity already loaded is returned without a query.</summary>
    ValueTask<TEntity?> FindAsync(params object[] keys);

    Task<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true);

    Task<List<TResult>> ListAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate);

    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>Staged until <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(TEntity entity);

    void AddRange(IEnumerable<TEntity> entities);

    void Remove(TEntity entity);

    void RemoveRange(IEnumerable<TEntity> entities);
}
