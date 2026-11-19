using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> b)
    {
        b.ToTable("import_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.ImportReport).HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();

        // Khai báo khoá ngoại tới scenarios (không có navigation property):
        // thiếu dòng này EF sẽ INSERT import_records TRƯỚC khi INSERT scenario -> lỗi FK 23503
        // khi import tạo cả hai trong cùng một SaveChanges.
        b.HasOne<Scenario>()
            .WithMany()
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
