using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

// One member's seat in the diagram: where it sits, and who and what it stands for.
// Row and Angle are the polar coordinates it was placed at, kept because the boundaries
// between groups are drawn along them.
public sealed record Seat(
    double X, double Y, string Vote, string Party, string Member, int PersonNumber,
    int Row, int Sector, double Angle );

// A group's short name, set under the block of seats it belongs to.
public sealed record ChamberLabel( double X, double Y, string Text );

// A laid-out chamber, with the box it needs. The caller does not compute geometry.
// Dividers are SVG path data, one per boundary between two groups.
public sealed record ChamberPlan(
    IReadOnlyList<Seat> Seats, IReadOnlyList<string> Dividers,
    IReadOnlyList<ChamberLabel> Labels,
    double Width, double Height, double Radius ) {
    public bool IsEmpty => Seats.Count == 0;
}

// Arranges one division's ballots into the shape of the Eduskunta chamber: a fan of
// concentric arcs, cut by aisles into four sectors.
//
// The proportions are measured from the seating plan the Eduskunta publishes
// (istumajarjestys-vaalikausi-2023-2026.pdf), by fitting a common centre to its 200 seats:
// eight seating rows, an outer radius about 2.4 times the inner one, seats spanning 25° to
// 154° about the centre, and aisles at roughly 62°, 90° and 117°. The four sectors those
// aisles make hold 48, 52, 51 and 49 members — near enough equal that this splits the
// chamber evenly between them.
//
// An earlier version drew a chevron, two straight arms rising from a point. That was taken
// from stylised graphics rather than from a seating plan, and it was wrong.
//
// **The seats are still not real.** api.eduskunta.fi publishes who voted and how, not where
// anyone sits, so who sits where is derived: members are placed in the conventional
// left-to-right order of the groups, and within a group by how they voted. What the picture
// is honest about is the thing it is for — the size of each group, the split inside it, and
// how the whole chamber divided. The shape is now honest too.
public static class Chamber {

    private const int RowCount = 8;
    private const double OuterRadius = 300;
    private const double InnerRadius = OuterRadius / 2.43;
    private const double SeatRadius = 6.2;
    private const double Margin = 8;
    private const double LabelSize = 17;

    // Extra steps of arc opened where one group meets the next, as a multiple of the step
    // between two neighbouring seats. Enough for the line plus clear air either side.
    private const double BoundaryGap = 1.1;

    // Sector bounds in degrees, measured from the plan. 0° is to the right of the centre and
    // 90° straight up, so the list runs right to left and the gaps between entries are the
    // aisles.
    private static readonly (double From, double To)[] Sectors =
        [ ( 25.0, 59.2 ), ( 65.2, 88.6 ), ( 92.5, 114.4 ), ( 119.6, 154.0 ) ];

    // How many seats each row holds, inner row first, as counted from the plan. The numbers
    // rise and then fall, which is the shape's whole character: the chamber is widest in the
    // middle rows, and the back rows are shorter than the ones in front of them. Treating
    // every row as a full arc growing outward gave a fan half again too wide.
    private static readonly double[] RowWeights = [ 16, 22, 26, 32, 30, 29, 26, 18 ];

    // The angular span of each row as a fraction of the widest, derived from those counts
    // against their radii. A back row covers about a third of the angle the middle rows do.
    private static readonly double[] RowSpan = [ 0.81, 0.92, 0.93, 1.00, 0.83, 0.72, 0.59, 0.37 ];

    // How much of a sector a given row actually holds.
    //
    // The aisles are gangways: they run radially, at the same angles in every row, so a row is
    // not compressed towards the centre line — it is cut short at its ends. A back row reaches
    // only the middle sectors, which is why the outermost arcs in the real plan span the
    // centre of the chamber and stop before its sides. Returns null when the row does not
    // reach this sector at all.
    private static (double From, double To)? SectorIn( int row, int sector ) {
        var ( from, to ) = Sectors[ sector ];
        var half = ( Sectors[ ^1 ].To - Sectors[ 0 ].From ) / 2 * RowSpan[ row ];
        var reachFrom = 90 - half;
        var reachTo = 90 + half;

        var clippedFrom = Math.Max( from, reachFrom );
        var clippedTo = Math.Min( to, reachTo );

        // A sliver too small to seat anyone is not a sector; dropping it keeps a lone seat
        // from being stranded past the end of its row.
        return clippedTo - clippedFrom < 2.0 ? null : ( clippedFrom, clippedTo );
    }

