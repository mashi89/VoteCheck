namespace VoteCheck.Core.Models
{
    // A named breakdown of the same tally shape as AanestysTulos — used for
    // eduskuntaryhmaJakaumat (by party), hallitusoppositioJakaumat (government/opposition),
    // and vaalipiiriJakaumat (by electoral district). See design.md §3.1.
    public sealed class Aanestysjakauma
    {
        public string? Nimi { get; set; }
        public int Jaa { get; set; }
        public int Ei { get; set; }
        public int Tyhjia { get; set; }
        public int Poissa { get; set; }
        public int Yhteensa { get; set; }
    }
}
