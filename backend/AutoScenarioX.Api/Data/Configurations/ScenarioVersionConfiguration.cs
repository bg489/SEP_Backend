using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class ScenarioVersionConfiguration : IEntityTypeConfiguration<ScenarioVersion>
{
    public void Configure(EntityTypeBuilder<ScenarioVersion> b)
    {
        b.ToTable("scenario_versions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Content).HasColumnType("jsonb");
        b.Property(x => x.EventTypes).HasColumnType("jsonb");

        // Cột generated trong PostgreSQL: chỉ đọc
        b.Property(x => x.MapName).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.WeatherPreset).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.EventTypes).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.ActorCount).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.EventCount).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.NpcVehicleCount).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.PedestrianCount).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.SpeedLimitKmh).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.EgoBlueprint).ValueGeneratedOnAddOrUpdate();

        b.Property(x => x.PublishedAt).ValueGeneratedOnAdd();

        // Khoá ngoại tới import_records (không có navigation property), để EF biết thứ tự INSERT.
        b.HasOne<ImportRecord>()
            .WithMany()
            .HasForeignKey(x => x.ImportRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
