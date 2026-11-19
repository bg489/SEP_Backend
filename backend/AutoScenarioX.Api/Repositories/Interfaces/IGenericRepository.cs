namespace AutoScenarioX.Api.Repositories.Interfaces;

/// <summary>Thao tác CRUD cơ bản dùng chung cho mọi entity có khoá.</summary>
public interface IGenericRepository<T> where T : class
{
    /// <summary>Tìm theo khoá chính (có tracking - dùng khi sắp sửa dữ liệu).</summary>
    Task<T?> FindAsync(object[] keyValues, CancellationToken ct = default);
    void Add(T entity);
    void Update(T entity);
    void Remove(T entity);
}
