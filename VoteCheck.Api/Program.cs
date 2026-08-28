using Microsoft.Extensions.Caching.Memory;
using VoteCheck.Api;
using VoteCheck.Core;

var builder = WebApplication.CreateBuilder(args);

// ── Upstream client ──────────────────────────────────────────────────────────
// EduskuntaClient is registered through IHttpClientFactory (socket reuse, sane handler
// lifetime), then wrapped in the caching decorator so every consumer gets caching without
// knowing about it. See design.md §3.

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<EduskuntaClient>(http =>
{
    http.BaseAddress = new Uri(EduskuntaClient.DefaultBaseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
    // Upstream is a public service with a documented rate limit; identify ourselves so
    // operators can see who is calling.
    http.DefaultRequestHeaders.UserAgent.ParseAdd("VoteCheck/1.0 (+https://github.com/mashi89/VoteCheck)");
});

builder.Services.AddScoped<IEduskuntaClient>(sp => new CachingEduskuntaClient(
    sp.GetRequiredService<EduskuntaClient>(),
    sp.GetRequiredService<IMemoryCache>()));

builder.Services.AddScoped(sp => new MpActivityService(sp.GetRequiredService<IEduskuntaClient>()));

// ── Output caching ───────────────────────────────────────────────────────────
// Two policies matching the upstream data's nature: completed divisions never change, while
// "who is seated" and "what just happened" do. This sits in front of the in-process client
// cache, so a repeat request is answered without even re-serializing.

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("immutable", policy => policy
        .Expire(TimeSpan.FromHours(12))
        .SetVaryByQuery("lang", "party"));

    options.AddPolicy("volatile", policy => policy
        .Expire(TimeSpan.FromMinutes(10))
        .SetVaryByQuery("lang", "search", "date"));
});

// ── CORS ─────────────────────────────────────────────────────────────────────
// The PWA (Step 3) will be served from a different origin. Allowed origins come from config
// (VoteCheck:AllowedOrigins) so deployments set their own; with none configured we stay
// closed rather than defaulting to "*".

var allowedOrigins = builder.Configuration
    .GetSection("VoteCheck:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "VoteCheck API",
        Version = "v1",
        Description =
            "Finnish Parliament voting records, sourced from api.eduskunta.fi and reshaped " +
            "for client use. All descriptive fields are resolved to a single language via " +
            "the 'lang' query parameter (fi|sv|en, default fi).",
    });
});

builder.Services.AddProblemDetails();

// Upstream being down is a normal condition for this API, not an internal fault — map it to
// 502/504 rather than letting it surface as a bare 500.
builder.Services.AddExceptionHandler<UpstreamExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Swagger is on in every environment: this is a public read-only API over open data, and
// discoverability is the point (design.md §4, Step 2).
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "VoteCheck API v1");
    options.DocumentTitle = "VoteCheck API";
});

app.UseCors();
app.UseOutputCache();

app.MapVoteCheckApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithTags("Meta")
   .ExcludeFromDescription();

app.MapGet("/", () => Results.Redirect("/swagger"))
   .ExcludeFromDescription();

app.Run();

// Exposed so the integration tests can drive the real pipeline via WebApplicationFactory.
public partial class Program { }
