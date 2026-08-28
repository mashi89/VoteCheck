using VoteCheck.Core.Models;

namespace VoteCheck.Api;

/// <summary>
/// Projects upstream Eduskunta models onto VoteCheck's own contracts, resolving bilingual
/// fields to a single language.
///
/// Upstream returns { "fi": "kok", "sv": "saml" } style objects for most descriptive fields
/// (design.md §3.1), so the desktop app's Swedish toggle becomes a language parameter here
/// rather than a name-mapping table.
/// </summary>
internal static class Mapping
{
    public const string DefaultLanguage = "fi";

    /// <summary>Languages the API will resolve. Upstream carries fi/sv everywhere and en on
    /// some MP fields only, so en falls back rather than returning nulls.</summary>
    public static bool IsSupportedLanguage(string? lang) =>
        lang is null ||
        lang.Equals("fi", StringComparison.OrdinalIgnoreCase) ||
        lang.Equals("sv", StringComparison.OrdinalIgnoreCase) ||
        lang.Equals("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Picks one language out of a bilingual field. Falls back through the other languages
    /// rather than returning null, since en is absent on most vote fields and a missing label
    /// is worse than one in the wrong language.
    /// </summary>
    public static string? Localize(LocalizedText? text, string? lang)
    {
        if (text is null) return null;

        string? preferred = (lang ?? DefaultLanguage).ToLowerInvariant() switch
        {
            "sv" => text.Sv,
            "en" => text.En,
            _ => text.Fi,
        };

        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : text.Fi ?? text.Sv ?? text.En;
    }

    // ── MPs ──────────────────────────────────────────────────────────────────

    public static MpSummary ToSummary(Mp mp, string? lang) => new(
        Id: mp.Henkilonro,
        Name: mp.DisplayName,
        Party: Localize(mp.ViimeisinEduskuntaryhma?.Nimi, lang),
        District: Localize(mp.ViimeisinVaalipiiri?.Nimi, lang),
        Status: mp.EdustajantoimenTila?.ToString());

    public static MpDetail ToDetail(Mp mp, string? lang) => new(
        Id: mp.Henkilonro,
        Name: mp.DisplayName,
        FirstNames: mp.Etunimet,
        LastName: mp.Sukunimi,
        Party: Localize(mp.ViimeisinEduskuntaryhma?.Nimi, lang),
        District: Localize(mp.ViimeisinVaalipiiri?.Nimi, lang),
        Status: mp.EdustajantoimenTila?.ToString(),
        Profession: Localize(mp.Ammatti, lang),
        HomeMunicipality: mp.Kotikunta,
        BirthYear: mp.Syntymavuosi,
        Email: mp.Sahkoposti);

    // ── Votes ────────────────────────────────────────────────────────────────

    public static Tally? ToTally(AanestysTulos? tulos) =>
        tulos is null
            ? null
            : new Tally(tulos.Jaa, tulos.Ei, tulos.Tyhjia, tulos.Poissa, tulos.Yhteensa);

    public static DistributionRow ToRow(Aanestysjakauma jakauma, string? lang) => new(
        Name: Localize(jakauma.Nimi, lang),
        Jaa: jakauma.Jaa,
        Ei: jakauma.Ei,
        Tyhja: jakauma.Tyhjia,
        Poissa: jakauma.Poissa,
        Total: jakauma.Yhteensa);

    public static VoteSummary ToSummary(Aanestys vote, string? lang) => new(
        Id: vote.Id,
        SessionId: vote.IstunnonTunniste,
        Date: vote.Istuntopvm,
        // The matter being decided, not the ballot options — see design.md §3.1.
        Subject: Localize(vote.Kohta?.Otsikko, lang),
        BallotTitle: Localize(vote.Aanestysotsikko, lang),
        DocumentId: Localize(vote.Kohta?.Asiakirjat?.PaaasiakirjaEduskuntatunnus, lang),
        Annulled: vote.Aanestysmitatoity,
        Result: ToTally(vote.Aanestystulos));

    public static VoteDetail ToDetail(Aanestys vote, string? lang) => new(
        Vote: ToSummary(vote, lang),
        ByParty: vote.EduskuntaryhmaJakaumat.Select(j => ToRow(j, lang)).ToList(),
        ByGovernmentOpposition: vote.HallitusoppositioJakaumat.Select(j => ToRow(j, lang)).ToList(),
        ByDistrict: vote.VaalipiiriJakaumat.Select(j => ToRow(j, lang)).ToList());

    public static Ballot ToBallot(EdustajanAanestys ballot, string? lang) => new(
        Id: ballot.Henkilonumero,
        Name: $"{ballot.Etunimi} {ballot.Sukunimi}".Trim(),
        Party: Localize(ballot.Edkryhmalyhenne, lang),
        Choice: Localize(ballot.Kayttaytyminen, lang) ?? VoteChoice.Unknown.ToString());

    // ── Per-MP ───────────────────────────────────────────────────────────────

    public static MpVoteRow ToRow(MpVote vote, string? lang) => new(
        VoteId: vote.VoteId,
        SessionId: vote.SessionId,
        Date: vote.Date,
        Subject: Localize(vote.Subject, lang),
        DocumentId: Localize(vote.DocumentId, lang),
        Choice: vote.Choice.ToString(),
        Annulled: vote.Annulled);

    public static ActivitySummary ToContract(MpActivitySummary summary, string? lang) => new(
        Id: summary.Henkilonumero,
        Name: summary.Name,
        Party: Localize(summary.Party, lang),
        TotalVotes: summary.TotalVotes,
        Jaa: summary.Jaa,
        Ei: summary.Ei,
        Tyhja: summary.Tyhja,
        Poissa: summary.Poissa,
        Unknown: summary.Unknown,
        Present: summary.Present,
        AttendanceRate: summary.AttendanceRate);
}
