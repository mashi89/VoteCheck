using System;
using System.Collections.Generic;
using System.Linq;

namespace VoteCheck.Core.Models
{
    // A matter before parliament — the bill, report or interpellation a division decides a step
    // of. Divisions name the matter only in its own legal title; this is where the plainer
    // description lives.
    public sealed class Valtiopaivaasia
    {
        // "HE 113/2026 vp". The identifier a division carries in
        // kohta.asiakirjat.paaasiakirjaEduskuntatunnus, and the key for this lookup.
        public LocalizedText? Eduskuntatunnus { get; set; }

        // The full legal title, the same text a division carries as its subject.
        public LocalizedText? Nimeke { get; set; }

        // "Hallituksen esitys" rather than the bare code "HE".
        public LocalizedText? Asiakirjatyyppinimi { get; set; }

        // Where the matter has got to, e.g. "Käsitelty".
        public LocalizedText? Tila { get; set; }

        // How the matter ended overall, e.g. "Hyväksytty", "Hyväksytty muutettuna".
        //
        // This is the fate of the whole matter, not the result of any one division. A division
        // decides a step; a bill can survive a lost amendment vote and pass anyway. Anything
        // rendering this has to say which of the two it is showing.
        public LocalizedText? Kokonaispaatosnimi { get; set; }

        public LocalizedText? ViimeisinKasittelyvaihe { get; set; }

        // Subject keywords from the YSO ontology — "tulovero", "kotitalousvähennys". The part
        // of this record that actually tells a reader what the matter is about.
        public Asiasanat? Asiasanat { get; set; }

        // Keywords in reading order, Finnish. Upstream numbers them rather than returning them
        // sorted, and the numbering is what decides which are the headline subjects.
        public IReadOnlyList<string> Keywords( ) =>
            ( Asiasanat?.Fi ?? new List<Asiasana>() )
                .Where( a => !string.IsNullOrWhiteSpace( a.Aiheteksti ) )
                .OrderBy( a => a.AsiasanaJarjestys )
                .Select( a => a.Aiheteksti! )
                .ToList();

        // The Eduskunta's own page for this matter, or null when the identifier is not the
        // shape the site's URLs are built from.
        //
        // "HE 113/2026 vp" becomes /valtiopaivaasiat/HE+113/2026. Verified against HE, VNS, VNT
        // and VK; a document that does not exist 404s rather than resolving to something else,
        // so a wrong guess fails visibly instead of sending a reader somewhere misleading.
        public Uri? PublicUrl( )
        {
            string? tunnus = Eduskuntatunnus?.Fi;
            if (string.IsNullOrWhiteSpace(tunnus))
                return null;

            // "HE 113/2026 vp" -> type "HE", number "113", year "2026".
            string[] parts = tunnus.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            string type = parts[0];
            string[] numberAndYear = parts[1].Split('/');
            if (numberAndYear.Length != 2)
                return null;

            string number = numberAndYear[0];
            string year = numberAndYear[1];

            // A combined identifier such as "LA 1, 18/2023 vp" names several documents at once
            // and has no single page; skip rather than link to the wrong one.
            if (number.Contains(',') || !int.TryParse(number, out _) || !int.TryParse(year, out _))
                return null;

            return new Uri($"https://www.eduskunta.fi/valtiopaivaasiat/{type}+{number}/{year}");
        }
    }

    public sealed class Asiasanat
    {
        public List<Asiasana> Fi { get; set; } = new();
        public List<Asiasana> Sv { get; set; } = new();
    }

    public sealed class Asiasana
    {
        public string? Aiheteksti { get; set; }
        public int AsiasanaJarjestys { get; set; }

        // A YSO ontology URI. Kept because it is the stable identity behind the word, should
        // anything ever want to group synonyms.
        public string? Muutunnus { get; set; }
    }
}
