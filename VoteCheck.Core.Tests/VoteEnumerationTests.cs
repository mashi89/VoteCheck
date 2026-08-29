using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace VoteCheck.Core.Tests
{
    // Covers archive enumeration (design.md §7 step 2). The vote endpoints answer by
    // identifier only, so history has to come through the search index; these tests pin
    // both the query we send and our parsing of a real captured response.

    [TestClass]
    public class VoteEnumerationTests
    {
        private static EduskuntaClient CreateClient(StubHttpMessageHandler handler) =>
            new EduskuntaClient(new HttpClient(handler));

        [TestMethod]
        public async Task GetVotePageAsync_PostsToSearchEndpoint()
        {
            var handler = new StubHttpMessageHandler(Fixtures.VoteSearch);

            await CreateClient(handler).GetVotePageAsync(2023, 0, 50);

            Assert.AreEqual("https://api.eduskunta.fi/api/v1/search", handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetVotePageAsync_FiltersByParliamentaryYear_NotSittingDate()
        {
            // The two disagree: 2023 onward is ~2,771 divisions by parliamentary year but
            // ~1,875 by calendar date, and the year is what SyncMinYear has always meant.
            var handler = new StubHttpMessageHandler(Fixtures.VoteSearch);

            await CreateClient(handler).GetVotePageAsync(2023, 0, 50);

            var body = JObject.Parse(handler.RequestBody!);
            Assert.AreEqual("aanestys", (string?)body["category"]);
            Assert.AreEqual("istuntovpvuosi", (string?)body["expression"]!["property"]);
            Assert.AreEqual(2023, (int?)body["expression"]!["from"]);
        }

        [TestMethod]
        public async Task GetVotePageAsync_SortsAscending_SoAStoredCursorStaysValid()
        {
            // Descending order would renumber every index whenever parliament sits, breaking
            // a resumable backfill; ascending appends new divisions past the cursor.
            var handler = new StubHttpMessageHandler(Fixtures.VoteSearch);

            await CreateClient(handler).GetVotePageAsync(2023, 0, 50);

            var sort = JObject.Parse(handler.RequestBody!)["sort"]![0]!;
            Assert.AreEqual("istuntopvm", (string?)sort["property"]);
            Assert.IsTrue((bool)sort["ascending"]!);
        }

        [TestMethod]
        public async Task GetVotePageAsync_UnwrapsCategoryEnvelopes()
        {
            // Search returns one envelope per hit with a slot per category, not a bare vote.
            var page = await CreateClient(new StubHttpMessageHandler(Fixtures.VoteSearch))
                .GetVotePageAsync(2023, 0, 50);

            Assert.AreEqual(2, page.Votes.Count);
            Assert.AreEqual("2023-14-1", page.Votes[0].Id);
            Assert.AreEqual("2023-19-5", page.Votes[1].Id);
            CollectionAssert.AllItemsAreNotNull(new[] { page.Votes[0].Kohta, page.Votes[1].Kohta });
        }

        [TestMethod]
        public async Task GetVotePageAsync_ReportsTotalAndCursorForResuming()
        {
            var page = await CreateClient(new StubHttpMessageHandler(Fixtures.VoteSearch))
                .GetVotePageAsync(2023, 0, 50);

            Assert.AreEqual(2771, page.TotalCount, "total is the archive size, not the page size");
            Assert.AreEqual(0, page.StartIndex);
            Assert.IsTrue(page.HasMore);
        }

        [TestMethod]
        public async Task GetVotePageAsync_ResultsAreOrderedOldestFirst()
        {
            var page = await CreateClient(new StubHttpMessageHandler(Fixtures.VoteSearch))
                .GetVotePageAsync(2023, 0, 50);

            StringAssert.StartsWith(page.Votes[0].Istuntopvm, "2023-06-20");
            StringAssert.StartsWith(page.Votes[1].Istuntopvm, "2023-06-28");
        }

        [TestMethod]
        public void GetVotePageAsync_RejectsPagingBeyondUpstreamWindow()
        {
            // Upstream 400s once startFromIndex + maxResults passes 10,000; failing here
            // gives a caller an actionable message instead of an opaque HTTP error.
            var client = CreateClient(new StubHttpMessageHandler(Fixtures.VoteSearch));

            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => client.GetVotePageAsync(2023, EduskuntaClient.MaxSearchWindow, 1).GetAwaiter().GetResult());
        }

        [TestMethod]
        public async Task GetVotePageAsync_ToleratesMissingSpeakerNumber()
        {
            // Two divisions in the archive record the Speaker's henkilonumero as "-".
            // A non-nullable int property throws on those, taking the whole page down;
            // 2023-14-1 in this fixture is one of them.
            var page = await CreateClient(new StubHttpMessageHandler(Fixtures.VoteSearch))
                .GetVotePageAsync(2023, 0, 50);

            Assert.AreEqual("2023-14-1", page.Votes[0].Id);
            Assert.IsNull(page.Votes[0].Puhemies?.Henkilonumero);
        }

        [TestMethod]
        public async Task Requests_CarryAUserAgent()
        {
            // api.eduskunta.fi answers 403 to any request without one, on every endpoint,
            // and HttpClient sends none by default. Stubbed tests cannot catch this, so it
            // is pinned explicitly here.
            var handler = new StubHttpMessageHandler(Fixtures.VoteSearch);

            await CreateClient(handler).GetVotePageAsync(2023, 0, 50);

            Assert.IsNotNull(handler.RequestUserAgent, "no User-Agent would mean 403 upstream");
        }

        [TestMethod]
        public async Task GetVotePageAsync_IsNotCached()
        {
            // Enumeration is a one-pass bulk read; caching it would evict the hot entries
            // the decorator exists to hold.
            var inner = new FakeEduskuntaClient();
            var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
            var client = new CachingEduskuntaClient(inner, cache);

            await client.GetVotePageAsync(2023, 0, 50);
            await client.GetVotePageAsync(2023, 0, 50);

            Assert.AreEqual(2, inner.VotePageCalls);
        }
    }
}
