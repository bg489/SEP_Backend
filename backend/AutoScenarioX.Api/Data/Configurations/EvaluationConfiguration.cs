using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> b)
    {
        b.ToTable("evaluations");
        b.HasKey(x => x.Id);
        b.Property(x => x.EvaluatedAt).ValueGeneratedOnAdd();

        b.HasOne(x => x.OracleConfig)
            .WithMany()
            .HasForeignKey(x => x.OracleConfigId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Violations)
            .WithOne(v => v.Evaluation)
            .HasForeignKey(v => v.EvaluationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ViolationConfiguration : IEntityTypeConfiguration<ViolationEntity>
{
    public void Configure(EntityTypeBuilder<ViolationEntity> b)
    {
        b.ToTable("violations");
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
        b.Property(x => x.Details).HasColumnType("jsonb");
    }
}
