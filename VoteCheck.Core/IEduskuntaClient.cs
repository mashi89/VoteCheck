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
    }
}
