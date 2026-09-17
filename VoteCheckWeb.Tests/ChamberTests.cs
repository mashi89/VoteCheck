using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Data;
using VoteCheckWeb.Pages;

namespace VoteCheckWeb.Tests;

// The chamber diagram is generated geometry, so the properties that make it truthful — every
// member seated exactly once, groups in one band each, nothing drawn outside the box — are
// asserted rather than eyeballed.
[TestClass]
public class ChamberTests {

    private static IndividualVote Ballot( int n, string party, string vote ) =>
        new( n, "Etu" + n, "Suku" + n, party, vote );

    // A division shaped like a real one: ten groups, a Jaa majority, some absences.
    private static List<IndividualVote> Division( int total = 199 ) {
        var groups = new[] {
            ( "kok", 48 ), ( "sd", 43 ), ( "ps", 44 ), ( "kesk", 23 ), ( "vihr", 13 ),
            ( "vas", 11 ), ( "r", 10 ), ( "kd", 5 ), ( "liik", 1 ), ( "tv", 1 ),
        };
        var ballots = new List<IndividualVote>();
        var n = 1;
        foreach ( var ( party, size ) in groups ) {
            for ( var i = 0; i < size && ballots.Count < total; i++ ) {
                var vote = ( i % 7 ) switch {
                    0 => VoteValue.Absent,
                    1 or 2 => VoteValue.No,
                    3 => VoteValue.Blank,
                    _ => VoteValue.Yes,
                };
                ballots.Add( Ballot( n++, party, vote ) );
            }
        }
        return ballots;
    }

    [TestMethod]
    public void EveryMemberIsSeatedExactlyOnce() {
        var ballots = Division();
        var plan = Chamber.Arrange( ballots );

        Assert.AreEqual( ballots.Count, plan.Seats.Count,
            "a member without a seat is a member missing from the picture" );
        CollectionAssert.AreEquivalent(
            ballots.Select( b => b.PersonNumber ).ToList(),
            plan.Seats.Select( s => s.PersonNumber ).ToList() );
    }

    // Exactly n ballots, spread over the groups — the shaped division above tops out at the
    // size of a real chamber, which is not what this is testing.
    private static List<IndividualVote> Ballots( int n ) {
        string[] parties = [ "kok", "sd", "ps", "kesk", "vihr", "vas", "r", "kd" ];
        string[] votes = [ VoteValue.Yes, VoteValue.No, VoteValue.Blank, VoteValue.Absent ];
        return Enumerable.Range( 1, n )
            .Select( i => Ballot( i, parties[ i % parties.Length ], votes[ i % votes.Length ] ) )
            .ToList();
    }

    [TestMethod]
    public void EveryChamberSizeGetsASeatForEachBallot() {
        // The Speaker does not vote, so a full chamber is 199 ballots rather than 200. Seats
        // are apportioned across rows and sectors by largest remainder, so an odd count needs
        // no special case and nobody is rounded away.
        foreach ( var n in new[] { 1, 2, 7, 8, 9, 99, 198, 199, 200, 201, 240 } ) {
            var plan = Chamber.Arrange( Ballots( n ) );
            Assert.AreEqual( n, plan.Seats.Count, $"{n} ballots" );
        }
    }

    [TestMethod]
    public void SeatsRunLeftToRightSoEachGroupIsOneWedge() {
        // Seats come back in the order the groups are seated, running left to right. On a fan
        // that is decreasing angle, not increasing x: at 120° an outer row sits further left
        // than an inner one, so x is not monotonic and never was the property worth asserting.
        var plan = Chamber.Arrange( Division() );

        for ( var i = 1; i < plan.Seats.Count; i++ )
            Assert.IsTrue( plan.Seats[ i ].Angle <= plan.Seats[ i - 1 ].Angle,
                $"seat {i} at {plan.Seats[ i ].Angle:0.0}° sits right of seat {i - 1} "
                + $"at {plan.Seats[ i - 1 ].Angle:0.0}°" );
    }

