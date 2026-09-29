using System.Linq.Expressions;
using TournamentScheduler.Api.Data.Repositories;

namespace TournamentScheduler.Api.Data.DataServices;

/// <summary>The one implementation of <see cref="IDataService{TEntity}"/>, for every entity.</summary>
public class DataService<TEntity> : IDataService<TEntity> where TEntity : class
{
    private readonly IRepository<TEntity> _repository;

    public DataService(IRepository<TEntity> repository)
    {
        _repository = repository;
    }

    public ValueTask<TEntity?> GetByIdAsync(params object[] keys) => _repository.FindAsync(keys);

    public Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? shape = null,
        bool tracking = true) =>
        _repository.FirstOrDefaultAsync(q => Shaped(q, shape).Where(predicate), tracking);

    public Task<List<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? shape = null,
        bool tracking = true) =>
        _repository.ListAsync(q =>
        {
            // Filter first, then shape: a shape may order the rows, and the order must survive.
            var filtered = predicate == null ? q : q.Where(predicate);
            return Shaped(filtered, shape);
        }, tracking);

    public Task<List<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true) =>
        _repository.ListAsync(query, tracking);

    public Task<TResult?> QueryFirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>> query, bool tracking = true) =>
        _repository.FirstOrDefaultAsync(query, tracking);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate) => _repository.AnyAsync(predicate);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate) => _repository.CountAsync(predicate);

    public void Add(TEntity entity) => _repository.Add(entity);

    public void AddRange(IEnumerable<TEntity> entities) => _repository.AddRange(entities);

    public void Remove(TEntity entity) => _repository.Remove(entity);

    public void RemoveRange(IEnumerable<TEntity> entities) => _repository.RemoveRange(entities);

    private static IQueryable<TEntity> Shaped(IQueryable<TEntity> query, Func<IQueryable<TEntity>, IQueryable<TEntity>>? shape) =>
        shape == null ? query : shape(query);
}
