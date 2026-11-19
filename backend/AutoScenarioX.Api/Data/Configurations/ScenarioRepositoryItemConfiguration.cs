using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ScenarioRepositoryItemConfiguration : IEntityTypeConfiguration<ScenarioRepositoryItem>
{
    public void Configure(EntityTypeBuilder<ScenarioRepositoryItem> b)
    {
        b.HasNoKey();
        b.ToView("v_scenario_repository");
        b.Property(x => x.EventTypes).HasColumnType("jsonb");
    }
}
