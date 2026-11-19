using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class MapMetadataConfiguration : IEntityTypeConfiguration<MapMetadata>
{
    public void Configure(EntityTypeBuilder<MapMetadata> b)
    {
        b.ToTable("map_metadata");
        b.HasKey(x => x.Id);
        b.Property(x => x.SpawnPoints).HasColumnType("jsonb");
        b.Property(x => x.SpawnPointCount).ValueGeneratedOnAddOrUpdate();
        b.Property(x => x.ImportedAt).ValueGeneratedOnAdd();
    }
}
