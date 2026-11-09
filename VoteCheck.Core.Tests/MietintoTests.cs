using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    // The texts a vote refers to without stating, read out of the committee report it was
    // decided on: HE 16/2025 vp, its report HaVM 28/2025 vp, and the two sessions that voted on
    // its sections and on statements taken over from its dissenting opinions.
    [TestClass]
    public class MietintoTests
    {
        private static readonly Mietinto Report = Mietinto.Parse(Fixtures.ReportHaVM28_2025Trimmed);

        private static PoytakirjaAanestys Vote(string xml, string id) =>
            Poytakirja.Parse("EDK", xml).Aanestykset.Single(v => v.AanestysTunnus == id);

        [TestMethod]
        public void Report_HoldsTheCommitteesStatementsAndSections()
        {
            Assert.AreEqual(4, Report.Lausumat.Count);
            StringAssert.StartsWith(Report.Lausumat[1],
                "Eduskunta edellyttää, että valtioneuvosto selvittää mahdollisuudet peliestorekisteriin");
            Assert.AreEqual(1, Report.Lakeja);
            StringAssert.StartsWith(Report.Pykalat[(1, "24 §")], "24 § Rahapelaamisen ikäraja");
        }

        [TestMethod]
        public void Report_KeepsEachDissentApart()
        {
            CollectionAssert.AreEqual(new int?[] { 1, 2 }, Report.Vastalauseet.Select(v => v.Numero).ToArray());

            var first = Report.Vastalauseet[0];
            Assert.AreEqual(4, first.Lausumat.Count);
            StringAssert.StartsWith(first.Lausumat[0], "Eduskunta edellyttää, että hallitus seuraa uudistuksen vaikutuksia");
            CollectionAssert.AreEquivalent(
                new[] { "24 §", "25 §", "28 §" }, first.Pykalat.Keys.Select(k => k.Pykala).ToArray(),
                "the dissent's 24 § has no identifier attribute; its heading is what names it");

            Assert.AreEqual(2, Report.Vastalauseet[1].Lausumat.Count);
            Assert.AreEqual(0, Report.Vastalauseet[1].Pykalat.Count);
        }

        [TestMethod]
        public void StruckWords_AreNotPartOfTheWording()
        {
            // Dissent 1 marks its change to 24 § in the text: <PoistoTeksti>18 </PoistoTeksti>
            // <MuutosTeksti>20</MuutosTeksti>. Its wording is the one with 20, and only 20.
            var dissent = Report.Vastalauseet[0].Pykalat[(1, "24 §")];

            StringAssert.Contains(dissent, "ei saa antaa alle 20-vuotiaan pelata rahapelejä.");
            Assert.IsFalse(dissent.Contains("18"));
            StringAssert.Contains(Report.Pykalat[(1, "24 §")], "ei saa antaa alle 18-vuotiaan pelata rahapelejä.");
        }

        [TestMethod]
        public void Minutes_NameTheReportTheItemIsDecidedOn()
        {
            Assert.AreEqual("HaVM 28/2025 vp", Vote(Fixtures.MinutesPtk130_2025Trimmed, "2025-130-10").Mietinto);
        }

        [TestMethod]
        public void StatementTakenOverFromADissent_IsThatDissentsStatement()
        {
            // "Tiina Elo on … tehnyt vastalauseen 1 mukaiset neljä lausumaehdotusta", then
            // "Mietintö ”jaa”, Tiina Elon ehdotus 2 ”ei”": dissent 1's statement 2.
            var vote = Vote(Fixtures.MinutesPtk130_2025Trimmed, "2025-130-11");

            Assert.IsTrue(Report.Resolve(vote.Ei!));
            Assert.AreEqual("Vastalause 1", vote.Ei!.Kohta);
            Assert.AreEqual(Report.Vastalauseet[0].Lausumat[1], vote.Ei.Teksti);
            Assert.IsNull(vote.Jaa?.Viite, "voting for the report here keeps the statement out; there is no text to it");
        }

        [TestMethod]
        public void DissentNumbering_IsTheMembersOwnSource()
        {
            // Pekonen also proposed rejecting the laws, in words. Her "ehdotus 1" is still her
            // first statement from dissent 2, not that proposal.
            var vote = Vote(Fixtures.MinutesPtk130_2025Trimmed, "2025-130-14");

            Assert.IsTrue(Report.Resolve(vote.Ei!));
            Assert.AreEqual("Vastalause 2", vote.Ei!.Kohta);
            Assert.AreEqual(Report.Vastalauseet[1].Lausumat[0], vote.Ei.Teksti);
        }

        [TestMethod]
        public void TwoMembersDissentsInOneSentence_StayWithTheirOwnMember()
        {
            // As session 63/2026 words it: "Mehtälä on … tehnyt vastalauseen 2 mukaiset neljä
            // lausumaehdotusta ja edustaja Yrttiaho on … tehnyt vastalauseen 4 …", no comma. Read
            // as one clause, Mehtälä's statements were attributed to dissent 4.
            var xml = Fixtures.MinutesPtk130_2025Trimmed
                .Replace(
                    "Keskustelussa edustaja Tiina Elo on edustaja Hanna Holopaisen kannattamana tehnyt vastalauseen 1 mukaiset neljä lausumaehdotusta.",
                    "Keskustelussa edustaja Tiina Elo on edustaja Hanna Holopaisen kannattamana tehnyt vastalauseen 1 mukaiset neljä lausumaehdotusta ja edustaja Aino-Kaisa Pekonen on edustaja Veronika Honkasalon kannattamana tehnyt vastalauseen 2 mukaiset kaksi lausumaehdotusta.")
                .Replace(
                    "Lisäksi keskustelussa edustaja Aino-Kaisa Pekonen on edustaja Veronika Honkasalon kannattamana tehnyt vastalauseen 2 mukaiset kaksi lausumaehdotusta.",
                    "");

            var elo = Vote(xml, "2025-130-11");
            var pekonen = Vote(xml, "2025-130-14");

            Assert.AreEqual(1, elo.Ei?.Viite?.Vastalause);
            Assert.AreEqual(4, elo.Ei?.Viite?.Maara);
            Assert.AreEqual(2, pekonen.Ei?.Viite?.Vastalause);
            Assert.AreEqual(2, pekonen.Ei?.Viite?.Maara);
        }

        [TestMethod]
        public void ClauseWithTwoSeconders_IsNotRead()
        {
            // A join this does not recognise leaves two members' words in one clause. Reading it
            // would give one member the other's dissent, so nothing is taken from it.
            var xml = Fixtures.MinutesPtk130_2025Trimmed.Replace(
                "Keskustelussa edustaja Tiina Elo on edustaja Hanna Holopaisen kannattamana tehnyt vastalauseen 1 mukaiset neljä lausumaehdotusta.",
                "Keskustelussa edustaja Tiina Elo on edustaja Hanna Holopaisen kannattamana ehdottanut jotakin sekä edustaja Hanna Holopainen edustaja Krista Mikkosen kannattamana tehnyt vastalauseen 1 mukaiset neljä lausumaehdotusta.");

            Assert.IsNull(Vote(xml, "2025-130-11").Ei?.Viite);
        }

        [TestMethod]
        public void DissentWithADifferentNumberOfStatements_IsNotUsed()
        {
            // The minutes counted four. A report saying otherwise is not the report they meant,
            // or not read the way they meant it.
            var vote = Vote(Fixtures.MinutesPtk130_2025Trimmed, "2025-130-11");
            vote.Ei!.Viite!.Maara = 3;

            Assert.IsFalse(Report.Resolve(vote.Ei));
            Assert.AreEqual("", vote.Ei.Teksti);
        }

        [TestMethod]
        public void SectionVote_ComparesTheCommitteesWordingWithTheDissents()
        {
            var vote = Vote(Fixtures.MinutesPtk125_2025Trimmed, "2025-125-5");

            Assert.IsTrue(Report.Resolve(vote.Jaa!));
            Assert.IsTrue(Report.Resolve(vote.Ei!));
            Assert.AreEqual("1. lakiehdotus, 24 §", vote.Jaa!.Kohta);
            Assert.AreEqual("Vastalause 1: 1. lakiehdotus, 24 §", vote.Ei!.Kohta);
            StringAssert.StartsWith(vote.Jaa.Teksti, "24 § Rahapelaamisen ikäraja");
            StringAssert.StartsWith(vote.Ei.Teksti, "24 § Rahapelaamisen ikäraja");
            Assert.AreNotEqual(vote.Jaa.Teksti, vote.Ei.Teksti);
        }

        [TestMethod]
        public void SectionTheDissentDeletes_KeepsTheMinutesSentence()
        {
            // Dissent 1 removes 26 § outright, so it has no wording of it. What Ei stood for is
            // then only what the minutes said.
            var vote = Vote(Fixtures.MinutesPtk125_2025Trimmed, "2025-125-7");

            Assert.IsFalse(Report.Resolve(vote.Ei!));
            Assert.AreEqual("Lakiehdotukset hyväksytään vastalauseen 1 mukaisina.", vote.Ei!.Teksti);
        }

        [TestMethod]
        public void CommitteeStatementSide_PointsAtTheReport()
        {
            var vote = Vote(Fixtures.MinutesPtk108_2026, "2026-108-5");

            Assert.AreEqual("TrVM 11/2026 vp", vote.Mietinto);
            Assert.AreEqual(1, vote.Jaa?.Viite?.Lausuma);
            Assert.IsFalse(vote.Jaa!.Viite!.Vastalauseesta);
        }

        [TestMethod]
        public void ProposalFollowedByADash_IsStillRead()
        {
            // "…vastalauseen 1 mukaisena. — Selonteko ruotsiksi." The dash ends the sentence as
            // surely as a capital would.
            var xml = Fixtures.MinutesPtk108_2026.Replace(
                "vastalauseen 1 mukaisena.", "vastalauseen 1 mukaisena. — Selonteko ruotsiksi.");

            Assert.AreEqual("Lakiehdotukset hyväksytään vastalauseen 1 mukaisena.", Vote(xml, "2026-108-1").Ei?.Teksti);
        }

        [TestMethod]
        public void TwoProposalsInOneSentence_AreReadApart()
        {
            var xml = Fixtures.MinutesPtk108_2026.Replace(
                "Yleiskeskustelussa edustaja Lauri Lyly on edustaja Tiina Elon kannattamana ehdottanut, että lakiehdotukset hyväksytään vastalauseen 1 mukaisena.",
                "Yleiskeskustelussa edustaja Pia Lohikoski on edustaja Tiina Elon kannattamana ehdottanut, että lakiehdotukset hylätään, ja edustaja Lauri Lyly on edustaja Tiina Elon kannattamana ehdottanut, että lakiehdotukset hyväksytään vastalauseen 1 mukaisena.");

            Assert.AreEqual("Lakiehdotukset hyväksytään vastalauseen 1 mukaisena.", Vote(xml, "2026-108-1").Ei?.Teksti);
        }
    }
}
