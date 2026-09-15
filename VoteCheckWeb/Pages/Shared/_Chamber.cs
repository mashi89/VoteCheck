using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

// One member's seat in the diagram: where it sits, and who and what it stands for.
public sealed record Seat(
    double X, double Y, string Vote, string Party, string Member, int PersonNumber );

// A group's short name, centred under the block of seats it belongs to.
public sealed record ChamberLabel( double X, double Y, string Text );

// A laid-out chamber, with the box it needs. The caller does not compute geometry.
// Dividers are SVG path data, one per boundary between two groups.
public sealed record ChamberPlan(
    IReadOnlyList<Seat> Seats, IReadOnlyList<string> Dividers,
    IReadOnlyList<ChamberLabel> Labels,
    double Width, double Height, double Radius ) {
    public bool IsEmpty => Seats.Count == 0;
}

// Arranges one division's ballots into the chevron the Eduskunta chamber is laid out in —
// two arms rising from a point at the centre, rather than the semicircle most parliaments
// use. The shape is the reason the picture is recognisable as this parliament and not a
// generic one.
//
// **These are not real seats.** api.eduskunta.fi publishes who voted and how, not where
// anyone sits, so positions are derived: members are placed in the conventional left-to-right
// order of the groups, and within a group by how they voted. The page says so in as many
// words. What the picture is honest about is the thing it is for — the size of each group,
// the split inside it, and how the whole chamber divided.
public static class Chamber {

    // Six rows rather than eight. The same members then spread over 43 columns instead of
    // 32, which is what gives the boundaries between groups room to be told apart, and leaves
    // the chevron wide and shallow the way the printed chamber graphics draw it.
    private const int RowCount = 6;
    private const double SeatSpacing = 18;   // along an arm, centre to centre
    private const double RowSpacing = 21;    // between nested chevrons
    private const double SeatRadius = 6.2;
    private const double Margin = 8;
    private const double LabelSize = 17;     // group names, in viewBox units

    // 30° from horizontal. Shallower reads as a line rather than a chamber; steeper turns the
    // chevron into a V narrow enough that the outer rows tower over the inner ones.
    private const double ArmAngle = Math.PI / 6;

    public static ChamberPlan Arrange( IReadOnlyList<IndividualVote> ballots ) {
        if ( ballots.Count == 0 )
            return new ChamberPlan( [], [], [], 0, 0, SeatRadius );

        var seated = ballots
            .OrderBy( b => Party.SeatingRank( b.Party ) )
            .ThenBy( b => VoteRank( b.Vote ) )
            .ThenBy( b => b.LastName, StringComparer.CurrentCulture )
            .ThenBy( b => b.FirstName, StringComparer.CurrentCulture )
            .ToList();

        var positions = Positions( seated.Count );

        // Positions come back in visual order, left edge to right edge, so zipping them with
        // members already in political order puts each group in one contiguous band.
        var seats = new List<Seat>( seated.Count );
        for ( var i = 0; i < seated.Count; i++ ) {
            var b = seated[ i ];
            var ( x, y ) = positions[ i ];
            seats.Add( new Seat( x, y, b.Vote, b.Party,
                                 $"{b.FirstName} {b.LastName}", b.PersonNumber ) );
        }

        // Normalise into a box that starts at the origin, so the caller's viewBox is just
        // "0 0 width height" and nothing depends on where the maths happened to put things.
        var minX = seats.Min( s => s.X ) - SeatRadius - Margin;
        var minY = seats.Min( s => s.Y ) - SeatRadius - Margin;
        var maxX = seats.Max( s => s.X ) + SeatRadius + Margin;
        var maxY = seats.Max( s => s.Y ) + SeatRadius + Margin;

        var shifted = seats
            .Select( s => s with { X = Round( s.X - minX ), Y = Round( s.Y - minY ) } )
            .ToList();

        var labels = Labels( shifted );

        // The box has to grow to hold the labels, which hang below the seats.
        var height = labels.Count == 0
            ? maxY - minY
            : Math.Max( maxY - minY, labels.Max( l => l.Y ) + LabelSize * 0.35 + Margin );

        return new ChamberPlan( shifted, Dividers( shifted ), labels,
                                Round( maxX - minX ), Round( height ), SeatRadius );
    }

