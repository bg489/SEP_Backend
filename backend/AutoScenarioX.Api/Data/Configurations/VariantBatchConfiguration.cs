using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class VariantBatchConfiguration : IEntityTypeConfiguration<VariantBatch>
{
    public void Configure(EntityTypeBuilder<VariantBatch> b)
    {
        b.ToTable("variant_batches");
        b.HasKey(x => x.Id);
        b.Property(x => x.ParameterGrid).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
    }
}
