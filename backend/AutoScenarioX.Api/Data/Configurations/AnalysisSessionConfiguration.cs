using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class AnalysisSessionConfiguration : IEntityTypeConfiguration<AnalysisSession>
{
    public void Configure(EntityTypeBuilder<AnalysisSession> b)
    {
        b.ToTable("failure_analyses");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CampaignId).IsUnique();
        b.Property(x => x.Effectiveness).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        b.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Faults)
            .WithOne(f => f.Analysis)
            .HasForeignKey(f => f.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AnalysisFaultConfiguration : IEntityTypeConfiguration<AnalysisFault>
{
    public void Configure(EntityTypeBuilder<AnalysisFault> b)
    {
        b.ToTable("failure_faults");
        b.HasKey(x => x.Id);

        b.HasMany(x => x.Runs)
            .WithOne(r => r.Fault)
            .HasForeignKey(r => r.FaultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AnalysisFaultRunConfiguration : IEntityTypeConfiguration<AnalysisFaultRun>
{
    public void Configure(EntityTypeBuilder<AnalysisFaultRun> b)
    {
        b.ToTable("failure_fault_runs");
        b.HasKey(x => new { x.FaultId, x.RunId });
    }
}
