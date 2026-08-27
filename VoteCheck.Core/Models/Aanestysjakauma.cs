namespace VoteCheck.Core.Models
{
    // A named breakdown sharing the same tally shape as AanestysTulos — used for
    // eduskuntaryhmaJakaumat (by party), hallitusoppositioJakaumat (government/opposition,
    // e.g. "Hallitusryhmät"/"Oppositioryhmät") and vaalipiiriJakaumat (by electoral
    // district). See design.md §3.1.
    //
    // Confirmed against a live response: nimi is a bilingual object, not a plain string.
    public sealed class Aanestysjakauma
    {
        public LocalizedText? Nimi { get; set; }
        public int Jaa { get; set; }
        public int Ei { get; set; }
        public int Tyhjia { get; set; }
        public int Poissa { get; set; }
        public int Yhteensa { get; set; }
    }
}
