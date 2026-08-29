using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoteCheck.Core;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    // Counting in-memory IEduskuntaClient, for exercising the caching decorator without HTTP.
    internal sealed class FakeEduskuntaClient : IEduskuntaClient
    {
        public int MpsCalls { get; private set; }
        public int MpCalls { get; private set; }
        public int VoteCalls { get; private set; }
        public int SessionVotesCalls { get; private set; }
        public int MatterVotesCalls { get; private set; }
        public int RecentVotesCalls { get; private set; }
        public int VotePageCalls { get; private set; }

        public IReadOnlyList<Mp> Mps { get; set; } = new List<Mp>();
        public Mp? Mp { get; set; } = new Mp { Henkilonro = 1109 };
        public Aanestys? Vote { get; set; } = new Aanestys { Id = "2026-60-1" };
        public IReadOnlyList<Aanestys> Votes { get; set; } = new List<Aanestys>();

        // Optional gate so tests can hold a fetch open and prove concurrent callers collapse
        // onto a single upstream request.
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<IReadOnlyList<Mp>> GetMpsAsync(CancellationToken cancellationToken = default)
        {
            MpsCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Mps;
        }

        public async Task<Mp?> GetMpAsync(int henkilonumero, CancellationToken cancellationToken = default)
        {
            MpCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Mp;
        }

        public async Task<Aanestys?> GetVoteAsync(
            string aanestystunnus, CancellationToken cancellationToken = default)
        {
            VoteCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Vote;
        }

        public async Task<IReadOnlyList<Aanestys>> GetVotesInSessionAsync(
            string istuntotunnus, CancellationToken cancellationToken = default)
        {
            SessionVotesCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Votes;
        }

        public async Task<IReadOnlyList<Aanestys>> GetVotesForMatterAsync(
            string eduskuntatunnus, CancellationToken cancellationToken = default)
        {
            MatterVotesCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Votes;
        }

        public async Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(
            CancellationToken cancellationToken = default)
        {
            RecentVotesCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return Votes;
        }

        public async Task<VotePage> GetVotePageAsync(
            int fromVpYear,
            int startFromIndex,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            VotePageCalls++;
            await WaitForGateAsync().ConfigureAwait(false);
            return new VotePage { Votes = Votes, TotalCount = Votes.Count, StartIndex = startFromIndex };
        }

        private Task WaitForGateAsync() => Gate?.Task ?? Task.CompletedTask;
    }
}
