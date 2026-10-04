using System.Reflection;

namespace AudiobookServer.Api.Endpoints;

/// <summary>
/// What a client asks before it signs in: "is this an AudiobookServer, and does it
/// speak my API?" Every self-hosted install is a different URL typed by a person, so
/// the app checks this first and can say "that isn't an AudiobookServer" or "update
/// the server" instead of failing on a login response it can't parse.
/// </summary>
/// <param name="Product">Always "AudiobookServer".</param>
/// <param name="Version">The build, for display and bug reports. Not for feature checks.</param>
/// <param name="ApiVersion">
/// The contract version. Additive changes (new fields, new routes) don't change it;
/// a breaking change does, along with a new route for the old behaviour. Clients
/// compare against this, never against <paramref name="Version"/>.
/// </param>
public sealed record ServerInfo(string Product, string Version, int ApiVersion);

public static class ServerInfoEndpoints
{
    public const string Product = "AudiobookServer";
    public const int ApiVersion = 1;

    // "1.0.0+<commit sha>" locally (the SDK appends the commit); just "1.0.0" in the
    // Docker image, whose build context excludes .git.
    private static readonly string Version =
        typeof(ServerInfoEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IEndpointRouteBuilder MapServerInfoEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous by design: it's asked before there's anyone to sign in. It reveals
        // the version, as Jellyfin's equivalent does; the source is public anyway.
        app.MapGet("/api/server-info", () => new ServerInfo(Product, Version, ApiVersion))
            .AllowAnonymous();

        return app;
    }
}
