using AutoScenarioX.Api.Data;
using AutoScenarioX.Api.Models.Entities;
using AutoScenarioX.Api.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.Api.Repositories.Implementations;

public class TagRepository(AsxDbContext db) : GenericRepository<Tag>(db), ITagRepository
{
    public async Task<IReadOnlyList<TagUsage>> ListUsageAsync(CancellationToken ct = default)
        => await Set.AsNoTracking()
            .Where(t => t.ScenarioTags.Any())
            .OrderBy(t => t.Name)
            .Select(t => new TagUsage(
                t.Name,
                t.ScenarioTags.Count(),
                t.ScenarioTags.Count(st => st.Scenario.Status == ScenarioStatuses.Active)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListNamesAsync(Guid scenarioId, CancellationToken ct = default)
        => await Db.ScenarioTags.AsNoTracking()
            .Where(st => st.ScenarioId == scenarioId)
            .Select(st => st.Tag.Name)
            .OrderBy(name => name)
            .ToListAsync(ct);

    public async Task AttachAsync(Guid scenarioId, IReadOnlyCollection<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0) return;
        // Tag vừa tạo trong cùng unit of work (vd nhiều biến thể một batch) chưa có trong DB: xem Local trước.
        var tags = Set.Local.Where(t => names.Contains(t.Name)).ToDictionary(t => t.Name);
        var missing = names.Where(n => !tags.ContainsKey(n)).ToList();
        if (missing.Count > 0)
            foreach (var tag in await Set.Where(t => missing.Contains(t.Name)).ToListAsync(ct))
                tags[tag.Name] = tag;

        foreach (var name in names)
        {
            if (!tags.TryGetValue(name, out var tag))
            {
                tag = new Tag { Id = Guid.NewGuid(), Name = name };
                Set.Add(tag);
                tags[name] = tag;
            }
            Db.ScenarioTags.Add(new ScenarioTag { ScenarioId = scenarioId, TagId = tag.Id });
        }
    }

    public async Task DetachAsync(Guid scenarioId, IReadOnlyCollection<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0) return;
        var links = await Db.ScenarioTags
            .Where(st => st.ScenarioId == scenarioId && names.Contains(st.Tag.Name))
            .ToListAsync(ct);
        Db.ScenarioTags.RemoveRange(links);
    }
}
