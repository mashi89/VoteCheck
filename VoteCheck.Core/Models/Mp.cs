using System.Collections.Generic;

namespace VoteCheck.Core.Models
{
    // Kansanedustaja, as returned by /kansanedustajat and /kansanedustajat/{id}.
    // Field names and shapes confirmed against a live response (see the fixture in
    // VoteCheck.Core.Tests/Fixtures/kansanedustaja-1109.json).
    //
    // The full upstream object also carries sidonnaisuudet, valiokuntajasenyydet,
    // toimielinjasenyydet, koulutukset, tyoura and more; those are per-language keyed
    // structures the v1 roadmap doesn't need yet, so they're deliberately not modeled.
    public sealed class Mp
    {
        // Upstream sends this as a JSON string ("1109"); Newtonsoft coerces it to int.
        public int Henkilonro { get; set; }

        public string? Etunimet { get; set; }
        public string? Sukunimi { get; set; }
        public string? Kutsumanimi { get; set; }
        public string? Kotikunta { get; set; }
        public int? Syntymavuosi { get; set; }
        public int? Kuolemavuosi { get; set; }
        public string? Syntymapaikka { get; set; }
        public string? Sukupuolikoodi { get; set; }
        public LocalizedText? Ammatti { get; set; }
        public string? Sahkoposti { get; set; }
        public string? Puhelinnumero { get; set; }
        public string? KansanedustajuusPaattynytPvm { get; set; }
        public EdustajantoimenTila? EdustajantoimenTila { get; set; }

        public Jasenyys? ViimeisinEduskuntaryhma { get; set; }
        public List<Jasenyys> Eduskuntaryhmat { get; set; } = new();
        public Jasenyys? ViimeisinVaalipiiri { get; set; }
        public List<Jasenyys> Vaalipiirit { get; set; } = new();

        public string DisplayName =>
            $"{(string.IsNullOrWhiteSpace(Kutsumanimi) ? Etunimet : Kutsumanimi)} {Sukunimi}".Trim();
    }
}
