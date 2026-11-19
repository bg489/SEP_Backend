using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ValidationReportConfiguration : IEntityTypeConfiguration<ValidationReport>
{
    public void Configure(EntityTypeBuilder<ValidationReport> b)
    {
        b.ToTable("validation_reports");
        b.HasKey(x => x.Id);
        b.Property(x => x.Errors).HasColumnType("jsonb");
        b.Property(x => x.Warnings).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();

        // Batch thêm scenario và report trong cùng SaveChanges: EF phải INSERT scenario trước.
        b.HasOne<Scenario>().WithMany().HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
