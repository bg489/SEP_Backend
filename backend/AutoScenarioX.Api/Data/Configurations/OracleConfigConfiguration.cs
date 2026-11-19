using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public class OracleConfigConfiguration : IEntityTypeConfiguration<OracleConfig>
{
    public void Configure(EntityTypeBuilder<OracleConfig> b)
    {
        b.ToTable("oracle_configs");
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
    }
}
