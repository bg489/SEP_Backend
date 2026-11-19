using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public sealed class SystemSettingsConfiguration : IEntityTypeConfiguration<SystemSettings>
{
    public void Configure(EntityTypeBuilder<SystemSettings> b)
    {
        b.ToTable("system_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<OracleConfig>().WithMany().HasForeignKey(x => x.ActiveOracleConfigId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SimilarityAnalysisConfiguration : IEntityTypeConfiguration<SimilarityAnalysis>
{
    public void Configure(EntityTypeBuilder<SimilarityAnalysis> b)
    {
        b.ToTable("similarity_analyses");
        b.HasKey(x => x.Id);
        b.Property(x => x.ResultJson).HasColumnType("jsonb");
    }
}
