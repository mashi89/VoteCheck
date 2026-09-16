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
        var plan = Chamber.Arrange( Division() );
        var kok = plan.Seats.Where( s => s.Party == "kok" ).Select( s => s.Vote ).ToList();

        // Each value appears in one run: Jaa, then Ei, then the ballots that decided nothing.
        var runs = kok.Where( ( v, i ) => i == 0 || v != kok[ i - 1 ] ).ToList();
        Assert.AreEqual( runs.Count, runs.Distinct().Count(),
            "a vote value appearing twice means the group's block was broken up" );
        Assert.AreEqual( VoteValue.Yes, runs.First() );
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
    public void EveryBoundaryBetweenGroupsGetsExactlyOneLine() {
        var plan = Chamber.Arrange( Division() );
        var groups = plan.Seats.Select( s => s.Party ).Distinct().Count();

        Assert.AreEqual( groups - 1, plan.Dividers.Count,
            "ten groups meet at nine boundaries; a missing line leaves two groups merged" );
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
    public void EachBoundaryIsOneRadialLineAndItSeparatesTheGroupsExactly() {
        // A single spoke suffices because seats are placed in order of decreasing angle and
        // groups are assigned along that order: every member of a group on the left sits at a
        // greater angle than every member of the group to its right. This asserts that
        // ordering directly — it is what makes one line correct rather than approximate.
        var plan = Chamber.Arrange( Division() );

        foreach ( var d in plan.Dividers ) {
            Assert.AreEqual( 1, d.Count( c => c == 'L' ), $"not a single straight line: {d}" );
            Assert.AreEqual( 0, d.Count( c => c == 'A' ), $"a fan boundary needs no arc: {d}" );
        }

        var byGroup = plan.Seats
            .GroupBy( s => s.Party )
            .Select( g => ( Party: g.Key, Min: g.Min( s => s.Angle ), Max: g.Max( s => s.Angle ) ) )
            .OrderByDescending( g => g.Max )
            .ToList();

        for ( var i = 1; i < byGroup.Count; i++ )
            Assert.IsTrue( byGroup[ i ].Max < byGroup[ i - 1 ].Min,
                $"{byGroup[ i ].Party} overlaps {byGroup[ i - 1 ].Party} in angle, so no single "
                + "line could separate them" );
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
