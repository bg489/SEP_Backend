using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
    }
}

public class ScenarioTagConfiguration : IEntityTypeConfiguration<ScenarioTag>
{
    public void Configure(EntityTypeBuilder<ScenarioTag> b)
    {
        b.ToTable("scenario_tags");
        b.HasKey(x => new { x.ScenarioId, x.TagId });
        b.HasOne(x => x.Scenario).WithMany(s => s.ScenarioTags).HasForeignKey(x => x.ScenarioId);
        b.HasOne(x => x.Tag).WithMany(t => t.ScenarioTags).HasForeignKey(x => x.TagId);
    }
}