    // A short name under each group, centred on its seats and hanging just below them, so the
    // labels follow the underside of the chevron. One line under the whole diagram would have
    // left the labels for the arms floating far from the members they name.
    private static List<ChamberLabel> Labels( List<Seat> seats ) {
        var labels = new List<ChamberLabel>();
        double placedRight = 0, placedY = 0;
        var first = true;

        foreach ( var group in seats.GroupBy( s => s.Party )
                                    .OrderBy( g => g.Min( s => s.X ) ) ) {
            var text = Party.ShortName( group.Key );
            var centre = ( group.Min( s => s.X ) + group.Max( s => s.X ) ) / 2;

            // Rough, and deliberately so: the exact width depends on the reader's font, and
            // erring wide both drops a doubtful label and clears more seats than strictly
            // needed, which are the safe directions to be wrong in.
            var half = text.Length * LabelSize * 0.34;

            // Clear every seat the label will actually sit over, not just the ones in this
            // group's own column. The underside of the chevron slopes, so a label centred on
            // one column overhangs deeper ones beside it: measuring a single column put VAS
            // and KOK inside the seats rather than under them.
            var footprint = seats
                .Where( s => Math.Abs( s.X - centre ) <= half + SeatRadius )
                .Select( s => s.Y )
                .DefaultIfEmpty( group.Max( s => s.Y ) )
                .Max();
            var y = footprint + SeatRadius + LabelSize;

            var left = centre - half;
            var right = centre + half;

            // Groups of one or two members are narrower than their own name, and a row of
            // overlapping labels identifies nobody. The list below the diagram names every
            // group, and hovering a seat names its member's, so dropping one costs little.
            if ( !first && left < placedRight && Math.Abs( y - placedY ) < LabelSize * 1.2 )
                continue;

            labels.Add( new ChamberLabel( Round( centre ), Round( y ), text ) );
            ( placedRight, placedY ) = ( right, y );
            first = false;
        }

        return labels;
    }

    // A line along each boundary between two groups, so the blocks are bounded rather than
    // left to be inferred from where the colour happens to change — a group that votes the
    // same way as its neighbour is otherwise invisible as a group at all.
    //
    // The layout is a lattice of vertical columns, so a boundary usually falls in the gap
    // between two of them and the line is straight. It does not always: a group rarely ends
    // exactly where a column does, and then the boundary runs partway down inside one column
    // and the line has to step around it. Drawing the straight line in that case would put
    // members on the wrong side of their own group.
    private static List<string> Dividers( List<Seat> seats ) {
        var columns = seats
            .GroupBy( s => s.X )
            .ToDictionary( g => g.Key, g => ( Top: g.Min( s => s.Y ), Bottom: g.Max( s => s.Y ) ) );

        var halfColumn = SeatSpacing * Math.Cos( ArmAngle ) / 2;
        var reach = SeatRadius + 6;
        var paths = new List<string>();

        for ( var i = 1; i < seats.Count; i++ ) {
            var before = seats[ i - 1 ];
            var after = seats[ i ];
            if ( before.Party == after.Party ) continue;

            if ( before.X < after.X ) {
                // Clean break between columns: one straight line down the gap, long enough to
                // clear the taller of the two columns it separates.
                var x = ( before.X + after.X ) / 2;
                var left = columns[ before.X ];
                var right = columns[ after.X ];
                var top = Math.Min( left.Top, right.Top ) - reach;
                var bottom = Math.Max( left.Bottom, right.Bottom ) + reach;
                paths.Add( $"M {N( x )} {N( top )} L {N( x )} {N( bottom )}" );
            }
            else {
                // Split inside one column. Seats run down the column, so the earlier group is
                // above the later one: the line comes down the column's right side, crosses
                // between the two members, and continues down its left side.
                var column = columns[ before.X ];
                var split = ( before.Y + after.Y ) / 2;
                var leftEdge = before.X - halfColumn;
                var rightEdge = before.X + halfColumn;
                paths.Add(
                    $"M {N( rightEdge )} {N( column.Top - reach )} "
                    + $"L {N( rightEdge )} {N( split )} "
                    + $"L {N( leftEdge )} {N( split )} "
                    + $"L {N( leftEdge )} {N( column.Bottom + reach )}" );
            }
        }

        return paths;
    }

