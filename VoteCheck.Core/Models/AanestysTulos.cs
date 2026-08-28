namespace VoteCheck.Core.Models
{
    // The tally embedded in an Aanestys (jaa/ei/tyhjia/poissa/yhteensa) — see design.md §3.1.
    public sealed class AanestysTulos
    {
        public int Jaa { get; set; }
        public int Ei { get; set; }
        public int Tyhjia { get; set; }
        public int Poissa { get; set; }
        public int Yhteensa { get; set; }
    }
}
