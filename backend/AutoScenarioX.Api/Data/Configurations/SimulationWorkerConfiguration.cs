using AutoScenarioX.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoScenarioX.Api.Data.Configurations;

public sealed class SimulationWorkerConfiguration : IEntityTypeConfiguration<SimulationWorker>
{
    public void Configure(EntityTypeBuilder<SimulationWorker> b)
    {
        b.ToTable("simulation_workers");
        b.HasKey(x => x.Id);
        b.Property(x => x.MapsJson).HasColumnType("jsonb");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
    }
}
