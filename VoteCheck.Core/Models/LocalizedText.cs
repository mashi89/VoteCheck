namespace VoteCheck.Core.Models
{
    // Titles in the new Eduskunta API come back as bilingual fi/sv objects (see design.md §3.1).
    public sealed class LocalizedText
    {
        public string? Fi { get; set; }
        public string? Sv { get; set; }
    }
}
