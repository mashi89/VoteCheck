using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Pages;
using static VoteCheckWeb.Pages.WordDiff;

namespace VoteCheckWeb.Tests;

// The changed passages between the committee's wording of a section and a dissent's.
[TestClass]
public class WordDiffTests {

    [TestMethod]
    public void IdenticalTexts_HaveNoHunks() {
        Assert.AreEqual( 0, Hunks( "24 § Rahapelaamisen ikäraja", "24 § Rahapelaamisen ikäraja" ).Count );
    }

    [TestMethod]
    public void ReplacedWord_ReadsOldThenNew() {
        var hunks = Hunks( "ei saa antaa alle 18-vuotiaan pelata", "ei saa antaa alle 20-vuotiaan pelata" );

        Assert.AreEqual( 1, hunks.Count );
        CollectionAssert.AreEqual(
            new[] {
                new Segment( Kind.Same, "ei saa antaa alle" ),
                new Segment( Kind.Removed, "18-vuotiaan" ),
                new Segment( Kind.Added, "20-vuotiaan" ),
                new Segment( Kind.Same, "pelata" ),
            },
            hunks[ 0 ].Segments.ToArray() );
        Assert.IsFalse( hunks[ 0 ].CutBefore );
        Assert.IsFalse( hunks[ 0 ].CutAfter );
    }

    [TestMethod]
    public void DistantChanges_AreSeparateHunksWithContextCut() {
        var words = Enumerable.Range( 1, 60 ).Select( n => $"w{n}" ).ToList();
        var from = string.Join( " ", words );
        var changed = words.ToList();
        changed[ 10 ] = "X";
        changed[ 50 ] = "Y";

        var hunks = Hunks( from, string.Join( " ", changed ), context: 3 );

        Assert.AreEqual( 2, hunks.Count );
        Assert.IsTrue( hunks[ 0 ].CutBefore && hunks[ 0 ].CutAfter );
        Assert.AreEqual( "w8 w9 w10", hunks[ 0 ].Segments[ 0 ].Text, "three words of context before the change" );
        Assert.IsTrue( hunks[ 1 ].Segments.Any( s => s.Kind == Kind.Added && s.Text == "Y" ) );
    }

    [TestMethod]
    public void NearbyChanges_ShareOneHunk() {
        var hunks = Hunks( "a b c d e f g h", "a B c d E f g h", context: 2 );

        Assert.AreEqual( 1, hunks.Count );
    }

    [TestMethod]
    public void EveryWordShown_IsFromOneOfTheTwoTexts() {
        const string from = "Pelaaja voi asettaa itselleen rahansiirtorajan.";
        const string to = "Pelaajan on asetettava itselleen rahansiirtoraja, jota ei voi ylittää.";

        var shown = Hunks( from, to ).SelectMany( h => h.Segments ).SelectMany( s => s.Text.Split( ' ' ) );

        Assert.IsTrue( shown.All( w => from.Split( ' ' ).Contains( w ) || to.Split( ' ' ).Contains( w ) ) );
    }
}
