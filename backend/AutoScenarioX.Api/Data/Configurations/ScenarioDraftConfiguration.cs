using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ScenarioDraftConfiguration : IEntityTypeConfiguration<ScenarioDraft>
{
    public void Configure(EntityTypeBuilder<ScenarioDraft> b)
    {
        b.ToTable("scenario_drafts");
        b.HasKey(x => x.ScenarioId);
        b.Property(x => x.Content).HasColumnType("jsonb");
        b.Property(x => x.RowVersion).IsConcurrencyToken();   // chống ghi đè khi 2 người cùng sửa
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
        b.Property(x => x.UpdatedAt).ValueGeneratedOnAddOrUpdate();

        // Khai báo khoá ngoại tới validation_reports (không có navigation property):
        // thiếu dòng này EF sẽ UPDATE draft TRƯỚC khi INSERT report -> lỗi FK 23503.
        b.HasOne<ValidationReport>()
            .WithMany()
            .HasForeignKey(x => x.LastValidationReportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