    private static double RowRadius( int row ) =>
        InnerRadius + ( OuterRadius - InnerRadius ) * row / ( RowCount - 1.0 );

    private static double RowGap => ( OuterRadius - InnerRadius ) / ( RowCount - 1.0 );

    public static ChamberPlan Arrange( IReadOnlyList<IndividualVote> ballots ) {
        if ( ballots.Count == 0 )
            return new ChamberPlan( [], [], [], 0, 0, SeatRadius );

        var seated = ballots
            .OrderBy( b => Party.SeatingRank( b.Party ) )
            .ThenBy( b => VoteRank( b.Vote ) )
            .ThenBy( b => b.LastName, StringComparer.CurrentCulture )
            .ThenBy( b => b.FirstName, StringComparer.CurrentCulture )
            .ToList();

        var places = Places( seated.Count );

        // Places come back left to right, so zipping them with members already in political
        // order puts each group in one contiguous wedge.
        var seats = new List<Seat>( seated.Count );
        for ( var i = 0; i < seated.Count; i++ ) {
            var b = seated[ i ];
            var p = places[ i ];
            seats.Add( new Seat( p.X, p.Y, b.Vote, b.Party,
                                 $"{b.FirstName} {b.LastName}", b.PersonNumber,
                                 p.Row, p.Sector, p.Angle ) );
        }

        seats = Respace( seats );

        // Labels sit outside the outer row, so they are laid out before the box is measured
        // and counted in it. Measuring the seats alone clipped the ones above the top of the
        // fan clean off.
        var labels = Labels( seats );

        var minX = Math.Min(
            seats.Min( s => s.X ) - SeatRadius,
            labels.Count == 0 ? double.MaxValue : labels.Min( l => l.X - HalfWidth( l.Text ) ) ) - Margin;
        var maxX = Math.Max(
            seats.Max( s => s.X ) + SeatRadius,
            labels.Count == 0 ? double.MinValue : labels.Max( l => l.X + HalfWidth( l.Text ) ) ) + Margin;
        var minY = Math.Min(
            seats.Min( s => s.Y ) - SeatRadius,
            labels.Count == 0 ? double.MaxValue : labels.Min( l => l.Y ) - LabelSize ) - Margin;
        var maxY = Math.Max(
            seats.Max( s => s.Y ) + SeatRadius,
            labels.Count == 0 ? double.MinValue : labels.Max( l => l.Y ) + LabelSize * 0.35 ) + Margin;

        var shifted = seats
            .Select( s => s with { X = Round( s.X - minX ), Y = Round( s.Y - minY ) } )
            .ToList();
        var shiftedLabels = labels
            .Select( l => l with { X = Round( l.X - minX ), Y = Round( l.Y - minY ) } )
            .ToList();

        return new ChamberPlan( shifted, Dividers( shifted, -minX, -minY ), shiftedLabels,
                                Round( maxX - minX ), Round( maxY - minY ), SeatRadius );
    }

    // Rough, and deliberately so: the exact width depends on the reader's font, and erring
    // wide both drops a doubtful label and reserves a little too much room, which are the
    // safe directions to be wrong in.
    private static double HalfWidth( string text ) => text.Length * LabelSize * 0.34;

