using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    // Exercises the per-MP derivation against the real captured vote (2026-60-1), which
    // carries all 199 ballots — the property that makes this feasible at all.
    [TestClass]
    public class MpActivityServiceTests
    {
        private static async Task<Aanestys> RealVoteAsync()
        {
            var client = new EduskuntaClient(
                new HttpClient(new StubHttpMessageHandler(Fixtures.Vote2026_60_1)));
            return (await client.GetVoteAsync("2026-60-1"))!;
        }

        // Pauli Aalto-Setälä (1504) is recorded Poissa in the fixture.
        private const int AbsentMp = 1504;

        // ── ExtractVotesFor ───────────────────────────────────────────────────

        [TestMethod]
        public async Task ExtractVotesFor_FindsTheMpsOwnBallot()
        {
            var vote = await RealVoteAsync();

            var votes = MpActivityService.ExtractVotesFor(AbsentMp, new[] { vote });

            Assert.AreEqual(1, votes.Count);
            Assert.AreEqual("2026-60-1", votes[0].VoteId);
            Assert.AreEqual(VoteChoice.Poissa, votes[0].Choice);
            Assert.AreEqual("kok", votes[0].PartyAbbreviation?.Fi);
        }

        [TestMethod]
        public async Task ExtractVotesFor_UsesAgendaSubjectNotBallotOptions()
        {
            var vote = await RealVoteAsync();

            var votes = MpActivityService.ExtractVotesFor(AbsentMp, new[] { vote });

            // Subject is the matter being decided...
            StringAssert.Contains(votes[0].Subject?.Fi, "eläinten hyvinvoinnista");
            // ...while the ballot title only describes the options.
            StringAssert.Contains(votes[0].BallotTitle?.Fi, "Käsittelyn pohja");
            Assert.AreEqual("HE 32/2026 vp", votes[0].DocumentId?.Fi);
        }

        [TestMethod]
        public async Task ExtractVotesFor_SkipsDivisionsTheMpIsAbsentFromEntirely()
        {
            var vote = await RealVoteAsync();

            // The Speaker (1109) presides and is omitted from the ballot list.
            var votes = MpActivityService.ExtractVotesFor(1109, new[] { vote });

            Assert.AreEqual(0, votes.Count,
                "the Speaker casts no ballot, so there is no row to show");
        }

        [TestMethod]
        public async Task ExtractVotesFor_UnknownMp_ReturnsEmpty()
        {
            var vote = await RealVoteAsync();

            var votes = MpActivityService.ExtractVotesFor(999999, new[] { vote });

            Assert.AreEqual(0, votes.Count);
        }

        // ── Summarize ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Summarize_CountsAnAbsence()
        {
            var vote = await RealVoteAsync();

            var summary = MpActivityService.Summarize(AbsentMp, new[] { vote });

            Assert.AreEqual(1, summary.TotalVotes);
            Assert.AreEqual(1, summary.Poissa);
            Assert.AreEqual(0, summary.Present);
            Assert.AreEqual(0.0, summary.AttendanceRate);
            Assert.AreEqual("Pauli Aalto-Setälä", summary.Name);
            Assert.AreEqual("kok", summary.Party?.Fi);
        }

        [TestMethod]
        public async Task Summarize_CountsAVoteCast()
        {
            var vote = await RealVoteAsync();
            // Pick any MP the fixture records as voting Jaa.
            var voter = vote.Aanestystapahtumat.First(b => b.Kayttaytyminen?.Fi == "Jaa");

            var summary = MpActivityService.Summarize(voter.Henkilonumero, new[] { vote });

            Assert.AreEqual(1, summary.TotalVotes);
            Assert.AreEqual(1, summary.Jaa);
            Assert.AreEqual(1, summary.Present);
            Assert.AreEqual(1.0, summary.AttendanceRate);
        }

        [TestMethod]
        public async Task Summarize_AcrossWindow_AggregatesPerMp()
        {
            var vote = await RealVoteAsync();
            var voter = vote.Aanestystapahtumat.First(b => b.Kayttaytyminen?.Fi == "Ei");

            // Same division three times stands in for a window of three.
            var summary = MpActivityService.Summarize(
                voter.Henkilonumero, new[] { vote, vote, vote });

            Assert.AreEqual(3, summary.TotalVotes);
            Assert.AreEqual(3, summary.Ei);
            Assert.AreEqual(1.0, summary.AttendanceRate);
        }

        [TestMethod]
        public async Task Summarize_TotalsMatchTheOfficialTallyAcrossAllMps()
        {
            // Summing every MP's summary must reproduce the vote's own aanestystulos —
            // the strongest available check that the derivation is faithful.
            var vote = await RealVoteAsync();
            var window = new[] { vote };

            int jaa = 0, ei = 0, poissa = 0, unknown = 0;
            foreach (var ballot in vote.Aanestystapahtumat)
            {
                var s = MpActivityService.Summarize(ballot.Henkilonumero, window);
                jaa += s.Jaa;
                ei += s.Ei;
                poissa += s.Poissa;
                unknown += s.Unknown;
            }

            Assert.AreEqual(vote.Aanestystulos!.Jaa, jaa);
            Assert.AreEqual(vote.Aanestystulos.Ei, ei);
            Assert.AreEqual(vote.Aanestystulos.Poissa, poissa);
            Assert.AreEqual(0, unknown, "every ballot value should map to a known VoteChoice");
        }

        [TestMethod]
        public void Summarize_EmptyWindow_ReportsNoAttendanceRateRatherThanZero()
        {
            var summary = MpActivityService.Summarize(1504, new List<Aanestys>());

            Assert.AreEqual(0, summary.TotalVotes);
            Assert.IsNull(summary.AttendanceRate,
                "no data must be distinguishable from 0% attendance");
        }

        [TestMethod]
        public async Task Summarize_ExcludesAnnulledDivisions()
        {
            var vote = await RealVoteAsync();
            var annulled = await RealVoteAsync();
            annulled.Aanestysmitatoity = true;

            var summary = MpActivityService.Summarize(AbsentMp, new[] { vote, annulled });

            Assert.AreEqual(1, summary.TotalVotes,
                "an annulled division carries no decision and must not be counted");
        }

        // ── ParseChoice ───────────────────────────────────────────────────────

        [DataTestMethod]
        [DataRow("Jaa", VoteChoice.Jaa)]
        [DataRow("Ei", VoteChoice.Ei)]
        [DataRow("Poissa", VoteChoice.Poissa)]
        [DataRow("Tyhjä", VoteChoice.Tyhja)]
        [DataRow("Tyhja", VoteChoice.Tyhja)]
        [DataRow("jaa", VoteChoice.Jaa)]
        public void ParseChoice_MapsKnownVocabulary(string value, VoteChoice expected)
        {
            Assert.AreEqual(expected, MpActivityService.ParseChoice(new LocalizedText { Fi = value }));
        }

        [TestMethod]
        public void ParseChoice_UnrecognisedOrMissing_IsUnknown()
        {
            Assert.AreEqual(VoteChoice.Unknown,
                MpActivityService.ParseChoice(new LocalizedText { Fi = "Jotain muuta" }));
            Assert.AreEqual(VoteChoice.Unknown, MpActivityService.ParseChoice(new LocalizedText()));
            Assert.AreEqual(VoteChoice.Unknown, MpActivityService.ParseChoice(null));
        }

        // ── End-to-end through the client ─────────────────────────────────────

        [TestMethod]
        public async Task GetRecentVotesForMpAsync_ReadsThroughTheClient()
        {
            var client = new EduskuntaClient(
                new HttpClient(new StubHttpMessageHandler(Fixtures.RecentVotes)));
            var service = new MpActivityService(client);

            // The trimmed recent-votes fixture keeps the first 3 ballots of each vote.
            var recent = await client.GetRecentVotesAsync();
            int mp = recent[0].Aanestystapahtumat[0].Henkilonumero;

            var votes = await service.GetRecentVotesForMpAsync(mp);

            Assert.IsTrue(votes.Count > 0);
            Assert.IsTrue(votes.All(v => !string.IsNullOrEmpty(v.VoteId)));
        }

        [TestMethod]
        public async Task GetRecentActivityAsync_ReadsThroughTheClient()
        {
            var client = new EduskuntaClient(
                new HttpClient(new StubHttpMessageHandler(Fixtures.RecentVotes)));
            var service = new MpActivityService(client);

            var recent = await client.GetRecentVotesAsync();
            int mp = recent[0].Aanestystapahtumat[0].Henkilonumero;

            var summary = await service.GetRecentActivityAsync(mp);

            Assert.AreEqual(mp, summary.Henkilonumero);
            Assert.IsTrue(summary.TotalVotes > 0);
        }
    }
}
