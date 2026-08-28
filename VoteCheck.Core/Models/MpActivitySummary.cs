namespace VoteCheck.Core.Models
{
    // Rolled-up voting activity for one MP over some window of votes — the computed view that
    // distinguishes VoteCheck from raw open data (design.md §4, Step 2).
    //
    // "Attendance" here means the MP was recorded casting a ballot of any kind, including a
    // blank/abstention: Jaa + Ei + Tyhjä. Presiding as Speaker is NOT an absence — the Speaker
    // does not vote and is omitted from the ballot list entirely, so those divisions simply
    // never enter this MP's window rather than counting against them.
    public sealed class MpActivitySummary
    {
        public int Henkilonumero { get; init; }
        public string? Name { get; init; }
        public LocalizedText? Party { get; init; }

        // Divisions in the window where this MP appears in the ballot list.
        public int TotalVotes { get; init; }

        public int Jaa { get; init; }
        public int Ei { get; init; }
        public int Tyhja { get; init; }
        public int Poissa { get; init; }

        // Ballots whose vocabulary term wasn't recognised; non-zero here means the upstream
        // vocabulary has drifted and the other counts may under-report.
        public int Unknown { get; init; }

        // Cast a ballot of any kind, abstentions included.
        public int Present => Jaa + Ei + Tyhja;

        // Share of the window where the MP cast a ballot, 0.0–1.0. Null when the window is
        // empty, since 0% attendance and "no data" are different claims.
        public double? AttendanceRate =>
            TotalVotes == 0 ? null : (double)Present / TotalVotes;
    }
}
