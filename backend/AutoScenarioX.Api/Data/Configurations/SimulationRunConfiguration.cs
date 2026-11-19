using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class SimulationRunConfiguration : IEntityTypeConfiguration<SimulationRun>
{
    public void Configure(EntityTypeBuilder<SimulationRun> b)
    {
        b.ToTable("simulation_runs");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CampaignId, x.ScenarioVersionId, x.Attempt }).IsUnique();
        b.HasIndex(x => new { x.CampaignId, x.ScenarioVersionId }).IsUnique().HasFilter("is_current");
        b.Property(x => x.Attempt).HasDefaultValue(1);
        b.Property(x => x.IsCurrent).HasDefaultValue(true);
        b.Property(x => x.ProgressJson).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
        b.Property(x => x.UpdatedAt).ValueGeneratedOnAddOrUpdate();

        b.HasOne(x => x.Campaign).WithMany(c => c.Runs).HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ScenarioVersion).WithMany().HasForeignKey(x => x.ScenarioVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SimulationRun>().WithMany().HasForeignKey(x => x.PreviousRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Telemetry).WithOne().HasForeignKey<RunTelemetry>(t => t.RunId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Evaluations).WithOne(e => e.Run).HasForeignKey(e => e.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RunTelemetryConfiguration : IEntityTypeConfiguration<RunTelemetry>
{
    public void Configure(EntityTypeBuilder<RunTelemetry> b)
    {
        b.ToTable("run_telemetry");
        b.HasKey(x => x.RunId);
        b.Property(x => x.ReceivedAt).ValueGeneratedOnAdd();
    }
}
