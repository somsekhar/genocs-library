using Genocs.Core.CQRS.Queries;
using Genocs.Core.Domain.Entities;
using Genocs.Core.Domain.Repositories;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Linq.Expressions;

namespace Genocs.Persistence.MongoDb.Repositories.Mentor;

public interface IMongoRepository<TEntity, TKey> : IRepositoryOfEntity<TEntity, TKey>
    where TEntity : IEntity<TKey>
{
    IMongoCollection<TEntity> Collection { get; }

    IMongoQueryable<TEntity> GetMongoQueryable();

    Task<TEntity> GetAsync(Expression<Func<TEntity, bool>> predicate);
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

    Task<PagedResult<TEntity>> BrowseAsync<TQuery>(Expression<Func<TEntity, bool>> predicate, TQuery query)
        where TQuery : IPagedQuery;

    Task AddAsync(TEntity entity);

    Task UpdateAsync(TEntity entity, Expression<Func<TEntity, bool>> predicate);

    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);
}