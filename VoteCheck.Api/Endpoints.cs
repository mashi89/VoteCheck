using Microsoft.AspNetCore.Mvc;
using VoteCheck.Core;

namespace VoteCheck.Api;

/// <summary>
/// The v1 API surface from design.md §3.1.
///
/// Every endpoint takes an optional <c>lang</c> (fi|sv|en, default fi) which resolves the
/// bilingual upstream fields. Sorting is applied explicitly wherever "latest" is implied,
/// because upstream's recent-votes endpoint is not chronologically ordered.
/// </summary>
internal static class Endpoints
{
    public static void MapVoteCheckApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        MapMps(api);
        MapVotes(api);
    }

    private static void MapMps(IEndpointRouteBuilder api)
    {
        var mps = api.MapGroup("/mps").WithTags("MPs");

        mps.MapGet("/", async (
                IEduskuntaClient client,
                [FromQuery] string? lang,
                [FromQuery] string? search,
                CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var all = await client.GetMpsAsync(ct);

                IEnumerable<Core.Models.Mp> filtered = all;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    // Surname-first match, mirroring the desktop app's "find by surname".
                    filtered = all.Where(m =>
                        (m.Sukunimi?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (m.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
                }

                var result = filtered
                    .OrderBy(m => m.Sukunimi, StringComparer.CurrentCulture)
                    .Select(m => Mapping.ToSummary(m, lang))
                    .ToList();

                return Results.Ok(result);
            })
            .WithName("GetMps")
            .WithSummary("Current members of parliament, optionally filtered by name.")
            .CacheOutput("volatile");

        mps.MapGet("/{id:int}", async (
                int id, IEduskuntaClient client, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var mp = await client.GetMpAsync(id, ct);
                return mp is null
                    ? NotFound($"No MP with henkilonumero {id}.")
                    : Results.Ok(Mapping.ToDetail(mp, lang));
            })
            .WithName("GetMp")
            .WithSummary("One MP by henkilonumero.")
            .CacheOutput("volatile");

        mps.MapGet("/{id:int}/votes", async (
                int id, MpActivityService activity, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var votes = await activity.GetRecentVotesForMpAsync(id, ct);

                var rows = votes
                    .OrderByDescending(v => v.Date, StringComparer.Ordinal)
                    .Select(v => Mapping.ToRow(v, lang))
                    .ToList();

                return Results.Ok(rows);
            })
            .WithName("GetMpVotes")
            .WithSummary("How an MP voted across the recent-votes window, newest first.")
            .WithDescription(
                "Scope is the recent divisions upstream exposes, not the MP's full term. " +
                "Derived from the ballots embedded in each vote.")
            .CacheOutput("volatile");

        mps.MapGet("/{id:int}/activity", async (
                int id, MpActivityService activity, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var summary = await activity.GetRecentActivityAsync(id, ct);
                return Results.Ok(Mapping.ToContract(summary, lang));
            })
            .WithName("GetMpActivity")
            .WithSummary("An MP's attendance and vote breakdown over the recent-votes window.")
            .WithDescription(
                "attendanceRate is null when the window contains no divisions for this MP, " +
                "which is not the same as 0%. Annulled divisions are excluded. Presiding as " +
                "Speaker is not an absence: the Speaker casts no ballot.")
            .CacheOutput("volatile");
    }

    private static void MapVotes(IEndpointRouteBuilder api)
    {
        var votes = api.MapGroup("/votes").WithTags("Votes");

        votes.MapGet("/", async (
                IEduskuntaClient client,
                [FromQuery] string? lang,
                [FromQuery] string? date,
                CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var recent = await client.GetRecentVotesAsync(ct);

                IEnumerable<Core.Models.Aanestys> filtered = recent;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    // Istuntopvm is "yyyy-MM-dd+HH:mm", so a prefix match supports
                    // yyyy / yyyy-MM / yyyy-MM-dd like the desktop app's date search.
                    filtered = recent.Where(v =>
                        v.Istuntopvm?.StartsWith(date, StringComparison.Ordinal) == true);
                }

                // Upstream does not return these in date order, so sort explicitly.
                var result = filtered
                    .OrderByDescending(v => v.Istuntopvm, StringComparer.Ordinal)
                    .Select(v => Mapping.ToSummary(v, lang))
                    .ToList();

                return Results.Ok(result);
            })
            .WithName("GetRecentVotes")
            .WithSummary("Recent divisions, newest first, optionally filtered by date prefix.")
            .CacheOutput("volatile");

        votes.MapGet("/{id}", async (
                string id, IEduskuntaClient client, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var vote = await client.GetVoteAsync(id, ct);
                return vote is null
                    ? NotFound($"No division with tunnus '{id}'.")
                    : Results.Ok(Mapping.ToSummary(vote, lang));
            })
            .WithName("GetVote")
            .WithSummary("One division by aanestystunnus, e.g. 2026-60-1.")
            .CacheOutput("immutable");

        votes.MapGet("/{id}/distribution", async (
                string id, IEduskuntaClient client, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var vote = await client.GetVoteAsync(id, ct);
                return vote is null
                    ? NotFound($"No division with tunnus '{id}'.")
                    : Results.Ok(Mapping.ToDetail(vote, lang));
            })
            .WithName("GetVoteDistribution")
            .WithSummary("Party, government/opposition and district breakdowns for a division.")
            .WithDescription("Ballots are excluded here; fetch /ballots when you need all 199 rows.")
            .CacheOutput("immutable");

        votes.MapGet("/{id}/ballots", async (
                string id,
                IEduskuntaClient client,
                [FromQuery] string? lang,
                [FromQuery] string? party,
                CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var vote = await client.GetVoteAsync(id, ct);
                if (vote is null) return NotFound($"No division with tunnus '{id}'.");

                IEnumerable<Core.Models.EdustajanAanestys> ballots = vote.Aanestystapahtumat;

                if (!string.IsNullOrWhiteSpace(party))
                {
                    // Party abbreviations are themselves bilingual ("kok"/"saml"), so match
                    // against either rather than only the requested language.
                    ballots = ballots.Where(b =>
                        string.Equals(b.Edkryhmalyhenne?.Fi?.Trim(), party.Trim(),
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(b.Edkryhmalyhenne?.Sv?.Trim(), party.Trim(),
                            StringComparison.OrdinalIgnoreCase));
                }

                var result = ballots
                    .OrderBy(b => b.Sukunimi, StringComparer.CurrentCulture)
                    .Select(b => Mapping.ToBallot(b, lang))
                    .ToList();

                return Results.Ok(result);
            })
            .WithName("GetVoteBallots")
            .WithSummary("Individual MP ballots for a division, optionally filtered by party.")
            .CacheOutput("immutable");

        api.MapGet("/sessions/{id}/votes", async (
                string id, IEduskuntaClient client, [FromQuery] string? lang, CancellationToken ct) =>
            {
                if (!Mapping.IsSupportedLanguage(lang)) return BadLanguage(lang);

                var sessionVotes = await client.GetVotesInSessionAsync(id, ct);
                var result = sessionVotes.Select(v => Mapping.ToSummary(v, lang)).ToList();
                return Results.Ok(result);
            })
            .WithTags("Votes")
            .WithName("GetSessionVotes")
            .WithSummary("All divisions in one plenary session, e.g. 2026-60.")
            .CacheOutput("immutable");
    }

    private static IResult BadLanguage(string? lang) =>
        Results.Problem(
            title: "Unsupported language",
            detail: $"'{lang}' is not supported. Use fi, sv or en.",
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult NotFound(string detail) =>
        Results.Problem(
            title: "Not found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound);
}
