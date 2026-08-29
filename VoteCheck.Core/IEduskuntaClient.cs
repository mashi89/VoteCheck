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
    }
}
