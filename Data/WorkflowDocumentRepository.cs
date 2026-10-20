using System.Text.Json;
using AutoScenarioX.AuthApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.AuthApi.Data;

public sealed class WorkflowDocumentRepository(AuthDbContext database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string area, string key, CancellationToken cancellationToken)
    {
        var document = await database.WorkflowDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Area == area && item.Key == key, cancellationToken);
        return document is null ? default : JsonSerializer.Deserialize<T>(document.Payload, JsonOptions);
    }

    public async Task<List<T>> ListAsync<T>(string area, CancellationToken cancellationToken)
    {
        var documents = await database.WorkflowDocuments.AsNoTracking()
            .Where(item => item.Area == area)
            .OrderByDescending(item => item.UpdatedAt)
            .ToListAsync(cancellationToken);
        return documents.Select(item => JsonSerializer.Deserialize<T>(item.Payload, JsonOptions)!)
            .Where(item => item is not null)
            .ToList();
    }

    public async Task SaveAsync<T>(string area, string key, T value, CancellationToken cancellationToken)
    {
        var document = await database.WorkflowDocuments
            .SingleOrDefaultAsync(item => item.Area == area && item.Key == key, cancellationToken);
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        if (document is null)
        {
            database.WorkflowDocuments.Add(new WorkflowDocument
            {
                Area = area,
                Key = key,
                Payload = payload,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            document.Payload = payload;
            document.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceAsync<T>(string area, IEnumerable<T> values, Func<T, string> keySelector, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var existing = await database.WorkflowDocuments.Where(item => item.Area == area).ToListAsync(cancellationToken);
        database.WorkflowDocuments.RemoveRange(existing);
        var updatedAt = DateTimeOffset.UtcNow;
        database.WorkflowDocuments.AddRange(values.Select(value => new WorkflowDocument
        {
            Area = area,
            Key = keySelector(value),
            Payload = JsonSerializer.Serialize(value, JsonOptions),
            UpdatedAt = updatedAt
        }));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
