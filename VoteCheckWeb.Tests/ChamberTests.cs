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
        // The Speaker does not vote, so a full chamber is 199 ballots rather than 200. Rows
        // are laid out along a path rather than split into two arms precisely so an odd count
        // needs no special case, and the row distribution hands out its remainder so no
        // member is rounded away.
        foreach ( var n in new[] { 1, 2, 7, 8, 9, 99, 198, 199, 200, 201, 240 } ) {
            var plan = Chamber.Arrange( Ballots( n ) );
            Assert.AreEqual( n, plan.Seats.Count, $"{n} ballots" );
        }
    }

    [TestMethod]
    public void SeatsRunLeftToRightSoEachGroupIsOneBand() {
        // Seats come back in the order the groups are seated, and their x coordinates must
        // increase with it. That is what makes a group a contiguous band across the rows
        // instead of scattered dots.
        var plan = Chamber.Arrange( Division() );

        for ( var i = 1; i < plan.Seats.Count; i++ )
            Assert.IsTrue( plan.Seats[ i ].X >= plan.Seats[ i - 1 ].X,
                $"seat {i} at x={plan.Seats[ i ].X} sits left of seat {i - 1} at x={plan.Seats[ i - 1 ].X}" );
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
        // The Eduskunta sits in a shallow chevron, not a horseshoe. If this inverts, the arm
        // angle has drifted and the shape has stopped being recognisable.
        var plan = Chamber.Arrange( Division() );
        Assert.IsTrue( plan.Width > plan.Height,
            $"{plan.Width}x{plan.Height} is not a chevron" );
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
