using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.ToTable("campaigns");
        b.HasKey(x => x.Id);
        b.Property(x => x.SeqNo).ValueGeneratedOnAdd();
        b.Property(x => x.Code).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.PassRate).HasColumnType("numeric(5,2)");
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
        b.Property(x => x.UpdatedAt).ValueGeneratedOnAddOrUpdate();
        b.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        b.HasOne(x => x.OracleConfig).WithMany().HasForeignKey(x => x.OracleConfigId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Versions).WithOne(x => x.Campaign).HasForeignKey(x => x.CampaignId);
        b.HasMany(x => x.Runs).WithOne(x => x.Campaign).HasForeignKey(x => x.CampaignId);
    }
}

public class CampaignVersionConfiguration : IEntityTypeConfiguration<CampaignVersion>
{
    public void Configure(EntityTypeBuilder<CampaignVersion> b)
    {
        b.ToTable("campaign_versions");
        b.HasKey(x => new { x.CampaignId, x.ScenarioVersionId });
        b.HasOne(x => x.ScenarioVersion).WithMany().HasForeignKey(x => x.ScenarioVersionId);
    }
}
