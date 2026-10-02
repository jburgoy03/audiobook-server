using System.Text.Json;
using System.Text.Json.Serialization;
using AudiobookServer.Api.Auth;
using AudiobookServer.Api.Cli;
using AudiobookServer.Api.Endpoints;
using AudiobookServer.Core.Data;
using AudiobookServer.Core.Media;
using AudiobookServer.Core.Metadata;
using AudiobookServer.Core.Scanning;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;

// `admin …` runs a server command (Cli/AdminCommand.cs) with the same configuration and
// services as the API, then exits without starting the web server. Its arguments are
// kept away from the host: the command-line configuration provider would read
// "--force" as a setting.
string[]? adminArgs = args is ["admin", .. var rest] ? rest : null;

var builder = WebApplication.CreateBuilder(adminArgs is null ? args : []);

if (adminArgs is not null)
{
    // Logs to stderr, so stdout carries only the command's output; and without EF's or
    // the host's chatter. The scanner's own logs still show, as progress.
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
        ["Logging:LogLevel:Microsoft.Hosting"] = "Warning",
    });
    builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
}

builder.Services.AddDbContext<AudiobookDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IMediaProbe>(_ => new FfprobeMediaProbe());

// Extracted covers live outside the library, which is treated as read-only.
// Override with Covers:Directory (e.g. a mounted volume in a container).
var coverDirectory = builder.Configuration["Covers:Directory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "covers");
builder.Services.AddSingleton<ICoverStore>(_ => new FfmpegCoverStore(coverDirectory));
builder.Services.AddSingleton<ILibraryWalker, LibraryWalker>();
builder.Services.AddScoped<IBookScanner, BookScanner>();
builder.Services.AddScoped<ILibraryScanService, LibraryScanService>();

// Blurbs from Open Library and Google Books, fetched when the admin asks. Open
// Library asks clients to identify themselves. Google Books only with a key
// (Blurbs:GoogleBooksApiKey; user-secrets locally, .env on the server).
builder.Services.AddSingleton(new BlurbSettings(builder.Configuration["Blurbs:GoogleBooksApiKey"]));
builder.Services.AddHttpClient<IBlurbFetcher, BlurbFetcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("audiobook-server/1.0 (+https://audiobooks.deanburgoyne.dev)");
});

builder.Services.AddAudiobookAuth(builder.Configuration, builder.Environment);

// Enums as strings in responses ("further", not 4), so clients don't depend on ordinals.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// In production the API sits behind cloudflared (and is otherwise reachable only over
// Tailscale), which forwards plain HTTP with X-Forwarded-Proto: https. Honouring it
// makes Request.IsHttps true, so the auth cookie is marked Secure. The proxy lists
// are cleared because the proxy's address isn't fixed; that's acceptable only because
// nothing but the tunnel and the tailnet can reach the port, and the worst a forged
// header can do is add the Secure flag.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (adminArgs is not null)
{
    Environment.ExitCode = await AdminCommand.RunFromConsoleAsync(app.Services, adminArgs);
    return;
}

await UserSeeder.SeedAsync(app.Services);

app.UseForwardedHeaders();

// The built web client (wwwroot, populated by the Docker build). Served before
// authentication, so the login page can load: it's code and fonts, never data.
// Vite fingerprints everything under /assets, so those can be cached forever;
// index.html must not be, or a deploy would never reach a returning browser.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl =
            ctx.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    }
});

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// No UseHttpsRedirection: TLS terminates at Cloudflare in production, and in
// development the web client reaches the API over plain HTTP through Vite's proxy.

app.MapAuthEndpoints();
app.MapProgressEndpoints();
app.MapLibraryEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminBookEndpoints();
app.MapBookEndpoints();

// An unknown /api route is a 404, never the web client's index.html.
app.MapFallback("/api/{**path}", () => Results.NotFound());

// Client-side routes (/books/:id) are real URLs: reloading one must return the app,
// which then routes itself. Anonymous, like the static files above.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

// Lets the integration tests' WebApplicationFactory<Program> see the entry point.
public partial class Program { }
