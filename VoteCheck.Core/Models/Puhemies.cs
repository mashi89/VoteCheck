namespace VoteCheck.Core.Models
{
    // The Speaker presiding over a vote. Note the Speaker does not vote, so this person
    // will not appear in Aanestys.aanestystapahtumat — relevant when computing an MP's
    // attendance, since presiding is not an absence.
    public sealed class Puhemies
    {
        public LocalizedText? Alkuotsikko { get; set; }
        public LocalizedText? Loppuotsikko { get; set; }
        public LocalizedText? Titteli { get; set; }

        // Upstream sends this as a JSON string ("1109"); Newtonsoft coerces it to int.
        public int Henkilonumero { get; set; }

        public string? Sukunimi { get; set; }
        public string? Etunimi { get; set; }
        public LocalizedText? Edkryhmalyhenne { get; set; }
    }
}
