using VoteCheck.Core;
using VoteCheckWeb.Data;
using VoteCheckWeb.Sync;

var builder = WebApplication.CreateBuilder( args );

builder.Services.AddRazorPages();
builder.Services.AddOutputCache( options => {
    // Historical votes are immutable; cache pages briefly so plenary-day
    // updates still appear within a poll interval.
    options.AddBasePolicy( policy => policy.Expire( TimeSpan.FromMinutes( 5 ) ) );
} );

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<Queries>();
// Upstream access goes through VoteCheck.Core, the single boundary to api.eduskunta.fi.
// No caching decorator here: the sync reads each page exactly once, and the pages are
// far too large to be worth retaining.
builder.Services.AddHttpClient<IEduskuntaClient, EduskuntaClient>();
builder.Services.AddHostedService<VoteSyncService>();

// The JSON API is documented and cross-origin capable because it is the surface a future
// mobile or PWA client consumes (design.md §3). Allowed origins come from configuration
// so a deployment names its own; with none set we stay closed rather than defaulting to "*".
var allowedOrigins = builder.Configuration.GetSection( "VoteCheck:AllowedOrigins" ).Get<string[]>() ?? [];
builder.Services.AddCors( options => options.AddDefaultPolicy( policy => {
    if ( allowedOrigins.Length > 0 )
        policy.WithOrigins( allowedOrigins ).AllowAnyHeader().AllowAnyMethod();
} ) );

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen( options => options.SwaggerDoc( "v1", new() {
    Title = "VoteCheck API",
    Version = "v1",
    Description =
        "Read-only JSON over Finnish parliamentary voting data, served from a local mirror "
        + "of api.eduskunta.fi. Descriptive fields resolve to one language via the 'lang' "
        + "query parameter (fi|sv, default fi); upstream carries no English on vote data.",
} ) );

var app = builder.Build();

app.Services.GetRequiredService<Db>().EnsureSchema();

app.UseStaticFiles();
app.UseCors();
app.UseOutputCache();
app.MapRazorPages();

// Documentation is on in every environment: this is a public, read-only API over open data.
app.UseSwagger();
app.UseSwaggerUI( options => options.SwaggerEndpoint( "/swagger/v1/swagger.json", "VoteCheck API v1" ) );

app.MapGet( "/health", () => Results.Ok( new { status = "ok" } ) ).ExcludeFromDescription();

// JSON API — the same surface the future mobile app consumes.
var api = app.MapGroup( "/api/v1" ).CacheOutput();

api.MapGet( "/sessions", ( Queries q, int count = 50, string? lang = null ) =>
    q.LatestSessions( Math.Clamp( count, 1, 200 ), lang ) );

api.MapGet( "/sessions/{id}", ( Queries q, string id, string? lang = null ) =>
    q.GetSession( id, lang ) is { } session
        ? Results.Ok( new { session, parties = q.GetPartyDistribution( id ) } )
        : Results.NotFound() );

api.MapGet( "/sessions/{id}/votes", ( Queries q, string id, string? party ) =>
    q.GetIndividualVotes( id, party ) );

api.MapGet( "/mps", ( Queries q, string? name ) => q.FindMps( name ) );

api.MapGet( "/mps/{personNumber:int}", ( Queries q, int personNumber, int count = 50, string? lang = null ) =>
    q.GetMpProfile( personNumber, Math.Clamp( count, 1, 200 ), lang ) is { } profile
        ? Results.Ok( profile )
        : Results.NotFound() );

// The activity rollup the product is named for: attendance and the Jaa/Ei/Tyhjä/Poissa
// split across every division a member could have voted in.
api.MapGet( "/mps/{personNumber:int}/activity", ( Queries q, int personNumber ) =>
    q.GetMpActivity( personNumber ) is { } activity
        ? Results.Ok( activity )
        : Results.NotFound() );

// Search always matches Finnish text — that is what the FTS index holds — but renders
// results in the requested language.
api.MapGet( "/search", ( Queries q, string query, int count = 50, string? lang = null ) =>
    q.SearchSessions( query, Math.Clamp( count, 1, 200 ), lang ) );

app.Run();
