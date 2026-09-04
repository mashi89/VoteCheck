using Microsoft.AspNetCore.HttpOverrides;
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

// Behind a TLS-terminating proxy the request arrives as plain http, so Request.Scheme
// would be "http" and every canonical URL, og:url and sitemap <loc> would advertise the
// wrong scheme — for a product whose distribution is shareable links, that matters.
// Off by default: trusting these headers when the app is directly exposed would let a
// client forge its own scheme and host.
var behindProxy = builder.Configuration.GetValue( "VoteCheck:BehindProxy", false );
if ( behindProxy ) {
    builder.Services.Configure<ForwardedHeadersOptions>( options => {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // The proxy is a separate container or host, so the default loopback-only trust
        // never matches. Clearing these trusts whatever fronts us — which is why the
        // whole block is opt-in.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    } );
}

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

// Must run before anything that reads the scheme or host — that is, before the pages
// and endpoints that build absolute URLs.
if ( behindProxy )
    app.UseForwardedHeaders();

app.UseStaticFiles();
app.UseCors();
app.UseOutputCache();
app.MapRazorPages();

// Documentation is on in every environment: this is a public, read-only API over open data.
app.UseSwagger();
app.UseSwaggerUI( options => options.SwaggerEndpoint( "/swagger/v1/swagger.json", "VoteCheck API v1" ) );

// HEAD as well as GET: uptime monitors default to HEAD, and a GET-only route answers
// 405, which reads as an outage rather than as a healthy service.
app.MapMethods( "/health", new[] { "GET", "HEAD" },
    () => Results.Ok( new { status = "ok" } ) ).ExcludeFromDescription();

// robots.txt and the sitemap are served rather than static files because both need the
// deployment's own absolute origin, which a file in wwwroot cannot know.
app.MapGet( "/robots.txt", ( HttpContext ctx ) => {
    var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Text( $"""
        # Votes and MP profiles are meant to be indexed — permalinks are the point.
        User-agent: *
        Allow: /

        # Search pages are generated per query and add nothing to an index.
        Disallow: /search

        Sitemap: {origin}/sitemap.xml
        """, "text/plain" );
} ).ExcludeFromDescription();

app.MapGet( "/sitemap.xml", ( Queries q, HttpContext ctx ) => {
    var origin = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var sb = new System.Text.StringBuilder()
        .AppendLine( """<?xml version="1.0" encoding="UTF-8"?>""" )
        .AppendLine( """<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""" )
        .AppendLine( $"<url><loc>{origin}/</loc></url>" )
        .AppendLine( $"<url><loc>{origin}/mps</loc></url>" );
    foreach ( var (loc, lastModified) in q.GetSitemapEntries() ) {
        sb.Append( $"<url><loc>{origin}{loc}</loc>" );
        if ( lastModified.Length >= 10 )
            sb.Append( $"<lastmod>{lastModified[..10]}</lastmod>" );
        sb.AppendLine( "</url>" );
    }
    sb.AppendLine( "</urlset>" );
    return Results.Text( sb.ToString(), "application/xml" );
} ).ExcludeFromDescription();

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
