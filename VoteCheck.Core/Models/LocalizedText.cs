namespace VoteCheck.Core.Models
{
    // Many api.eduskunta.fi fields are bilingual/trilingual objects rather than plain strings
    // (see design.md §3.1). Vote payloads carry fi/sv; MP payloads add en on some fields
    // (ammatti, group/district names), so En is nullable and often absent.
    public sealed class LocalizedText
    {
        public string? Fi { get; set; }
        public string? Sv { get; set; }
        public string? En { get; set; }

        // Convenience for UI code that just wants "the Finnish one, or whatever exists".
        public string? Preferred => Fi ?? Sv ?? En;

        public override string ToString() => Preferred ?? string.Empty;
    }
}