    [TestMethod]
    public void GroupsSitInTheConventionalLeftToRightOrder() {
        var plan = Chamber.Arrange( Division() );
        var order = plan.Seats.Select( s => s.Party ).Distinct().ToList();

        CollectionAssert.AreEqual(
            new[] { "vas", "sd", "vihr", "kesk", "liik", "r", "kd", "kok", "ps", "tv" },
            order,
            "an unrecognised group sorts last rather than appearing on the left, where its "
            + "position would read as a claim about its politics" );
    }

    [TestMethod]
    public void WithinAGroupTheSidesAreBlocksRatherThanScatter() {
        // Checked along each arc rather than across the whole chamber: a group spans several
        // arcs, and reading them in one angular sweep interleaves the rows, which is the fan
        // working as intended rather than the blocks breaking up.
        var plan = Chamber.Arrange( Division() );

        foreach ( var arc in plan.Seats.Where( s => s.Party == "kok" )
                                 .GroupBy( s => ( s.Row, s.Sector ) ) ) {
            var votes = arc.OrderByDescending( s => s.Angle ).Select( s => s.Vote ).ToList();
            var runs = votes.Where( ( v, i ) => i == 0 || v != votes[ i - 1 ] ).ToList();
            Assert.AreEqual( runs.Count, runs.Distinct().Count(),
                $"a vote value appears twice along row {arc.Key.Row}, so the block was split" );
        }
    }

    [TestMethod]
    public void GroupsDoNotInterleaveAlongAnArc() {
        // Asserted per arc, which is where it has to hold: each arc carries its groups in
        // seating order with no member of another group among them.
        //
        // Not across the whole chamber. Every arc holds a different number of seats, so the
        // groups change over at a different angle in each, and a group's seats in one row can
        // reach past another group's seats in a different row. That is the fan, not a fault —
        // and it is why each arc gets its own boundary line rather than one radial spoke.
        var plan = Chamber.Arrange( Division() );

        foreach ( var arc in plan.Seats.GroupBy( s => ( s.Row, s.Sector ) ) ) {
            var order = arc.OrderByDescending( s => s.Angle ).Select( s => s.Party ).ToList();
            var runs = order.Where( ( p, i ) => i == 0 || p != order[ i - 1 ] ).ToList();
            Assert.AreEqual( runs.Count, runs.Distinct().Count(),
                $"row {arc.Key.Row} sector {arc.Key.Sector} returns to a group it had left: "
                + string.Join( " ", order ) );
        }
    }

    [TestMethod]
    public void LabelsFitInsideTheReportedBox() {
        // Labels are set outside the outer row, so measuring the box from the seats alone cut
        // the ones above the top of the fan clean off — silently, since SVG simply does not
        // draw what falls outside its viewBox.
        var plan = Chamber.Arrange( Division() );

        foreach ( var label in plan.Labels ) {
            Assert.IsTrue( label.Y - 17 >= 0 && label.Y <= plan.Height,
                $"label {label.Text} at y={label.Y} escapes a {plan.Height}-tall viewBox" );
            Assert.IsTrue( label.X > 0 && label.X < plan.Width,
                $"label {label.Text} at x={label.X} escapes a {plan.Width}-wide viewBox" );
        }
    }

    [TestMethod]
    public void NothingIsDrawnOutsideTheReportedBox() {
        var plan = Chamber.Arrange( Division() );

        foreach ( var seat in plan.Seats ) {
            Assert.IsTrue( seat.X - plan.Radius >= 0 && seat.X + plan.Radius <= plan.Width,
                $"x={seat.X} escapes a {plan.Width}-wide viewBox" );
            Assert.IsTrue( seat.Y - plan.Radius >= 0 && seat.Y + plan.Radius <= plan.Height,
                $"y={seat.Y} escapes a {plan.Height}-tall viewBox" );
        }
    }

