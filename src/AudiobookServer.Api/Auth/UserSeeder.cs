using AudiobookServer.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AudiobookServer.Api.Auth;

/// <summary>
/// There's no registration. The single account is created at startup from
/// Auth:SeedUser:Username and Auth:SeedUser:Password, and only when no user exists
/// yet, so the settings can stay in place afterwards without overwriting anything.
/// Locally they live in user-secrets; in a container, in environment variables.
/// </summary>
public static class UserSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UserSeeder));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        if (await users.Users.AnyAsync(ct))
            return;

        var username = config["Auth:SeedUser:Username"];
        var password = config["Auth:SeedUser:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning(
                "No user exists and Auth:SeedUser:Username / Auth:SeedUser:Password are not set. " +
                "Nobody can sign in until they are.");
            return;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = username,
            IsAdmin = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            // Fail loudly: a server nobody can sign in to should not look healthy.
            throw new InvalidOperationException(
                "Could not create the seed user: " +
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        logger.LogInformation("Created user {Username} from Auth:SeedUser", username);
    }
}
