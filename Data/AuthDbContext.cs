using AutoScenarioX.AuthApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AutoScenarioX.AuthApi.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<WorkflowDocument> WorkflowDocuments => Set<WorkflowDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("app_auth");
        builder.Entity<ApplicationUser>().Property(user => user.FullName).HasMaxLength(160).IsRequired();
        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<WorkflowDocument>(entity =>
        {
            entity.ToTable("documents", "app_workflow");
            entity.HasKey(document => new { document.Area, document.Key });
            entity.Property(document => document.Area).HasMaxLength(80);
            entity.Property(document => document.Key).HasMaxLength(160);
            entity.Property(document => document.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(document => document.UpdatedAt).IsRequired();
            entity.HasIndex(document => new { document.Area, document.UpdatedAt });
        });
    }
}
