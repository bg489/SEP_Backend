using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class GenericRepository<T>(AsxDbContext db) : IGenericRepository<T> where T : class
{
    protected readonly AsxDbContext Db = db;
    protected DbSet<T> Set => Db.Set<T>();

    public async Task<T?> FindAsync(object[] keyValues, CancellationToken ct = default)
        => await Set.FindAsync(keyValues, ct);

    public void Add(T entity) => Set.Add(entity);
    public void Update(T entity) => Set.Update(entity);
    public void Remove(T entity) => Set.Remove(entity);
}