    // Widen the step where one group gives way to the next, so the line between them has
    // somewhere to go.
    //
    // Evenly spaced, the gap between two neighbours on an arc is about a seat and a half
    // wide, and a line down the middle of it clips both. Worse, a line cannot be put at one
    // angle for the whole boundary at all: each arc holds a different number of seats, so the
    // groups change over at a different angle in every row, and a single spoke crossed a
    // circle in all seven boundaries of a full chamber — twice straight through the centre of
    // one. Each arc is respaced on its own, and each gets its own segment of the line.
    private static List<Seat> Respace( List<Seat> seats ) {
        var result = new List<Seat>( seats.Count );

        foreach ( var cell in seats.GroupBy( s => ( s.Row, s.Sector ) ) ) {
            var inCell = cell.OrderByDescending( s => s.Angle ).ToList();
            var span = SectorIn( cell.Key.Row, cell.Key.Sector );
            if ( span is null ) { result.AddRange( inCell ); continue; }

            var ( from, to ) = span.Value;
            var breaks = Enumerable.Range( 1, inCell.Count - 1 )
                .Count( i => inCell[ i ].Party != inCell[ i - 1 ].Party );

            // Seats take one step each, a change of group takes BoundaryGap extra.
            var step = ( to - from ) / ( inCell.Count + breaks * BoundaryGap );
            var radius = RowRadius( cell.Key.Row );

            var cursor = 0.0;
            for ( var i = 0; i < inCell.Count; i++ ) {
                if ( i > 0 && inCell[ i ].Party != inCell[ i - 1 ].Party )
                    cursor += step * BoundaryGap;

                // Seats run right to left along the arc, from `to` down towards `from`.
                var angle = to - ( cursor + step / 2 );
                var radians = angle * Math.PI / 180;
                result.Add( inCell[ i ] with {
                    X = radius * Math.Cos( radians ),
                    Y = -radius * Math.Sin( radians ),
                    Angle = angle,
                } );
                cursor += step;
            }
        }

        return result.OrderByDescending( s => s.Angle ).ThenBy( s => s.Row ).ToList();
    }

    private readonly record struct Place( double X, double Y, int Row, int Sector, double Angle );

    // Seat centres, ordered left to right across the whole chamber.
    private static List<Place> Places( int total ) {
        // Rows take the share the plan gives them; within a row, a wider sector takes more, so
        // seats stay evenly spaced along every arc.
        // A row takes the share the plan gives it, spread over however much of each sector it
        // actually reaches — so seats stay evenly spaced along every arc, and a row that stops
        // short of a sector is given nothing there.
        var weights = new double[ RowCount * Sectors.Length ];
        for ( var row = 0; row < RowCount; row++ ) {
            var reach = Enumerable.Range( 0, Sectors.Length )
                .Select( x => SectorIn( row, x ) )
                .Sum( x => x is null ? 0 : x.Value.To - x.Value.From );
            if ( reach <= 0 ) continue;

            for ( var sector = 0; sector < Sectors.Length; sector++ ) {
                var span = SectorIn( row, sector );
                if ( span is null ) continue;
                weights[ row * Sectors.Length + sector ] =
                    RowWeights[ row ] * ( span.Value.To - span.Value.From ) / reach;
            }
        }

        var counts = Apportion( total, weights );
        var places = new List<Place>( total );

        for ( var row = 0; row < RowCount; row++ ) {
            for ( var sector = 0; sector < Sectors.Length; sector++ ) {
                var n = counts[ row * Sectors.Length + sector ];
                if ( n == 0 ) continue;

                var span = SectorIn( row, sector );
                if ( span is null ) continue;
                var ( from, to ) = span.Value;
                var radius = RowRadius( row );

                for ( var i = 0; i < n; i++ ) {
                    // Even spacing inside the sector, half a step in from each aisle so the
                    // gap between two sectors stays a gap rather than closing up.
                    var angle = from + ( to - from ) * ( i + 0.5 ) / n;
                    var radians = angle * Math.PI / 180;
                    places.Add( new Place(
                        radius * Math.Cos( radians ),
                        -radius * Math.Sin( radians ),
                        row, sector, angle ) );
                }
            }
        }

        // 154° is the far left and 25° the far right, so left to right is decreasing angle.
        // Ties break outward, filling a wedge from the inside.
        return places.OrderByDescending( p => p.Angle ).ThenBy( p => p.Row ).ToList();
    }

    // Largest-remainder apportionment, so the seats drawn add up to the members there are
    // rather than losing one to rounding.
    private static int[] Apportion( int total, double[] weights ) {
        var sum = weights.Sum();
        var counts = new int[ weights.Length ];
        var remainders = new (int Index, double Fraction)[ weights.Length ];
        var assigned = 0;

        for ( var i = 0; i < weights.Length; i++ ) {
            var exact = total * weights[ i ] / sum;
            counts[ i ] = (int)Math.Floor( exact );
            remainders[ i ] = ( i, exact - counts[ i ] );
            assigned += counts[ i ];
        }

        foreach ( var ( index, _ ) in remainders
                     .OrderByDescending( x => x.Fraction )
                     .Take( total - assigned ) )
            counts[ index ]++;

        return counts;
    }

