namespace VoteCheck.Api;

// Response shapes for VoteCheck's own API.
//
// These deliberately do NOT mirror the upstream Eduskunta objects. A single upstream vote is
// ~75 KB — 199 ballots plus three breakdown sets — and proxying that to a phone for a list
// view would be wasteful (design.md §6). Each contract carries only what its view needs, and
// bilingual upstream fields are resolved to a single string in the caller's language.

/// <summary>An MP as shown in lists and search results.</summary>
public sealed record MpSummary(
    int Id,
    string Name,
    string? Party,
    string? District,
    string? Status);

/// <summary>Fuller MP detail for a profile page.</summary>
public sealed record MpDetail(
    int Id,
    string Name,
    string? FirstNames,
    string? LastName,
    string? Party,
    string? District,
    string? Status,
    string? Profession,
    string? HomeMunicipality,
    int? BirthYear,
    string? Email);

/// <summary>The overall Jaa/Ei/Tyhjä/Poissa tally for a division.</summary>
public sealed record Tally(int Jaa, int Ei, int Tyhja, int Poissa, int Total);

/// <summary>One named breakdown row (a party, a district, or government/opposition).</summary>
public sealed record DistributionRow(
    string? Name,
    int Jaa,
    int Ei,
    int Tyhja,
    int Poissa,
    int Total);

/// <summary>A division as shown in lists.</summary>
public sealed record VoteSummary(
    string? Id,
    string? SessionId,
    string? Date,
    string? Subject,
    string? BallotTitle,
    string? DocumentId,
    bool Annulled,
    Tally? Result);

/// <summary>
/// Full breakdown for one division. Ballots are not included — they are a separate call, so a
/// client showing only the party split never pays for 199 rows.
/// </summary>
public sealed record VoteDetail(
    VoteSummary Vote,
    IReadOnlyList<DistributionRow> ByParty,
    IReadOnlyList<DistributionRow> ByGovernmentOpposition,
    IReadOnlyList<DistributionRow> ByDistrict);

/// <summary>How one MP voted in one division.</summary>
public sealed record Ballot(
    int Id,
    string Name,
    string? Party,
    string Choice);

/// <summary>One row of an MP's voting history.</summary>
public sealed record MpVoteRow(
    string? VoteId,
    string? SessionId,
    string? Date,
    string? Subject,
    string? DocumentId,
    string Choice,
    bool Annulled);

/// <summary>
/// An MP's rolled-up activity over a window of divisions. AttendanceRate is null rather than 0
/// when the window is empty, so "no data" stays distinguishable from "never showed up".
/// </summary>
public sealed record ActivitySummary(
    int Id,
    string? Name,
    string? Party,
    int TotalVotes,
    int Jaa,
    int Ei,
    int Tyhja,
    int Poissa,
    int Unknown,
    int Present,
    double? AttendanceRate);
