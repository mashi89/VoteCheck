namespace VoteCheck.Core.Models
{
    // A single MP's ballot within an Aanestys.aanestystapahtumat list. See design.md §3.1.
    public sealed class EdustajanAanestys
    {
        public int Henkilonumero { get; set; }
        public string? Sukunimi { get; set; }
        public string? Etunimi { get; set; }
        public string? Edkryhmalyhenne { get; set; }
        public string? Vaalipiiri { get; set; }
        public string? Eduskuntaryhma { get; set; }
        public string? Sukupuoli { get; set; }

        // The vote itself: "Jaa" | "Ei" | "Tyhjä" | "Poissa" (per the legacy API's vocabulary;
        // unconfirmed whether the new API uses the same literal strings).
        public string? Kayttaytyminen { get; set; }
    }
}