    [TestMethod]
    public void TheChamberIsWiderThanItIsTall() {
        // The seating spans 25° to 154°, so the fan is broad and shallow rather than a half
        // circle. If this inverts, the angular span has drifted.
        var plan = Chamber.Arrange( Division() );
        Assert.IsTrue( plan.Width > plan.Height,
            $"{plan.Width}x{plan.Height} is not the shape of the chamber" );
    }

    // ---- boundaries between groups -------------------------------------------------

    [TestMethod]
    public void EveryPlaceTwoGroupsMeetOnAnArcGetsALine() {
        // One segment per arc rather than one per boundary: a boundary crosses however many
        // arcs it happens to cross, and each needs its own line.
        var plan = Chamber.Arrange( Division() );

        var changes = plan.Seats
            .GroupBy( s => ( s.Row, s.Sector ) )
            .Sum( arc => arc.OrderByDescending( s => s.Angle )
                            .Select( s => s.Party )
                            .Where( ( p, i ) => i > 0 )
                            .Zip( arc.OrderByDescending( s => s.Angle ).Select( s => s.Party ),
                                  ( a, b ) => a != b )
                            .Count( x => x ) );

        Assert.AreEqual( changes, plan.Dividers.Count );
        Assert.IsTrue( plan.Dividers.Count > 0, "a ten-group chamber has boundaries to draw" );
    }

    [TestMethod]
    public void ASingleGroupHasNothingToDivide() {
        var ballots = Enumerable.Range( 1, 40 )
            .Select( i => Ballot( i, "kok", VoteValue.Yes ) ).ToList();
        Assert.AreEqual( 0, Chamber.Arrange( ballots ).Dividers.Count );
    }

    // The aisles measured from the published seating plan. Nothing may be drawn in them —
    // they are the gangways, and seats standing in one would be the same class of error as
    // the chevron was.
    private static readonly (double From, double To)[] Aisles =
        [ ( 59.2, 65.2 ), ( 88.6, 92.5 ), ( 114.4, 119.6 ) ];

    [TestMethod]
    public void NobodySitsInAnAisle() {
        foreach ( var n in new[] { 99, 150, 198, 199, 200 } ) {
            foreach ( var seat in Chamber.Arrange( Ballots( n ) ).Seats ) {
                Assert.IsTrue( seat.Angle is >= 25 and <= 154,
                    $"{seat.Angle:0.0}° is outside the chamber" );
                foreach ( var ( from, to ) in Aisles )
                    Assert.IsFalse( seat.Angle > from && seat.Angle < to,
                        $"{seat.Angle:0.0}° stands in the aisle between {from}° and {to}°" );
            }
        }
    }

    [TestMethod]
    public void SeatsLieOnEightConcentricRows() {
        // A row is an arc, so every seat in it is the same distance from the centre of the
        // fan — and the eight radii are distinct and increase with the row number.
        var plan = Chamber.Arrange( Division() );
        var centre = Centre( plan.Seats );

        var radii = new Dictionary<int, double>();
        foreach ( var seat in plan.Seats ) {
            var r = Math.Sqrt( Math.Pow( seat.X - centre.X, 2 ) + Math.Pow( seat.Y - centre.Y, 2 ) );
            if ( radii.TryGetValue( seat.Row, out var known ) )
                Assert.AreEqual( known, r, 0.5,
                    $"row {seat.Row} is not a single arc" );
            else
                radii[ seat.Row ] = r;
        }

        Assert.AreEqual( 8, radii.Count, "eight seating rows" );
        var ordered = radii.OrderBy( kv => kv.Key ).Select( kv => kv.Value ).ToList();
        for ( var i = 1; i < ordered.Count; i++ )
            Assert.IsTrue( ordered[ i ] > ordered[ i - 1 ], "rows run outward" );

        Assert.AreEqual( 2.43, ordered.Last() / ordered.First(), 0.02,
            "the outer row sits about 2.4 times the inner radius, as measured from the plan" );
    }

