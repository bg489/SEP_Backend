using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ReadyScenarioVersionItemConfiguration : IEntityTypeConfiguration<ReadyScenarioVersionItem>
{
    public void Configure(EntityTypeBuilder<ReadyScenarioVersionItem> b)
    {
        b.HasNoKey();
        b.ToView("v_ready_scenario_versions");
        b.Property(x => x.EventTypes).HasColumnType("jsonb");
    }
}
