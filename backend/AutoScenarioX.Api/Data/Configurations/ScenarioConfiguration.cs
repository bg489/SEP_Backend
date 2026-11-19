using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ScenarioConfiguration : IEntityTypeConfiguration<Scenario>
{
    public void Configure(EntityTypeBuilder<Scenario> b)
    {
        b.ToTable("scenarios");
        b.HasKey(x => x.Id);

        // Cột do DB sinh -> EF không gửi giá trị khi INSERT/UPDATE
        b.Property(x => x.SeqNo).ValueGeneratedOnAdd();
        b.Property(x => x.Code).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
        b.Property(x => x.UpdatedAt).ValueGeneratedOnAddOrUpdate();

        b.HasOne<VariantBatch>().WithMany().HasForeignKey(x => x.VariantBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Versions)
            .WithOne(v => v.Scenario)
            .HasForeignKey(v => v.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Draft)
            .WithOne(d => d.Scenario)
            .HasForeignKey<ScenarioDraft>(d => d.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
