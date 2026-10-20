using AutoScenarioX.AuthApi.Models;
using Microsoft.AspNetCore.Identity;

namespace AutoScenarioX.AuthApi.Auth;

public static class IdentityRoleSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await roleManager.RoleExistsAsync(role)) continue;

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed role '{role}': {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }

        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Configure both BootstrapAdmin:Email and BootstrapAdmin:Password, or leave both unset.");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = configuration["BootstrapAdmin:FullName"]?.Trim() ?? "AutoScenarioX Administrator"
            };
            var createResult = await userManager.CreateAsync(admin, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the bootstrap administrator: " +
                    string.Join("; ", createResult.Errors.Select(error => error.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(admin, RoleNames.Administrator))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, RoleNames.Administrator);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not assign the administrator role: " +
                    string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }
        }

        logger.LogInformation("Bootstrap administrator is ready for {Email}.", email);
    }
}
