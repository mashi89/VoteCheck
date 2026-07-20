using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoteCheck.Core.Tests
{
    // These tests verify EduskuntaClient's request/deserialization mechanics against
    // hand-written fixtures matching the *documented* shape of api.eduskunta.fi (see
    // design.md §3.1). Unlike VoteCollectorTests' fixtures (captured from the live legacy
    // API), these have not been checked against a real response — see the caveat atop
    // EduskuntaClient.cs.

    [TestClass]
    public class EduskuntaClientTests
    {
        private static EduskuntaClient CreateClient(StubHttpMessageHandler handler) =>
            new EduskuntaClient(new HttpClient(handler));

        [TestMethod]
        public async Task GetMpsAsync_DeserializesListOfMps()
        {
            const string json = @"[
                { ""henkilonro"": 1109, ""etunimet"": ""Jussi"", ""sukunimi"": ""Halla-aho"", ""kutsumanimi"": ""Jussi"" },
                { ""henkilonro"": 1155, ""etunimet"": ""Anna"", ""sukunimi"": ""Kontula"", ""kutsumanimi"": ""Anna"" }
            ]";
            var client = CreateClient(new StubHttpMessageHandler(json));

            var mps = await client.GetMpsAsync();

            Assert.AreEqual(2, mps.Count);
            Assert.AreEqual(1109, mps[0].Henkilonro);
            Assert.AreEqual("Halla-aho", mps[0].Sukunimi);
            Assert.AreEqual("Jussi Halla-aho", mps[0].DisplayName);
        }

        [TestMethod]
        public async Task GetMpsAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetMpsAsync();

            Assert.AreEqual("https://api.eduskunta.fi/api/v1/kansanedustajat", handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetMpAsync_ReturnsNull_OnNotFound()
        {
            var client = CreateClient(new StubHttpMessageHandler("", HttpStatusCode.NotFound));

            var mp = await client.GetMpAsync(999999);

            Assert.IsNull(mp);
        }

        [TestMethod]
        public async Task GetVoteAsync_UrlEscapesTunnus()
        {
            var handler = new StubHttpMessageHandler("null");
            var client = CreateClient(handler);

            await client.GetVoteAsync("2023-12-3");

            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/aanestykset/2023-12-3",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetVoteAsync_DeserializesTallyBallotsAndDistributions()
        {
            const string json = @"{
                ""id"": ""2023-12-3"",
                ""istunnonTunniste"": ""2023-12"",
                ""aanestysotsikko"": { ""fi"": ""Esimerkki"", ""sv"": ""Exempel"" },
                ""aanestysmitatoity"": false,
                ""aanestystulos"": { ""jaa"": 100, ""ei"": 50, ""tyhjia"": 2, ""poissa"": 47, ""yhteensa"": 199 },
                ""aanestystapahtumat"": [
                    { ""henkilonumero"": 1109, ""sukunimi"": ""Halla-aho"", ""etunimi"": ""Jussi"",
                      ""edkryhmalyhenne"": ""ps"", ""kayttaytyminen"": ""Jaa"" }
                ],
                ""eduskuntaryhmaJakaumat"": [
                    { ""nimi"": ""ps"", ""jaa"": 40, ""ei"": 0, ""tyhjia"": 0, ""poissa"": 2, ""yhteensa"": 42 }
                ],
                ""hallitusoppositioJakaumat"": [
                    { ""nimi"": ""Hallitus"", ""jaa"": 60, ""ei"": 10, ""tyhjia"": 1, ""poissa"": 20, ""yhteensa"": 91 }
                ]
            }";
            var client = CreateClient(new StubHttpMessageHandler(json));

            var vote = await client.GetVoteAsync("2023-12-3");

            Assert.IsNotNull(vote);
            Assert.AreEqual("Esimerkki", vote!.Aanestysotsikko?.Fi);
            Assert.AreEqual("Exempel", vote.Aanestysotsikko?.Sv);
            Assert.AreEqual(100, vote.Aanestystulos?.Jaa);
            Assert.AreEqual(199, vote.Aanestystulos?.Yhteensa);
            Assert.AreEqual(1, vote.Aanestystapahtumat.Count);
            Assert.AreEqual("Halla-aho", vote.Aanestystapahtumat[0].Sukunimi);
            Assert.AreEqual("Jaa", vote.Aanestystapahtumat[0].Kayttaytyminen);
            Assert.AreEqual(1, vote.EduskuntaryhmaJakaumat.Count);
            Assert.AreEqual("ps", vote.EduskuntaryhmaJakaumat[0].Nimi);
            Assert.AreEqual(1, vote.HallitusoppositioJakaumat.Count);
            Assert.AreEqual("Hallitus", vote.HallitusoppositioJakaumat[0].Nimi);
        }

        [TestMethod]
        public async Task GetVotesInSessionAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetVotesInSessionAsync("2023-12");

            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/istunnon-aanestykset/2023-12",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetVotesForMatterAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetVotesForMatterAsync("HE 1/2023");

            // Uri.ToString() unescapes %20 back to a literal space for display while leaving
            // %2F intact; the request line on the wire still uses the escaped form.
            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/asian-aanestykset/HE 1%2F2023",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetRecentVotesAsync_DeserializesList()
        {
            const string json = @"[ { ""id"": ""2023-12-3"" }, { ""id"": ""2023-12-4"" } ]";
            var client = CreateClient(new StubHttpMessageHandler(json));

            var votes = await client.GetRecentVotesAsync();

            Assert.AreEqual(2, votes.Count);
            Assert.AreEqual("2023-12-3", votes[0].Id);
        }

        [TestMethod]
        [ExpectedException(typeof(HttpRequestException))]
        public async Task GetMpsAsync_ThrowsOnServerError()
        {
            var client = CreateClient(new StubHttpMessageHandler("", HttpStatusCode.InternalServerError));

            await client.GetMpsAsync();
        }
    }
}
