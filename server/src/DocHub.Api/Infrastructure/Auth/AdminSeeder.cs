using DocHub.DataAccess;
using DocHub.DataAccess.Entities;
using Microsoft.AspNetCore.Identity;

namespace DocHub.Api.Infrastructure.Auth;

/// <summary>
/// Sets the seeded administrator's password: `dotnet run -- seed-admin`.
///
/// A separate, operator-run step for the same reason migrations and the blob
/// container are. A password hash is salted per call, so it cannot be a
/// constant in a migration's seed data; and a credential that *was* constant
/// would be a credential in source control, identical on every machine that
/// ever cloned this repository.
///
/// Idempotent — running it again resets the password, which is also the
/// recovery path for a forgotten local one.
/// </summary>
internal static class AdminSeeder
{
    public const string PasswordKey = "Authentication:SeedAdminPassword";

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        var password = configuration[PasswordKey];

        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine(
                $"{PasswordKey} is not configured. Set it in appsettings.Development.json for "
                + "local development, or with `dotnet user-secrets` anywhere that matters.");

            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var admin = await users.FindByEmailAsync(DocHubDbContext.SystemUserEmail);

        if (admin is null)
        {
            Console.Error.WriteLine(
                $"No user found for {DocHubDbContext.SystemUserEmail}. Run "
                + "`dotnet ef database update` first — the row arrives with the migrations.");

            return 1;
        }

        // Remove-then-add rather than a reset token: this is a local
        // provisioning step with no email to send a token to.
        if (await users.HasPasswordAsync(admin))
        {
            var removed = await users.RemovePasswordAsync(admin);
            if (!Report(removed, "clear the existing password")) return 1;
        }

        if (!Report(await users.AddPasswordAsync(admin, password), "set the password")) return 1;

        // A password change invalidates sessions issued before it, which is the
        // point of running this after a suspected leak.
        await users.UpdateSecurityStampAsync(admin);

        Console.WriteLine(
            $"Password set for {admin.Email} ({admin.Role}). Any existing session is now invalid.");

        return 0;
    }

    private static bool Report(IdentityResult result, string action)
    {
        if (result.Succeeded) return true;

        // Identity's own messages name the failing rule — "passwords must be at
        // least 7 characters" is more use than "seeding failed".
        Console.Error.WriteLine($"Could not {action}:");

        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"  - {error.Description}");
        }

        return false;
    }
}