    // Path data is markup, not text: a decimal comma would silently break every coordinate.
    private static string N( double value ) =>
        Math.Round( value, 2 ).ToString( System.Globalization.CultureInfo.InvariantCulture );

    // Seat centres, ordered left to right across the whole chamber.
    private static List<(double X, double Y)> Positions( int total ) {
        var perRow = DistributeRows( total );
        var points = new List<(double X, double Y)>( total );

        for ( var row = 0; row < perRow.Length; row++ ) {
            var n = perRow[ row ];
            if ( n == 0 ) continue;

            // The apex of each nested chevron sits above the one inside it; the arms simply
            // run as far as the row's seats reach.
            var apexY = -row * RowSpacing;

            // Every seat sits a half-integer number of steps out from the apex, on both arms
            // and in every row, so all rows share one lattice and the columns line up.
            //
            // Spacing them along the row's own length instead looked equivalent and was not:
            // a row holding an odd number of seats landed on integer steps while an even row
            // landed on half-integer ones, and the two interleaved into columns half the
            // expected distance apart. The dividers, which reach half a column either side,
            // then crossed their neighbours.
            var left = n / 2;
            var right = n - left;
            // Alternate which arm takes the extra seat of an odd row, so the lopsidedness
            // does not accumulate down one side of the chamber.
            if ( row % 2 == 1 ) ( left, right ) = ( right, left );

            for ( var k = 0; k < left; k++ ) {
                var d = ( k + 0.5 ) * SeatSpacing;
                points.Add( ( -d * Math.Cos( ArmAngle ), apexY - d * Math.Sin( ArmAngle ) ) );
            }
            for ( var k = 0; k < right; k++ ) {
                var d = ( k + 0.5 ) * SeatSpacing;
                points.Add( ( d * Math.Cos( ArmAngle ), apexY - d * Math.Sin( ArmAngle ) ) );
            }
        }

        // Ordering by x turns each group into a band running across the rows, which is how the
        // chamber reads in print. Ties break on y so a column fills bottom to top.
        return points.OrderBy( p => p.X ).ThenBy( p => p.Y ).ToList();
    }

    // Seats per row, inner row first. Rows grow outward, so the counts ramp; the remainder is
    // handed out largest-first so the totals match the chamber exactly rather than leaving a
    // member without a seat.
    private static int[] DistributeRows( int total ) {
        var weights = new double[ RowCount ];
        for ( var r = 0; r < RowCount; r++ ) weights[ r ] = RowCount + r;
        var weightSum = weights.Sum();

        var counts = new int[ RowCount ];
        var remainders = new (int Row, double Fraction)[ RowCount ];
        var assigned = 0;

        for ( var r = 0; r < RowCount; r++ ) {
            var exact = total * weights[ r ] / weightSum;
            counts[ r ] = (int)Math.Floor( exact );
            remainders[ r ] = ( r, exact - counts[ r ] );
            assigned += counts[ r ];
        }

        foreach ( var ( row, _ ) in remainders.OrderByDescending( x => x.Fraction ).Take( total - assigned ) )
            counts[ row ]++;

        return counts;
    }

    // Jaa, Ei, then the ballots that decided nothing. Grouping a party's members by how they
    // voted makes a split visible as a block rather than as scattered dots.
    private static int VoteRank( string vote ) => vote switch {
        VoteValue.Yes => 0,
        VoteValue.No => 1,
        VoteValue.Blank => 2,
        VoteValue.Absent => 3,
        _ => 4,
    };

    // Two decimals is well under a pixel at any size this renders at, and keeps the markup
    // from carrying seventeen digits per coordinate across 200 seats.
    private static double Round( double value ) => Math.Round( value, 2 );
}
