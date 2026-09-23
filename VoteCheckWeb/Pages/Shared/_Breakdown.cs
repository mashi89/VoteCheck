using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

// One row of a breakdown on the vote page: a group, the government or opposition, or an
// electoral district. A link and a tooltip only where the row has somewhere to go.
public sealed record BreakdownRow(
    string Name, int Yes, int No, int Blank, int Absent,
    string? Href = null, string? Tooltip = null ) {
    public int Total => Yes + No + Blank + Absent;
}

// A set of rows drawn against one scale, so a bar's length says how big the row is as well
// as how it voted.
public sealed record Breakdown( IReadOnlyList<BreakdownRow> Rows ) {
    // Rows with nowhere to link to: the government split and the districts.
    public static Breakdown Of( IEnumerable<Split> splits ) =>
        new( splits.Select( s => new BreakdownRow( s.Name, s.Yes, s.No, s.Blank, s.Absent ) ).ToList() );

    public int Largest => Rows.Count == 0 ? 1 : Math.Max( 1, Rows.Max( r => r.Total ) );
}
