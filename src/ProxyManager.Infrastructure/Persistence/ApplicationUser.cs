using Microsoft.AspNetCore.Identity;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
