namespace VoteCheck.Core.Models
{
    // A dated membership entry — used for both eduskuntaryhmat (parliamentary groups) and
    // vaalipiirit (electoral districts) on Kansanedustaja. Confirmed against a live
    // /kansanedustajat/{id} response.
    //
    // Dates are plain "yyyy-MM-dd" strings upstream; loppupvm is null for a current membership.
    public sealed class Jasenyys
    {
        public LocalizedText? Nimi { get; set; }
        public string? Tunnus { get; set; }
        public string? Alkupvm { get; set; }
        public string? Loppupvm { get; set; }

        public bool IsCurrent => string.IsNullOrEmpty(Loppupvm);
    }
}
