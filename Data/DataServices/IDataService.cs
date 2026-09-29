using System.Linq.Expressions;

namespace TournamentScheduler.Api.Data.DataServices;

/// <summary>
/// What a service uses to read and write one entity — <c>IDataService&lt;Team&gt;</c>,
/// <c>IDataService&lt;CricketMatch&gt;</c> and so on, one generic implementation for all of them,
/// built on <see cref="Repositories.IRepository{TEntity}"/>.
///
/// <para><b>Shapes.</b> A <paramref name="shape"/> adds ordering or loads related rows (a team's
/// players, a match's innings). Shapes that load related rows are defined once in
/// <c>Data/Queries</c>, so services never need Entity Framework to ask for them.</para>
///
/// <para><b>Saving.</b> Nothing here saves. Adds, removes and changes to loaded entities are staged
/// and written together by <see cref="IUnitOfWork.SaveChangesAsync"/>, so an action that touches
/// several tables commits all of it or none of it.</para>
///
/// <para><b>Tracking.</b> Loaded entities are tracked by default so a service can change them and
/// save; pass <c>tracking: false</c> for read-only loads.</para>
/// </summary>
public interface IDataService<TEntity> where TEntity : class
{
    /// <summary>By primary key.</summary>
    ValueTask<TEntity?> GetByIdAsync(params object[] keys);

    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? shape = null,
        bool tracking = true);

    Task<List<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? shape = null,
        bool tracking = true);

    /// <summary>Any LINQ over the table — filters, projections, ordering — run in the database.</summary>
    Task<List<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true);

    Task<TResult?> QueryFirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate);

    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate);

    void Add(TEntity entity);

    void AddRange(IEnumerable<TEntity> entities);

    void Remove(TEntity entity);

    void RemoveRange(IEnumerable<TEntity> entities);
}
