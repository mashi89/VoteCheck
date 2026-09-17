using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core.Models;

namespace VoteCheck.Core.Tests
{
    // The matter behind a division: how its identifier becomes a link, and how upstream's
    // subject terms become the words shown to a reader.
    [TestClass]
    public class ValtiopaivaasiaTests
    {
        private static Valtiopaivaasia Matter(string tunnus) =>
            new() { Eduskuntatunnus = new LocalizedText { Fi = tunnus } };

        [TestMethod]
        public void PublicUrl_BuildsTheEduskuntaPageForEachDocumentType()
        {
            // Verified against the live site: these four resolve, and a document that does not
            // exist 404s rather than redirecting somewhere plausible — so a wrong guess fails
            // visibly instead of sending a reader to the wrong bill.
            Assert.AreEqual("https://www.eduskunta.fi/valtiopaivaasiat/HE+113/2026",
                Matter("HE 113/2026 vp").PublicUrl()?.ToString());
            Assert.AreEqual("https://www.eduskunta.fi/valtiopaivaasiat/VNS+2/2026",
                Matter("VNS 2/2026 vp").PublicUrl()?.ToString());
            Assert.AreEqual("https://www.eduskunta.fi/valtiopaivaasiat/VK+1/2026",
                Matter("VK 1/2026 vp").PublicUrl()?.ToString());
            Assert.AreEqual("https://www.eduskunta.fi/valtiopaivaasiat/VNT+1/2026",
                Matter("VNT 1/2026 vp").PublicUrl()?.ToString());
        }

        [TestMethod]
        public void PublicUrl_RefusesAnIdentifierNamingSeveralDocuments()
        {
            // "LA 1, 18/2023 vp" is two private members' bills taken together. There is no one
            // page for it, and guessing either would attach a division to the wrong document.
            Assert.IsNull(Matter("LA 1, 18/2023 vp").PublicUrl());
        }

        [TestMethod]
        public void PublicUrl_RefusesAnythingItCannotParse()
        {
            Assert.IsNull(Matter("").PublicUrl());
            Assert.IsNull(Matter("HE").PublicUrl());
            Assert.IsNull(Matter("HE 113-2026 vp").PublicUrl());
            Assert.IsNull(Matter("HE yksi/2026 vp").PublicUrl());
            Assert.IsNull(new Valtiopaivaasia().PublicUrl());
        }

        [TestMethod]
        public void Keywords_FollowUpstreamsOwnOrderRatherThanTheOrderItSendsThem()
        {
            // Upstream numbers the subject terms and then returns them alphabetically. The
            // numbering is what says which are the headline subjects, so a page showing only
            // the first few has to sort by it: for HE 113/2026 the lead term is "tulovero",
            // which arrives last alphabetically.
            var matter = new Valtiopaivaasia
            {
                Asiasanat = new Asiasanat
                {
                    Fi = new List<Asiasana>
                    {
                        new() { Aiheteksti = "kotitalousvähennys", AsiasanaJarjestys = 2 },
                        new() { Aiheteksti = "verovähennykset", AsiasanaJarjestys = 4 },
                        new() { Aiheteksti = "tulovero", AsiasanaJarjestys = 1 },
                        new() { Aiheteksti = "matkakustannukset", AsiasanaJarjestys = 3 },
                    },
                },
            };

            CollectionAssert.AreEqual(
                new[] { "tulovero", "kotitalousvähennys", "matkakustannukset", "verovähennykset" },
                (List<string>)matter.Keywords());
        }

        [TestMethod]
        public void Keywords_SkipsBlanksAndCopesWithNoneAtAll()
        {
            Assert.AreEqual(0, new Valtiopaivaasia().Keywords().Count);

            var matter = new Valtiopaivaasia
            {
                Asiasanat = new Asiasanat
                {
                    Fi = new List<Asiasana>
                    {
                        new() { Aiheteksti = "  ", AsiasanaJarjestys = 1 },
                        new() { Aiheteksti = null, AsiasanaJarjestys = 2 },
                        new() { Aiheteksti = "tulovero", AsiasanaJarjestys = 3 },
                    },
                },
            };
            CollectionAssert.AreEqual(new[] { "tulovero" }, (List<string>)matter.Keywords());
        }
    }
}
