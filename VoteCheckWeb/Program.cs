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
builder.Services.AddHttpClient<EduskuntaApiClient>();
builder.Services.AddHostedService<VoteSyncService>();

var app = builder.Build();

app.Services.GetRequiredService<Db>().EnsureSchema();

app.UseStaticFiles();
app.UseOutputCache();
app.MapRazorPages();

// JSON API — the same surface the future mobile app consumes.
var api = app.MapGroup( "/api/v1" ).CacheOutput();

api.MapGet( "/sessions", ( Queries q, int count = 50 ) =>
    q.LatestSessions( Math.Clamp( count, 1, 200 ) ) );

api.MapGet( "/sessions/{id:int}", ( Queries q, int id ) =>
    q.GetSession( id ) is { } session
        ? Results.Ok( new { session, parties = q.GetPartyDistribution( id ) } )
        : Results.NotFound() );

api.MapGet( "/sessions/{id:int}/votes", ( Queries q, int id, string? party ) =>
    q.GetIndividualVotes( id, party ) );

api.MapGet( "/mps", ( Queries q, string? name ) => q.FindMps( name ) );

api.MapGet( "/mps/{personNumber:int}", ( Queries q, int personNumber, int count = 50 ) =>
    q.GetMpProfile( personNumber, Math.Clamp( count, 1, 200 ) ) is { } profile
        ? Results.Ok( profile )
        : Results.NotFound() );

api.MapGet( "/search", ( Queries q, string query, int count = 50 ) =>
    q.SearchSessions( query, Math.Clamp( count, 1, 200 ) ) );

app.Run();
