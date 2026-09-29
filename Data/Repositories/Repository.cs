using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace TournamentScheduler.Api.Data.Repositories;

/// <summary>Entity Framework implementation of <see cref="IRepository{TEntity}"/> over the table's DbSet.</summary>
public class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly DbSet<TEntity> _set;

    public Repository(TournamentDbContext db)
    {
        _set = db.Set<TEntity>();
    }

    private IQueryable<TEntity> Source(bool tracking) => tracking ? _set : _set.AsNoTracking();

    public ValueTask<TEntity?> FindAsync(params object[] keys) => _set.FindAsync(keys);

    public Task<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true) =>
        query(Source(tracking)).FirstOrDefaultAsync();

    public Task<List<TResult>> ListAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true) =>
        query(Source(tracking)).ToListAsync();

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate) => _set.AnyAsync(predicate);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate) => _set.CountAsync(predicate);

    public void Add(TEntity entity) => _set.Add(entity);

    public void AddRange(IEnumerable<TEntity> entities) => _set.AddRange(entities);

    public void Remove(TEntity entity) => _set.Remove(entity);

    public void RemoveRange(IEnumerable<TEntity> entities) => _set.RemoveRange(entities);
}
