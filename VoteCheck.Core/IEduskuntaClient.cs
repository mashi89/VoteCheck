using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoteCheck.Core.Models;

namespace VoteCheck.Core
{
    // The upstream data surface VoteCheck depends on. Exists so the caching layer can be a
    // decorator and so VoteCheck.Api can inject either implementation — see design.md §3.
    public interface IEduskuntaClient
    {
        // Members from the roster endpoint — which is **not** the sitting parliament, and
        // cannot be made to be.
        //
        // It returns a fixed slice of 1000 records covering the whole history of the chamber,
        // birth years back to the 1830s. Of those, only a few dozen are sitting members; the
        // rest have left. There is no paging: every query parameter tried — perPage, page,
        // maxResults, startFromIndex — is not rejected but silently returns **zero** records,
        // which is a worse failure than an error because it looks like an empty roster.
        //
        // So this cannot answer "who is in parliament now". The mirror does not use it: the
        // member table is built from the ballots inside each division, which name every member
        // who voted and are therefore complete by construction. For detail about one member,
        // GetMpAsync(henkilonumero) works properly.
        Task<IReadOnlyList<Mp>> GetMpsAsync(CancellationToken cancellationToken = default);

        Task<Mp?> GetMpAsync(int henkilonumero, CancellationToken cancellationToken = default);

        Task<Aanestys?> GetVoteAsync(string aanestystunnus, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Aanestys>> GetVotesInSessionAsync(
            string istuntotunnus, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Aanestys>> GetVotesForMatterAsync(
            string eduskuntatunnus, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(CancellationToken cancellationToken = default);

        // Walks the vote archive in pages, oldest first, for callers that need history rather
        // than "what happened lately" — the SQLite mirror's backfill, principally.
        //
        // None of the taysistunnot/* endpoints can enumerate: they answer by identifier, and
        // uusimmat-aanestykset only returns the newest handful. This goes through the search
        // index instead, which is the only surface that can page the whole archive.
        //
        // Ordering is ascending by sitting date deliberately. New divisions then append at the
        // end, so an index stays pointing at the same vote between cycles and a stored cursor
        // survives; descending order would renumber everything each time parliament sits.
        //
        // Bounded by MaxSearchWindow: startFromIndex + maxResults may not exceed it. The
        // window comfortably covers a recent-years backfill (2023 onward is ~2,800 divisions),
        // but not the full archive back to 2008 — that needs the async dataset export.
        Task<VotePage> GetVotePageAsync(
            int fromVpYear,
            int startFromIndex,
            int maxResults,
            CancellationToken cancellationToken = default);

        // The matter behind a division, by the identifier the division carries in
        // kohta.asiakirjat.paaasiakirjaEduskuntatunnus — "HE 113/2026 vp".
        //
        // This is where a division's subject stops being a legal title and becomes something a
        // reader recognises: the keywords, the document type in plain Finnish, and how the
        // matter ended. Divisions share matters — about three divisions to each — so a caller
        // walking the archive should look up each identifier once.
        //
        // An unknown identifier comes back as null rather than an error — a combined identifier
        // such as "LA 1, 18/2023 vp" names two documents at once and has no single matter, so
        // "no such thing" is an ordinary answer here and the caller records it as one.
        Task<Valtiopaivaasia?> GetMatterAsync(
            string eduskuntatunnus, CancellationToken cancellationToken = default);

        // A plenary session's minutes, read for what each vote's options were — "PTK 108/2026 vp"
        // for vpYear 2026, session 108. See Poytakirja.
        //
        // Null when the minutes are not published yet, which is the ordinary state for a few
        // hours after a session and not an error: the caller should ask again later.
        //
        // Costs one search (the capped verb) and one document fetch. Finding the document takes
        // a search because the by-identifier lookup returns at most ten documents per
        // identifier, cannot page, and usually leaves the full minutes out.
        Task<Poytakirja?> GetSessionMinutesAsync(
            int vpYear, int sessionNumber, CancellationToken cancellationToken = default);

        // A committee report, "HaVM 28/2025 vp", found through the matter it was written on,
        // "HE 16/2025 vp". See Mietinto.
        //
        // Through the matter because that is the only exact route: the matter lists its
        // documents with their edktunnus, while looking a report's identifier up directly is a
        // fuzzy search that returns other years' reports of the same number. Two GETs; null when
        // the matter does not list the report.
        Task<Mietinto?> GetCommitteeReportAsync(
            string matterId, string reportId, CancellationToken cancellationToken = default);
    }
}
