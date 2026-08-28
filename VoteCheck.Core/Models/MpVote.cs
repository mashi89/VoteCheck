namespace VoteCheck.Core.Models
{
    // One MP's ballot presented in the context of the vote it belongs to — the row behind
    // "what has my MP been voting on lately".
    //
    // Subject comes from Kohta.Otsikko (the matter being decided), not Aanestysotsikko, which
    // only describes the ballot options. See design.md §3.1.
    public sealed class MpVote
    {
        public string? VoteId { get; init; }
        public string? SessionId { get; init; }
        public string? Date { get; init; }

        // The matter being decided (Kohta.Otsikko).
        public LocalizedText? Subject { get; init; }

        // What the ballot options were (Aanestysotsikko).
        public LocalizedText? BallotTitle { get; init; }

        // Parliamentary id of the originating document, e.g. "HE 32/2026 vp".
        public LocalizedText? DocumentId { get; init; }

        public VoteChoice Choice { get; init; }

        // The MP's party abbreviation at the time of this vote, e.g. { fi: "kok" }.
        public LocalizedText? PartyAbbreviation { get; init; }

        // True when the division itself was annulled upstream (aanestysmitatoity).
        public bool Annulled { get; init; }
    }
}
