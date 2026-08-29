namespace VoteCheckWeb.Data;

// The canonical set of ballot values held in the mirror, and the rule for getting there
// from what upstream sends. Lives beside the schema rather than inside the sync because
// the queries, the pages and the seeding script all depend on the same four strings.
public static class VoteValue {

    public const string Yes = "Jaa";
    public const string No = "Ei";
    public const string Blank = "Tyhjä";
    public const string Absent = "Poissa";

    // Upstream spells the blank vote "Tyhjää" (partitive) and pads some values with
    // whitespace. Anything unrecognised is passed through trimmed rather than dropped:
    // losing a ballot silently would skew a party tally, whereas an unexpected value is
    // visible in the data and can be dealt with when it shows up.
    public static string Normalize( string? raw ) => ( raw ?? "" ).Trim() switch {
        Yes => Yes,
        No => No,
        "Tyhjää" or Blank => Blank,
        Absent => Absent,
        var other => other,
    };
}
