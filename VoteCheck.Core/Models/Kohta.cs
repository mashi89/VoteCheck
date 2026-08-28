using System.Collections.Generic;

namespace VoteCheck.Core.Models
{
    // The agenda item a vote belongs to. Kohta.Otsikko is the human-readable subject of the
    // vote (e.g. the government bill being decided) — the field the "what has my MP been
    // voting on" view needs, since aanestysotsikko only describes the ballot options.
    public sealed class Kohta
    {
        public LocalizedText? Kasittelyotsikkonimi { get; set; }
        public LocalizedText? Kasittelyvaihenimi { get; set; }
        public string? Jarjestys { get; set; }
        public string? Tunniste { get; set; }
        public bool Nakyykokohtatunniste { get; set; }
        public LocalizedText? Otsikko { get; set; }
        public KohtaAsiakirjat? Asiakirjat { get; set; }
    }

    // Document references for an agenda item. PaaasiakirjaEduskuntatunnus is the parliamentary
    // id of the main document (e.g. "HE 32/2026 vp") — the key for /valtiopaivaasiat and
    // /taysistunnot/asian-aanestykset lookups.
    public sealed class KohtaAsiakirjat
    {
        public LocalizedText? PaaasiakirjaEduskuntatunnus { get; set; }
        public string? PaaasiakirjaAsiatyyppi { get; set; }
        public string? PaaasiakirjaTehtavaluokka { get; set; }
        public List<string> Eduskuntatunnukset { get; set; } = new();
    }
}
