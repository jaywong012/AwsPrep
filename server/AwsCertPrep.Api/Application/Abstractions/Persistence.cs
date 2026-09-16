using System.Linq.Expressions;

namespace AwsCertPrep.Api.Application.Abstractions;

/// <summary>
/// A reusable, composable description of a query: what to filter on, what to include, how to
/// order, how much to take.
///
/// This exists because a repository with only <c>GetAll</c> and <c>Find(predicate)</c> forces
/// callers either to pull whole tables into memory or to grow a bespoke method per query. A
/// specification keeps the filtering in the database while still letting the repository own how
/// the query is built.
/// </summary>
public interface ISpecification<T>
{
    Expression<Func<T, bool>>? Criteria { get; }

    IReadOnlyList<Expression<Func<T, object>>> Includes { get; }

    /// <summary>Includes that cannot be written as a lambda, such as "Items.Question.Options".</summary>
    IReadOnlyList<string> IncludeStrings { get; }

    Expression<Func<T, object>>? OrderBy { get; }

    Expression<Func<T, object>>? OrderByDescending { get; }

    int? Skip { get; }

    int? Take { get; }

    /// <summary>
    /// True for read-only queries. Tracking is the default for anything a handler will modify,
    /// because the Unit of Work has to see the change to persist it.
    /// </summary>
    bool AsNoTracking { get; }
}

/// <summary>Base class so a specification is a few lines of constructor rather than a full type.</summary>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<Expression<Func<T, object>>> _includes = [];
    private readonly List<string> _includeStrings = [];

    protected Specification(Expression<Func<T, bool>>? criteria = null) => Criteria = criteria;

    public Expression<Func<T, bool>>? Criteria { get; private set; }
    public IReadOnlyList<Expression<Func<T, object>>> Includes => _includes;
    public IReadOnlyList<string> IncludeStrings => _includeStrings;
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool AsNoTracking { get; private set; }

    protected void Where(Expression<Func<T, bool>> criteria) => Criteria = criteria;
    protected void AddInclude(Expression<Func<T, object>> include) => _includes.Add(include);
    protected void AddInclude(string include) => _includeStrings.Add(include);
    protected void SortBy(Expression<Func<T, object>> orderBy) => OrderBy = orderBy;
    protected void SortByDescending(Expression<Func<T, object>> orderBy) => OrderByDescending = orderBy;
    protected void Paginate(int skip, int take) => (Skip, Take) = (skip, take);
    protected void Limit(int take) => Take = take;
    protected void NoTracking() => AsNoTracking = true;
}

/// <summary>
/// Repository over one entity type.
///
/// <see cref="Query"/> is a deliberate escape hatch. A generic repository that only returns whole
/// entities forces projections and grouped reads back into memory, which is how a tuned query
/// quietly becomes an N+1. Handlers that need a narrow projection compose on <see cref="Query"/>;
/// everything else uses the specification methods and stays out of EF's way.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct = default);

    Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default);

    Task<IReadOnlyList<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken ct = default);

    Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken ct = default);

    Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken ct = default);

    void Add(T entity);

    void AddRange(IEnumerable<T> entities);

    void Remove(T entity);

    /// <summary>
    /// The underlying queryable, for projections and shapes a specification cannot express.
    /// Untracked by default: a handler that means to modify something asks for tracking.
    /// </summary>
    IQueryable<T> Query(bool tracked = false);
}

/// <summary>
/// One transactional scope over the repositories.
///
/// Repositories do not save. A handler makes all its changes and then commits once here, so a
/// request either persists completely or not at all, and so two repositories touched in the same
/// handler cannot half-commit.
/// </summary>
public interface IUnitOfWork
{
    IRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Detaches an entity the caller no longer wants persisted. Needed where a write races
    /// another request and the loser has to drop its copy rather than retry the insert.
    /// </summary>
    void Detach<T>(T entity) where T : class;
}
