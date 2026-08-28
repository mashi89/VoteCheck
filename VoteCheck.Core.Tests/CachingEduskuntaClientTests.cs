using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    [TestClass]
    public class CachingEduskuntaClientTests
    {
        private static MemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

        [TestMethod]
        public async Task RepeatedVoteFetch_HitsUpstreamOnce()
        {
            var inner = new FakeEduskuntaClient();
            var client = new CachingEduskuntaClient(inner, NewCache());

            await client.GetVoteAsync("2026-60-1");
            await client.GetVoteAsync("2026-60-1");
            await client.GetVoteAsync("2026-60-1");

            Assert.AreEqual(1, inner.VoteCalls);
        }

        [TestMethod]
        public async Task DifferentVoteIds_AreCachedSeparately()
        {
            var inner = new FakeEduskuntaClient();
            var client = new CachingEduskuntaClient(inner, NewCache());

            await client.GetVoteAsync("2026-60-1");
            await client.GetVoteAsync("2026-65-4");
            await client.GetVoteAsync("2026-60-1");

            Assert.AreEqual(2, inner.VoteCalls);
        }

        [TestMethod]
        public async Task CachedValue_IsReturnedNotJustSuppressed()
        {
            var inner = new FakeEduskuntaClient
            {
                Vote = new Aanestys { Id = "2026-60-1" }
            };
            var client = new CachingEduskuntaClient(inner, NewCache());

            var first = await client.GetVoteAsync("2026-60-1");
            var second = await client.GetVoteAsync("2026-60-1");

            Assert.IsNotNull(second);
            Assert.AreEqual("2026-60-1", second!.Id);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public async Task EachEndpointHasItsOwnCacheKey()
        {
            var inner = new FakeEduskuntaClient();
            var client = new CachingEduskuntaClient(inner, NewCache());

            await client.GetMpsAsync();
            await client.GetRecentVotesAsync();
            await client.GetVotesInSessionAsync("2026-60");
            await client.GetVotesForMatterAsync("HE 32/2026 vp");
            await client.GetMpAsync(1109);

            // Repeat all of them; nothing should reach upstream a second time.
            await client.GetMpsAsync();
            await client.GetRecentVotesAsync();
            await client.GetVotesInSessionAsync("2026-60");
            await client.GetVotesForMatterAsync("HE 32/2026 vp");
            await client.GetMpAsync(1109);

            Assert.AreEqual(1, inner.MpsCalls);
            Assert.AreEqual(1, inner.RecentVotesCalls);
            Assert.AreEqual(1, inner.SessionVotesCalls);
            Assert.AreEqual(1, inner.MatterVotesCalls);
            Assert.AreEqual(1, inner.MpCalls);
        }

        [TestMethod]
        public async Task ExpiredEntry_IsRefetched()
        {
            var inner = new FakeEduskuntaClient();
            var client = new CachingEduskuntaClient(
                inner, NewCache(),
                immutableTtl: TimeSpan.FromMilliseconds(1),
                volatileTtl: TimeSpan.FromMilliseconds(1));

            await client.GetVoteAsync("2026-60-1");
            await Task.Delay(60);
            await client.GetVoteAsync("2026-60-1");

            Assert.AreEqual(2, inner.VoteCalls);
        }

        [TestMethod]
        public async Task NullResult_IsNotCached()
        {
            // A 404 today may be a real record tomorrow; caching the miss would pin it.
            var inner = new FakeEduskuntaClient { Mp = null };
            var client = new CachingEduskuntaClient(inner, NewCache());

            await client.GetMpAsync(999999);
            await client.GetMpAsync(999999);

            Assert.AreEqual(2, inner.MpCalls);
        }

        [TestMethod]
        public async Task ConcurrentCallersForSameKey_ShareOneUpstreamFetch()
        {
            var gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var inner = new FakeEduskuntaClient { Gate = gate };
            var client = new CachingEduskuntaClient(inner, NewCache());

            // Fire several concurrent requests while the upstream fetch is held open.
            var pending = Enumerable.Range(0, 5)
                .Select(_ => client.GetVoteAsync("2026-60-1"))
                .ToArray();

            gate.SetResult(true);
            await Task.WhenAll(pending);

            Assert.AreEqual(1, inner.VoteCalls, "concurrent callers must collapse onto one fetch");
            Assert.IsTrue(pending.All(t => t.Result is not null));
        }

        [TestMethod]
        public async Task FetchesAfterAnInFlightOneCompletes_ComeFromCache()
        {
            var inner = new FakeEduskuntaClient();
            var client = new CachingEduskuntaClient(inner, NewCache());

            await client.GetVoteAsync("2026-60-1");
            // The in-flight bookkeeping must not leak: a later call still hits the cache.
            await client.GetVoteAsync("2026-60-1");

            Assert.AreEqual(1, inner.VoteCalls);
        }
    }
}
