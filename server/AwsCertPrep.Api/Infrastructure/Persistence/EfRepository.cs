using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Infrastructure.Persistence;

/// <summary>Turns an <see cref="ISpecification{T}"/> into an EF query.</summary>
public static class SpecificationEvaluator
{
    public static IQueryable<T> Apply<T>(IQueryable<T> input, ISpecification<T> spec) where T : class
    {
        var query = input;

        if (spec.AsNoTracking) query = query.AsNoTracking();
        if (spec.Criteria is not null) query = query.Where(spec.Criteria);

        query = spec.Includes.Aggregate(query, (current, include) => current.Include(include));
        query = spec.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

        if (spec.OrderBy is not null) query = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDescending is not null) query = query.OrderByDescending(spec.OrderByDescending);

        // Skip/Take after ordering, because a page is only meaningful over a defined order.
        if (spec.Skip is { } skip) query = query.Skip(skip);
        if (spec.Take is { } take) query = query.Take(take);

        return query;
    }
}

/// <summary>
/// EF Core implementation of <see cref="IRepository{T}"/>.
///
/// Note that nothing here saves. Persisting is <see cref="UnitOfWork"/>'s job, so a handler that
/// touches three repositories still commits once.
/// </summary>
public class EfRepository<T>(AppDbContext db) : IRepository<T> where T : class
{
    private DbSet<T> Set => db.Set<T>();

    public async Task<T?> GetByIdAsync(object id, CancellationToken ct = default) =>
        await Set.FindAsync([id], ct);

    public async Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default) =>
        await SpecificationEvaluator.Apply(Set.AsQueryable(), specification).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T>? specification = null, CancellationToken ct = default) =>
        specification is null
            ? await Set.AsNoTracking().ToListAsync(ct)
            : await SpecificationEvaluator.Apply(Set.AsQueryable(), specification).ToListAsync(ct);

    public async Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken ct = default) =>
        specification is null
            ? await Set.CountAsync(ct)
            // Ordering and paging are irrelevant to a count and only cost the database work.
            : await Set.AsQueryable().Where(specification.Criteria ?? (_ => true)).CountAsync(ct);

    public async Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken ct = default) =>
        await Set.AsQueryable().AnyAsync(specification.Criteria ?? (_ => true), ct);

    public void Add(T entity) => Set.Add(entity);

    public void AddRange(IEnumerable<T> entities) => Set.AddRange(entities);

    public void Remove(T entity) => Set.Remove(entity);

    public IQueryable<T> Query(bool tracked = false) =>
        tracked ? Set.AsQueryable() : Set.AsNoTracking();
}

/// <summary>
/// Unit of Work over <see cref="AppDbContext"/>.
///
/// The context is already scoped per request, so this adds the single commit point and a cache of
/// repositories rather than a second lifetime to reason about.
/// </summary>
public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];

    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        if (_repositories.TryGetValue(typeof(TEntity), out var existing))
            return (IRepository<TEntity>)existing;

        var created = new EfRepository<TEntity>(db);
        _repositories[typeof(TEntity)] = created;
        return created;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public void Detach<TEntity>(TEntity entity) where TEntity : class =>
        db.Entry(entity).State = EntityState.Detached;
}
