using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoteCheck.Core.Tests
{
    // Request-shaping tests: verify EduskuntaClient builds the URLs from design.md §3.1 and
    // handles HTTP outcomes. Deserialization against real payloads lives in
    // LiveResponseShapeTests.

    [TestClass]
    public class EduskuntaClientTests
    {
        private static EduskuntaClient CreateClient(StubHttpMessageHandler handler) =>
            new EduskuntaClient(new HttpClient(handler));

        [TestMethod]
        public async Task GetMpsAsync_RequestsExpectedUrl()
        {
            // The stub body used to be "[]", which is not what this endpoint returns. That is
            // how the deserialization bug survived: the only test touching this method fed it a
            // shape the live service never sends, and passed.
            var handler = new StubHttpMessageHandler("{\"kansanedustajat\":[]}");
            var client = CreateClient(handler);

            await client.GetMpsAsync();

            Assert.AreEqual("https://api.eduskunta.fi/api/v1/kansanedustajat", handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetMatterAsync_EscapesTheSlashInsideTheIdentifier()
        {
            // The whole identifier is one path segment. A literal slash in "113/2026" would
            // split it into two and 404 for the wrong reason — which is exactly what made this
            // endpoint look as though it did not exist.
            var handler = new StubHttpMessageHandler("{}");
            var client = CreateClient(handler);

            await client.GetMatterAsync("HE 113/2026 vp");

            // Asserted on the slash rather than the whole string: .NET renders %20 back as a
            // space when a Uri is turned into text, so an exact comparison would be testing
            // that display quirk instead of the thing that matters.
            StringAssert.StartsWith(
                handler.RequestedUrl, "https://api.eduskunta.fi/api/v1/valtiopaivaasiat/HE" );
            StringAssert.Contains( handler.RequestedUrl, "%2F2026" );
            Assert.IsFalse( handler.RequestedUrl!.Contains( "113/2026" ),
                "an unescaped slash would make the year a path segment of its own" );
        }

        [TestMethod]
        public async Task GetMatterAsync_ReturnsNull_WhenNoSuchMatter()
        {
            var client = CreateClient(new StubHttpMessageHandler("", HttpStatusCode.NotFound));

            Assert.IsNull(await client.GetMatterAsync("HE 99999/2026 vp"));
        }

        [TestMethod]
        public async Task GetMatterAsync_ReturnsNull_WhenTheIdentifierIsNotOne()
        {
            // Upstream answers 400, not 404, for anything failing its identifier pattern —
            // including a combined identifier like "LA 1, 18/2023 vp", which names two private
            // members' bills at once and has no single matter. Those exist in the archive, so
            // this has to be an answer rather than an exception: the caller records a miss only
            // when it gets one, and a throw here would retry the same identifier for ever.
            var client = CreateClient(new StubHttpMessageHandler(
                "{\"message\":\"Parameter is not valid\",\"status\":400}",
                HttpStatusCode.BadRequest));

            Assert.IsNull(await client.GetMatterAsync("LA 1, 18/2023 vp"));
        }

        [TestMethod]
        public async Task GetMatterAsync_DoesNotCallUpstreamForAnEmptyIdentifier()
        {
            var handler = new StubHttpMessageHandler("{}");
            var client = CreateClient(handler);

            Assert.IsNull(await client.GetMatterAsync("   "));
            Assert.IsNull(handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetMpAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("null");
            var client = CreateClient(handler);

            await client.GetMpAsync(1109);

            Assert.AreEqual("https://api.eduskunta.fi/api/v1/kansanedustajat/1109", handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetMpAsync_ReturnsNull_OnNotFound()
        {
            var client = CreateClient(new StubHttpMessageHandler("", HttpStatusCode.NotFound));

            var mp = await client.GetMpAsync(999999);

            Assert.IsNull(mp);
        }

        [TestMethod]
        public async Task GetVoteAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("null");
            var client = CreateClient(handler);

            await client.GetVoteAsync("2026-60-1");

            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/aanestykset/2026-60-1",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetVotesInSessionAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetVotesInSessionAsync("2026-60");

            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/istunnon-aanestykset/2026-60",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetVotesForMatterAsync_EscapesTunnus()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetVotesForMatterAsync("HE 32/2026 vp");

            // Uri.ToString() unescapes %20 back to a literal space for display while leaving
            // %2F intact; the request line on the wire still uses the escaped form.
            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/asian-aanestykset/HE 32%2F2026 vp",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetRecentVotesAsync_RequestsExpectedUrl()
        {
            var handler = new StubHttpMessageHandler("[]");
            var client = CreateClient(handler);

            await client.GetRecentVotesAsync();

            Assert.AreEqual(
                "https://api.eduskunta.fi/api/v1/taysistunnot/uusimmat-aanestykset",
                handler.RequestedUrl);
        }

        [TestMethod]
        public async Task GetMpsAsync_ThrowsOnServerError()
        {
            var client = CreateClient(new StubHttpMessageHandler("", HttpStatusCode.InternalServerError));

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetMpsAsync());
        }

        [TestMethod]
        public async Task GetRecentVotesAsync_ReturnsEmpty_WhenBodyIsNull()
        {
            var client = CreateClient(new StubHttpMessageHandler("null"));

            var votes = await client.GetRecentVotesAsync();

            Assert.AreEqual(0, votes.Count);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Shape tests against REAL captured api.eduskunta.fi responses.
    //
    // These are the tests that matter for the migration: they pin the models to actual
    // upstream output. Several fields the initial scaffold modeled as plain strings turned
    // out to be bilingual objects, and uusimmat-aanestykset turned out to be nested — both
    // caught here.
    // ═══════════════════════════════════════════════════════════════════════════

    [TestClass]
    public class LiveResponseShapeTests
    {
        private static EduskuntaClient ClientReturning(string body) =>
            new EduskuntaClient(new HttpClient(new StubHttpMessageHandler(body)));

        // ── Kansanedustaja ────────────────────────────────────────────────────

        [TestMethod]
        public async Task MpList_DeserializesTheEnvelopeRatherThanABareArray()
        {
            // Every other list endpoint here returns an array; this one wraps its records in an
            // object. Asking for a bare List<Mp> binds to nothing against the live service, and
            // the call had never worked — it went unseen because the product does not use it and
            // no fixture existed, so the tests only ever proved the stub could return "[]".
            var mps = await ClientReturning(Fixtures.MpList).GetMpsAsync();

            Assert.AreEqual(2, mps.Count);
            Assert.IsTrue(mps.All(m => m.Henkilonro > 0), "records bound, not defaults");
            Assert.IsTrue(mps.Any(m => !string.IsNullOrWhiteSpace(m.Sukunimi)));
        }

        [TestMethod]
        public async Task MpList_CarriesFormerMembersAsWellAsSitting()
        {
            // The fixture keeps one of each on purpose. The endpoint returns a slice of the
            // whole history of the chamber, so a caller that treats the result as "parliament
            // today" is wrong — and this is the assertion that says so out loud.
            var mps = await ClientReturning(Fixtures.MpList).GetMpsAsync();

            CollectionAssert.AreEquivalent(
                new[] { "Nykyinen", "Entinen" },
                mps.Select(m => m.EdustajantoimenTila.ToString()).ToArray());
        }

        [TestMethod]
        public async Task Mp_DeserializesCoreIdentityFields()
        {
            var mp = await ClientReturning(Fixtures.Mp1109).GetMpAsync(1109);

            Assert.IsNotNull(mp);
            // henkilonro arrives as the JSON string "1109" and is coerced to int.
            Assert.AreEqual(1109, mp!.Henkilonro);
            Assert.AreEqual("Halla-aho", mp.Sukunimi);
            Assert.AreEqual("Jussi Kristian", mp.Etunimet);
            Assert.AreEqual("Jussi", mp.Kutsumanimi);
            Assert.AreEqual("Helsinki", mp.Kotikunta);
            Assert.AreEqual(1971, mp.Syntymavuosi);
            Assert.IsNull(mp.Kuolemavuosi);
            Assert.AreEqual("Jussi Halla-aho", mp.DisplayName);
        }

        [TestMethod]
        public async Task Mp_DeserializesTrilingualProfession()
        {
            var mp = await ClientReturning(Fixtures.Mp1109).GetMpAsync(1109);

            Assert.AreEqual("filosofian tohtori", mp!.Ammatti?.Fi);
            Assert.AreEqual("filosofie doktor", mp.Ammatti?.Sv);
            Assert.AreEqual("Doctor of Philosophy", mp.Ammatti?.En);
        }

        [TestMethod]
        public async Task Mp_DeserializesSeatStatusEnum()
        {
            var mp = await ClientReturning(Fixtures.Mp1109).GetMpAsync(1109);

            Assert.AreEqual(Models.EdustajantoimenTila.Nykyinen, mp!.EdustajantoimenTila);
        }

        [TestMethod]
        public async Task Mp_DeserializesCurrentGroupAndDistrictAsTypedMemberships()
        {
            var mp = await ClientReturning(Fixtures.Mp1109).GetMpAsync(1109);

            Assert.AreEqual(
                "Perussuomalaisten eduskuntaryhmä", mp!.ViimeisinEduskuntaryhma?.Nimi?.Fi);
            Assert.AreEqual(
                "The Finns Party Parliamentary Group", mp.ViimeisinEduskuntaryhma?.Nimi?.En);
            Assert.IsTrue(mp.ViimeisinEduskuntaryhma!.IsCurrent,
                "loppupvm is null upstream, so the membership is current");

            Assert.AreEqual("Helsingin vaalipiiri", mp.ViimeisinVaalipiiri?.Nimi?.Fi);
            Assert.AreEqual("HEL02", mp.ViimeisinVaalipiiri?.Tunnus);
        }

        [TestMethod]
        public async Task Mp_DeserializesMembershipHistories()
        {
            var mp = await ClientReturning(Fixtures.Mp1109).GetMpAsync(1109);

            Assert.AreEqual(3, mp!.Eduskuntaryhmat.Count);
            Assert.AreEqual(2, mp.Vaalipiirit.Count);
            // The earliest group membership is a closed period.
            Assert.AreEqual("2011-04-20", mp.Eduskuntaryhmat[0].Alkupvm);
            Assert.AreEqual("2014-06-30", mp.Eduskuntaryhmat[0].Loppupvm);
            Assert.IsFalse(mp.Eduskuntaryhmat[0].IsCurrent);
        }

        // ── Aanestys ──────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Vote_DeserializesIdentityAndTimestamps()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            Assert.IsNotNull(vote);
            Assert.AreEqual("2026-60-1", vote!.Id);
            Assert.AreEqual("2026-60", vote.IstunnonTunniste);
            Assert.AreEqual("2026", vote.Istuntovpvuosi);
            Assert.AreEqual("60", vote.Istuntonumero);
            Assert.AreEqual("1", vote.Aanestysnumero);
            Assert.IsFalse(vote.Aanestysmitatoity);
            // Istuntopvm is a date+offset ("2026-06-03+03:00"), deliberately kept as a string.
            Assert.AreEqual("2026-06-03+03:00", vote.Istuntopvm);
        }

        [TestMethod]
        public async Task Vote_DeserializesBilingualTitles()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            StringAssert.Contains(vote!.Aanestysotsikko?.Fi, "Käsittelyn pohja");
            StringAssert.Contains(vote.Aanestysotsikko?.Sv, "Grund för behandlingen");
            StringAssert.Contains(vote.Paivajarjestyksenotsikko?.Fi, "Keskiviikko");
            // Vote payloads carry fi/sv only — no en.
            Assert.IsNull(vote.Aanestysotsikko?.En);
        }

        [TestMethod]
        public async Task Vote_DeserializesTally()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            Assert.AreEqual(143, vote!.Aanestystulos?.Jaa);
            Assert.AreEqual(21, vote.Aanestystulos?.Ei);
            Assert.AreEqual(0, vote.Aanestystulos?.Tyhjia);
            Assert.AreEqual(35, vote.Aanestystulos?.Poissa);
            Assert.AreEqual(199, vote.Aanestystulos?.Yhteensa);
        }

        [TestMethod]
        public async Task Vote_DeserializesAgendaItemSubject()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            // Kohta.Otsikko is the subject of the vote (aanestysotsikko is only the options).
            StringAssert.Contains(vote!.Kohta?.Otsikko?.Fi, "eläinten hyvinvoinnista");
            Assert.AreEqual("Ensimmäinen käsittely", vote.Kohta?.Kasittelyotsikkonimi?.Fi);
            Assert.AreEqual("HE 32/2026 vp", vote.Kohta?.Asiakirjat?.PaaasiakirjaEduskuntatunnus?.Fi);
            Assert.AreEqual("HE", vote.Kohta?.Asiakirjat?.PaaasiakirjaAsiatyyppi);
        }

        [TestMethod]
        public async Task Vote_DeserializesSpeaker()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            Assert.AreEqual(1109, vote!.Puhemies?.Henkilonumero);
            Assert.AreEqual("Halla-aho", vote.Puhemies?.Sukunimi);
            Assert.AreEqual("ps", vote.Puhemies?.Edkryhmalyhenne?.Fi);
            Assert.AreEqual("saf", vote.Puhemies?.Edkryhmalyhenne?.Sv);
        }

        [TestMethod]
        public async Task Vote_EmbedsFullBallotListForEverySeat()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            // This is what makes per-MP vote history feasible without a dedicated endpoint:
            // one entry per seat, on every vote payload.
            Assert.AreEqual(199, vote!.Aanestystapahtumat.Count);
        }

        [TestMethod]
        public async Task Vote_BallotFieldsAreBilingualObjectsNotStrings()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            var ballot = vote!.Aanestystapahtumat[0];
            Assert.AreEqual(1504, ballot.Henkilonumero);
            Assert.AreEqual("Aalto-Setälä", ballot.Sukunimi);
            Assert.AreEqual("Pauli", ballot.Etunimi);
            Assert.AreEqual("kok", ballot.Edkryhmalyhenne?.Fi);
            Assert.AreEqual("saml", ballot.Edkryhmalyhenne?.Sv);
            Assert.AreEqual("Poissa", ballot.Kayttaytyminen?.Fi);
            Assert.AreEqual("Frånvarande", ballot.Kayttaytyminen?.Sv);
            Assert.AreEqual("Varsinais-Suomen vaalipiiri", ballot.Vaalipiiri?.Fi);
            Assert.AreEqual("Kansallisen kokoomuksen eduskuntaryhmä", ballot.Eduskuntaryhma?.Fi);
        }

        [TestMethod]
        public async Task Vote_BallotsUseExpectedVoteVocabulary()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            var observed = vote!.Aanestystapahtumat
                .Select(b => b.Kayttaytyminen?.Fi)
                .Distinct()
                .OrderBy(v => v)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "Ei", "Jaa", "Poissa" }, observed);
        }

        [TestMethod]
        public async Task Vote_BallotTallyMatchesReportedResult()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            // Cross-check that deriving counts from ballots agrees with aanestystulos —
            // the same derivation the per-MP activity summary will rely on.
            int jaa = vote!.Aanestystapahtumat.Count(b => b.Kayttaytyminen?.Fi == "Jaa");
            int ei = vote.Aanestystapahtumat.Count(b => b.Kayttaytyminen?.Fi == "Ei");
            int poissa = vote.Aanestystapahtumat.Count(b => b.Kayttaytyminen?.Fi == "Poissa");

            Assert.AreEqual(vote.Aanestystulos!.Jaa, jaa);
            Assert.AreEqual(vote.Aanestystulos.Ei, ei);
            Assert.AreEqual(vote.Aanestystulos.Poissa, poissa);
        }

        [TestMethod]
        public async Task Vote_DeserializesPartyDistributionWithBilingualName()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            var kok = vote!.EduskuntaryhmaJakaumat[0];
            Assert.AreEqual("Kansallisen kokoomuksen eduskuntaryhmä", kok.Nimi?.Fi);
            Assert.AreEqual("Samlingspartiets riksdagsgrupp", kok.Nimi?.Sv);
            Assert.AreEqual(38, kok.Jaa);
            Assert.AreEqual(10, kok.Poissa);
            Assert.AreEqual(48, kok.Yhteensa);
        }

        [TestMethod]
        public async Task Vote_DeserializesGovernmentOppositionSplit()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            Assert.AreEqual(2, vote!.HallitusoppositioJakaumat.Count);
            Assert.AreEqual("Hallitusryhmät", vote.HallitusoppositioJakaumat[0].Nimi?.Fi);
            Assert.AreEqual("Oppositioryhmät", vote.HallitusoppositioJakaumat[1].Nimi?.Fi);
            Assert.AreEqual(107, vote.HallitusoppositioJakaumat[0].Yhteensa);
            Assert.AreEqual(92, vote.HallitusoppositioJakaumat[1].Yhteensa);
        }

        [TestMethod]
        public async Task Vote_DeserializesElectoralDistrictDistribution()
        {
            var vote = await ClientReturning(Fixtures.Vote2026_60_1).GetVoteAsync("2026-60-1");

            Assert.IsTrue(vote!.VaalipiiriJakaumat.Count > 0);
            Assert.AreEqual("Helsingin vaalipiiri", vote.VaalipiiriJakaumat[0].Nimi?.Fi);
            Assert.AreEqual(22, vote.VaalipiiriJakaumat[0].Yhteensa);
        }

        // ── uusimmat-aanestykset (nested array) ───────────────────────────────

        [TestMethod]
        public async Task RecentVotes_FlattensNestedArrayResponse()
        {
            // Upstream returns [[vote], [vote], ...] rather than [vote, vote, ...].
            var votes = await ClientReturning(Fixtures.RecentVotes).GetRecentVotesAsync();

            Assert.AreEqual(2, votes.Count);
            Assert.AreEqual("2026-60-1", votes[0].Id);
            Assert.AreEqual("2026-65-4", votes[1].Id);
        }

        [TestMethod]
        public async Task RecentVotes_CarryBallotsAndDistributions()
        {
            var votes = await ClientReturning(Fixtures.RecentVotes).GetRecentVotesAsync();

            // Fixture is trimmed to 3 ballots per vote; the live response carries all 199.
            Assert.AreEqual(3, votes[0].Aanestystapahtumat.Count);
            Assert.IsTrue(votes[0].EduskuntaryhmaJakaumat.Count > 0);
            Assert.IsNotNull(votes[0].Aanestystulos);
        }
    }
}
