using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    // Reading what each side of a vote stood for out of a session's minutes, against two real
    // documents: one where proposals are described in the debate, and a supplementary budget
    // where they are listed by number.
    [TestClass]
    public class PoytakirjaTests
    {
        private static PoytakirjaAanestys Vote(string xml, string id) =>
            Poytakirja.Parse("EDK", xml).Aanestykset.Single(v => v.AanestysTunnus == id);

        [TestMethod]
        public void EveryVoteIsKeyedToTheDivisionItRecords()
        {
            var minutes = Poytakirja.Parse("EDK-2024-AK-15838", Fixtures.MinutesPtk39_2024);

            Assert.AreEqual("EDK-2024-AK-15838", minutes.Edktunnus);
            CollectionAssert.AreEqual(
                Enumerable.Range(1, 8).Select(n => $"2024-39-{n}").ToArray(),
                minutes.Aanestykset.Select(v => v.AanestysTunnus).ToArray());
        }

        [TestMethod]
        public void SetupAndOutcome_AreTheMinutesOwnWords()
        {
            var vote = Vote(Fixtures.MinutesPtk108_2026, "2026-108-5");

            Assert.AreEqual("Mietinnön lausumaehdotus 1 ”jaa”, Pia Lohikosken ehdotus ”ei”.", vote.Asettelu);
            Assert.AreEqual("Eduskunta hyväksyi mietinnön lausumaehdotuksen 1.", vote.Tulos);
        }

        [TestMethod]
        public void NumberedProposal_BringsItsTextAndBudgetItem()
        {
            var vote = Vote(Fixtures.MinutesPtk39_2024, "2024-39-1");

            Assert.AreEqual("Mietintö", vote.Jaa?.Nimi);
            Assert.AreEqual("", vote.Jaa?.Teksti, "the committee's side is not stated in the minutes");
            Assert.AreEqual("Saara Hyrkön ehdotus 1", vote.Ei?.Nimi);
            Assert.AreEqual("29.91.51", vote.Ei?.Kohta);
            Assert.AreEqual(
                "Lisätään 8 500 000 euroa etsivään nuorisotyöhön ja nuorten syrjäytymisen ehkäisyyn. (Vastalause 2)",
                vote.Ei?.Teksti);
        }

        [TestMethod]
        public void ProposalAgainstProposal_ResolvesBothSides()
        {
            // The first of a pair: two members' proposals on the same point, before the winner
            // meets the committee's.
            var vote = Vote(Fixtures.MinutesPtk39_2024, "2024-39-2");

            Assert.AreEqual("Hanna Sarkkisen ehdotus 2", vote.Jaa?.Nimi);
            Assert.AreEqual("30.20.20", vote.Jaa?.Kohta);
            StringAssert.StartsWith(vote.Jaa?.Teksti,
                "Lausumaehdotus: ”Eduskunta edellyttää, että hallitus tuo eduskunnan käsiteltäväksi esityksen turkistarhauksen lopettamiseksi");
            Assert.AreEqual("Saara Hyrkön ehdotus 7", vote.Ei?.Nimi);
            Assert.AreEqual("yleisperustelut", vote.Ei?.Kohta);
            Assert.AreEqual("Tässä äänestyksessä voitti Hanna Sarkkisen ehdotus 2.", vote.Tulos);
        }

        [TestMethod]
        public void BudgetChapters_AreReadAndKeepTheirOwnNumbering()
        {
            // The annual budget's per-chapter votes sit in MuuAsiakohta items, and each chapter
            // numbers its proposals from 1. "Saara Hyrkön ehdotus 2" is a different proposal in
            // chapter 23 than in chapter 24.
            var minutes = Poytakirja.Parse("EDK", Fixtures.MinutesPtk76_2023Trimmed);

            Assert.AreEqual(20, minutes.Aanestykset.Count);

            var chapter23 = minutes.Aanestykset.Single(v => v.AanestysTunnus == "2023-76-3");
            var chapter24 = minutes.Aanestykset.Single(v => v.AanestysTunnus == "2023-76-7");
            Assert.AreEqual("Saara Hyrkön ehdotus 2", chapter23.Ei?.Nimi);
            Assert.AreEqual("Saara Hyrkön ehdotus 2", chapter24.Ei?.Nimi);
            Assert.AreEqual("23.01.22", chapter23.Ei?.Kohta);
            Assert.AreEqual("24.30.66", chapter24.Ei?.Kohta);
            StringAssert.StartsWith(chapter24.Ei?.Teksti, "Lisätään 130 000 000 euroa varsinaiseen kehitysyhteistyöhön.");
        }

        [TestMethod]
        public void UnnumberedSide_TakesTheMembersOnlyListedProposal()
        {
            // "Saara Hyrkön ehdotus" with no number, in an item whose list holds one proposal of
            // hers: a statement on the government's economic policy, with no budget item.
            var vote = Vote(Fixtures.MinutesPtk76_2023Trimmed, "2023-76-425");

            Assert.AreEqual("Saara Hyrkön ehdotus", vote.Jaa?.Nimi);
            Assert.AreEqual("", vote.Jaa?.Kohta);
            StringAssert.StartsWith(vote.Jaa?.Teksti, "”Eduskunta toteaa, että hallituksen talouspolitiikka");
        }

        [TestMethod]
        public void DescribedProposal_IsTheClauseTheMinutesState()
        {
            var vote = Vote(Fixtures.MinutesPtk108_2026, "2026-108-1");

            Assert.AreEqual("Lauri Lylyn ehdotus", vote.Ei?.Nimi);
            Assert.AreEqual("Lakiehdotukset hyväksytään vastalauseen 1 mukaisena.", vote.Ei?.Teksti);
            Assert.AreEqual("Eduskunta hyväksyi mietinnön.", vote.Tulos);
        }

        [TestMethod]
        public void DescribedProposal_StopsBeforeTheAttachmentReference()
        {
            // The proposal itself is in an attachment the minutes point to. What is stated here
            // is what it does, and the bracketed reference is not part of that sentence.
            var vote = Vote(Fixtures.MinutesPtk108_2026, "2026-108-5");

            Assert.AreEqual(
                "Mietinnön lausumaehdotus 1 hylätään ja korvataan lausumaehdotuksella, joka on toimitettu edustajille.",
                vote.Ei?.Teksti);
        }

        [TestMethod]
        public void AcceptOrReject_HasLabelsAndNoInventedText()
        {
            var vote = Vote(Fixtures.MinutesPtk108_2026, "2026-108-2");

            Assert.AreEqual("Lakiehdotuksen hyväksyminen", vote.Jaa?.Nimi);
            Assert.AreEqual("hylkääminen", vote.Ei?.Nimi);
            Assert.AreEqual("", vote.Jaa?.Teksti);
            Assert.AreEqual("", vote.Ei?.Teksti);
        }

        [TestMethod]
        public void NumberedProposal_IsNotAttachedWhenTheNameDisagrees()
        {
            // "ehdotus 1" in the list is Saara Hyrkkö's. A vote naming someone else's proposal 1
            // must not borrow her text: a wrong text beside a vote is worse than none.
            var xml = Fixtures.MinutesPtk39_2024.Replace(
                "Mietintö ”jaa”, Saara Hyrkön ehdotus 1 ”ei”.",
                "Mietintö ”jaa”, Hanna Sarkkisen ehdotus 1 ”ei”.");

            var vote = Vote(xml, "2024-39-1");

            Assert.AreEqual("Hanna Sarkkisen ehdotus 1", vote.Ei?.Nimi);
            Assert.AreEqual("", vote.Ei?.Teksti);
        }

        [TestMethod]
        public void SetupWithoutBothSidesMarked_ResolvesNoOptions()
        {
            var xml = Fixtures.MinutesPtk108_2026.Replace(
                "Lakiehdotuksen hyväksyminen ”jaa”, hylkääminen ”ei”.",
                "Puolueen puheenjohtaja Petteri Orpon valinta pääministeriksi ”jaa”; joka ei kannata valintaa, äänestää ”ei”.");

            var vote = Vote(xml, "2026-108-2");

            Assert.IsNull(vote.Jaa);
            Assert.IsNull(vote.Ei);
            StringAssert.StartsWith(vote.Asettelu, "Puolueen puheenjohtaja");
        }

        [TestMethod]
        public void MalformedXml_IsAnEmptyDocumentRatherThanAnException()
        {
            Assert.AreEqual(0, Poytakirja.Parse("EDK", "<not xml").Aanestykset.Count);
        }

        [TestMethod]
        public void SameMember_FollowsTheInflectedSurname()
        {
            Assert.IsTrue(Poytakirja.SameMember("Hanna Sarkkinen", "Hanna Sarkkisen"));
            Assert.IsTrue(Poytakirja.SameMember("Saara Hyrkkö", "Saara Hyrkön"));
            Assert.IsTrue(Poytakirja.SameMember("Tiina Elo", "Tiina Elon"));
            Assert.IsTrue(Poytakirja.SameMember("Pia Lohikoski", "Pia Lohikosken"));

            Assert.IsFalse(Poytakirja.SameMember("Hanna Sarkkinen", "Hanna Räsäsen"));
            Assert.IsFalse(Poytakirja.SameMember("Saara Hyrkkö", "Hanna Hyrkön"));
            Assert.IsFalse(Poytakirja.SameMember("Elo", "Elon"));
        }
    }
}
