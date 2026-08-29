using Newtonsoft.Json;

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

        // Upstream sends this as a JSON string ("1109"), but a couple of divisions in the
        // archive carry "-" where no Speaker is recorded, so this is nullable and parsed
        // leniently rather than throwing mid-sync. Null means "not recorded".
        [JsonConverter(typeof(LenientInt32Converter))]
        public int? Henkilonumero { get; set; }

        public string? Sukunimi { get; set; }
        public string? Etunimi { get; set; }
        public LocalizedText? Edkryhmalyhenne { get; set; }
    }
}