    // The centre of the fan, solved from two seats whose angles differ. Each seat's polar
    // coordinates are known, so two of them determine it exactly.
    private static (double X, double Y) Centre( IReadOnlyList<Seat> seats ) {
        var a = seats[ 0 ];
        var b = seats.First( s => Math.Abs( s.Angle - a.Angle ) > 5 && s.Row == a.Row );
        var ra = a.Angle * Math.PI / 180;
        var rb = b.Angle * Math.PI / 180;
        var radius = ( a.X - b.X ) / ( Math.Cos( ra ) - Math.Cos( rb ) );
        return ( a.X - radius * Math.Cos( ra ), a.Y + radius * Math.Sin( ra ) );
    }

    [TestMethod]
    public void NoBoundaryLineTouchesASeat() {
        // The reason each arc is respaced and gets its own segment. A single spoke per boundary
        // crossed a circle on all seven boundaries of a full chamber — twice through the middle
        // of one — because a seat is about two degrees wide on the inner arcs and the groups
        // change over at a different angle in every row.
        foreach ( var n in new[] { 99, 150, 199, 200 } ) {
            var plan = Chamber.Arrange( Ballots( n ) );

            foreach ( var d in plan.Dividers ) {
                var p = d.Split( ' ' );
                Assert.AreEqual( "M", p[ 0 ] );
                Assert.AreEqual( "L", p[ 3 ] );
                var ( ax, ay, bx, by ) = ( Num( p[ 1 ] ), Num( p[ 2 ] ), Num( p[ 4 ] ), Num( p[ 5 ] ) );

                foreach ( var seat in plan.Seats )
                    Assert.IsTrue( DistanceToSegment( seat.X, seat.Y, ax, ay, bx, by ) >= plan.Radius,
                        $"{n} ballots: a boundary line crosses {seat.Member}'s seat" );
            }
        }
    }

    private static double Num( string s ) =>
        double.Parse( s, System.Globalization.CultureInfo.InvariantCulture );

    private static double DistanceToSegment(
        double px, double py, double ax, double ay, double bx, double by ) {
        double vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
        var t = Math.Clamp( ( wx * vx + wy * vy ) / ( vx * vx + vy * vy ), 0, 1 );
        return Math.Sqrt( Math.Pow( px - ( ax + t * vx ), 2 ) + Math.Pow( py - ( ay + t * vy ), 2 ) );
    }

    [TestMethod]
    public void DividerPathsAreValidInvariantCultureMarkup() {
        var plan = Chamber.Arrange( Division() );

        foreach ( var d in plan.Dividers ) {
            StringAssert.StartsWith( d, "M " );
            Assert.IsFalse( d.Contains( ',' ),
                $"a decimal comma would silently break the path: {d}" );
            foreach ( var token in d.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
                Assert.IsTrue( token is "M" or "L" or "A"
                    || double.TryParse( token, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out _ ),
                    $"unexpected token '{token}' in {d}" );
        }
    }

    [TestMethod]
    public void ADivisionWithNoBallotsDrawsNothing() {
        var plan = Chamber.Arrange( [] );
        Assert.IsTrue( plan.IsEmpty );
        Assert.AreEqual( 0, plan.Seats.Count );
    }

    [TestMethod]
    public void SeatingRank_PlacesKnownGroupsInOrderAndStrangersLast() {
        Assert.IsTrue( Party.SeatingRank( "vas" ) < Party.SeatingRank( "kesk" ) );
        Assert.IsTrue( Party.SeatingRank( "kesk" ) < Party.SeatingRank( "ps" ) );
        Assert.IsTrue( Party.SeatingRank( "ps" ) < Party.SeatingRank( "tv" ) );
        Assert.AreEqual( Party.SeatingRank( "xyz" ), Party.SeatingRank( "tv" ) );
        Assert.AreEqual( Party.SeatingRank( "KOK" ), Party.SeatingRank( " kok " ) );
    }
}
