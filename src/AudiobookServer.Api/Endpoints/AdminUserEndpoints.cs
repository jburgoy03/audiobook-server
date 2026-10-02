using System.Security.Claims;
using AudiobookServer.Api.Auth;

namespace AudiobookServer.Api.Endpoints;

public sealed record CreateUserRequest(string Username);

/// <summary>The complete set of non-public libraries the account may see.</summary>
public sealed record SetLibrariesRequest(List<Guid>? LibraryIds);

/// <summary>Returned once, at creation or reset. Only the passphrase's hash is stored.</summary>
public sealed record TemporaryPasswordResponse(Guid Id, string Username, string TemporaryPassword);

/// <summary>
/// Managing accounts from the admin page: admin only. The rules (what ends sessions,
/// what disabling means) live in <see cref="AccountAdmin"/>, shared with the `admin`
/// command. There's no promote or demote here: one admin is the plan, and the command
/// has set-admin for the day that changes.
/// </summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/admin/users")
            .RequireAuthorization(AuthPolicies.Admin);

        users.MapGet("", (AccountAdmin accounts, CancellationToken ct) => accounts.ListAsync(ct));

        users.MapPost("", async (CreateUserRequest request, AccountAdmin accounts) =>
        {
            var result = await accounts.CreateAsync(request.Username ?? "");
            return result.Failure switch
            {
                AccountFailure.None => Results.Created(
                    $"/api/admin/users/{result.User!.Id}",
                    new TemporaryPasswordResponse(result.User.Id, result.User.UserName!, result.TemporaryPassword!)),
                AccountFailure.Conflict => Results.Conflict(new { error = result.Message }),
                _ => Results.BadRequest(new { error = result.Message }),
            };
        });

        users.MapPost("/{id:guid}/reset-password", async (Guid id, AccountAdmin accounts) =>
        {
            if (await accounts.FindAsync(id.ToString()) is not { } user)
                return Results.NotFound();

            var result = await accounts.ResetPasswordAsync(user);
            return result.Succeeded
                ? Results.Ok(new TemporaryPasswordResponse(user.Id, user.UserName!, result.TemporaryPassword!))
                : Results.BadRequest(new { error = result.Message });
        });

        users.MapPost("/{id:guid}/disable", async (Guid id, ClaimsPrincipal principal, AccountAdmin accounts) =>
        {
            if (await accounts.FindAsync(id.ToString()) is not { } user)
                return Results.NotFound();

            var result = await accounts.DisableAsync(user, principal.GetUserId());
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { error = result.Message });
        });

        users.MapPut("/{id:guid}/libraries", async (Guid id, SetLibrariesRequest request, AccountAdmin accounts, CancellationToken ct) =>
        {
            if (await accounts.FindAsync(id.ToString()) is not { } user)
                return Results.NotFound();

            var result = await accounts.SetLibrariesAsync(user, request.LibraryIds ?? [], ct);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { error = result.Message });
        });

        users.MapPost("/{id:guid}/enable", async (Guid id, AccountAdmin accounts) =>
        {
            if (await accounts.FindAsync(id.ToString()) is not { } user)
                return Results.NotFound();

            var result = await accounts.EnableAsync(user);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { error = result.Message });
        });

        return app;
    }
}