    // A line along each boundary between two groups, one segment per arc.
    //
    // It cannot be a single spoke. Each arc holds a different number of seats, so the groups
    // change over at a different angle in every row, and a seat is wide in angle terms — about
    // two degrees on the inner arcs. A spoke at one angle crossed a circle on all seven
    // boundaries of a full chamber, twice straight through the middle of one. Each arc gets
    // its own segment instead, placed in the gap Respace opened there.
    private static List<string> Dividers( List<Seat> seats, double cx, double cy ) {
        var paths = new List<string>();

        foreach ( var cell in seats.GroupBy( s => ( s.Row, s.Sector ) )
                                   .OrderBy( g => g.Key.Row ).ThenBy( g => g.Key.Sector ) ) {
            var inCell = cell.OrderByDescending( s => s.Angle ).ToList();
            var inner = RowRadius( cell.Key.Row ) - RowGap * 0.42;
            var outer = RowRadius( cell.Key.Row ) + RowGap * 0.42;

            for ( var i = 1; i < inCell.Count; i++ ) {
                if ( inCell[ i ].Party == inCell[ i - 1 ].Party ) continue;
                var angle = ( inCell[ i - 1 ].Angle + inCell[ i ].Angle ) / 2;
                paths.Add( $"M {Point( cx, cy, inner, angle )} L {Point( cx, cy, outer, angle )}" );
            }
        }

        return paths;
    }

    // A short name under each group, set below the outer edge of its seats.
    private static List<ChamberLabel> Labels( List<Seat> seats ) {
        var labels = new List<ChamberLabel>();
        var placedRight = double.MinValue;
        var first = true;

        foreach ( var group in seats.GroupBy( s => s.Party )
                                    .OrderByDescending( g => g.Max( s => s.Angle ) ) ) {
            var text = Party.ShortName( group.Key );

            // Centred on the group's middle angle and set outside the outer row, which is
            // where there is room — the middle of a wedge is full of seats.
            var midAngle = ( group.Min( s => s.Angle ) + group.Max( s => s.Angle ) ) / 2;
            var radians = midAngle * Math.PI / 180;
            var radius = OuterRadius + RowGap * 0.85;

            // The seats have been shifted into the viewBox, so the centre of the fan has moved
            // with them. Recover it from any seat: its polar coordinates are known exactly.
            var any = group.First();
            var cx = any.X - RowRadius( any.Row ) * Math.Cos( any.Angle * Math.PI / 180 );
            var cy = any.Y + RowRadius( any.Row ) * Math.Sin( any.Angle * Math.PI / 180 );

            var lx = cx + radius * Math.Cos( radians );
            var ly = cy - radius * Math.Sin( radians ) + LabelSize * 0.35;

            var half = HalfWidth( text );

            // Groups come in descending angle, which is left to right across the page, so each
            // label is checked against the right edge of the one before it.
            if ( !first && lx - half < placedRight ) continue;

            labels.Add( new ChamberLabel( Round( lx ), Round( ly ), text ) );
            placedRight = lx + half;
            first = false;
        }

        return labels;
    }

    private static string Point( double cx, double cy, double radius, double angleDeg ) {
        var radians = angleDeg * Math.PI / 180;
        return $"{N( cx + radius * Math.Cos( radians ) )} {N( cy - radius * Math.Sin( radians ) )}";
    }

    // Path data is markup, not text: a decimal comma would silently break every coordinate.
    private static string N( double value ) =>
        Math.Round( value, 2 ).ToString( System.Globalization.CultureInfo.InvariantCulture );

    // Jaa, Ei, then the ballots that decided nothing. Grouping a party's members by how they
    // voted makes a split visible as a block rather than as scattered dots.
    private static int VoteRank( string vote ) => vote switch {
        VoteValue.Yes => 0,
        VoteValue.No => 1,
        VoteValue.Blank => 2,
        VoteValue.Absent => 3,
        _ => 4,
    };

    private static double Round( double value ) => Math.Round( value, 2 );
}
