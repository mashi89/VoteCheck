namespace VoteCheck.Core.Models
{
    // A single MP's ballot within Aanestys.aanestystapahtumat.
    // Confirmed against a live response: vaalipiiri, eduskuntaryhma, sukupuoli,
    // kayttaytyminen and edkryhmalyhenne are all bilingual objects upstream, NOT plain
    // strings — hence LocalizedText rather than string.
    public sealed class EdustajanAanestys
    {
        // Upstream sends this as a JSON string ("1504"); Newtonsoft coerces it to int.
        public int Henkilonumero { get; set; }

        public string? Sukunimi { get; set; }
        public string? Etunimi { get; set; }

        // Party abbreviation, localized: { "fi": "kok", "sv": "saml" }.
        public LocalizedText? Edkryhmalyhenne { get; set; }

        public LocalizedText? Vaalipiiri { get; set; }
        public LocalizedText? Eduskuntaryhma { get; set; }
        public LocalizedText? Sukupuoli { get; set; }

        // How the MP voted. Observed Finnish values: "Jaa", "Ei", "Poissa"
        // (and "Tyhjä" where an abstention occurs).
        public LocalizedText? Kayttaytyminen { get; set; }
    }
}
