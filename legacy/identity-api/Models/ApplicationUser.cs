using Microsoft.AspNetCore.Identity;

namespace AutoScenarioX.AuthApi.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
}
