using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Tests;

[TestClass]
public class VoteValueTests {

    [TestMethod]
    public void Normalize_MapsPartitiveBlankToCanonicalForm() {
        // Upstream spells the blank vote "Tyhjää"; everything downstream expects "Tyhjä".
        // Real data confirms this is not hypothetical — 267 such ballots in the sample alone.
        Assert.AreEqual( VoteValue.Blank, VoteValue.Normalize( "Tyhjää" ) );
    }

    [TestMethod]
    public void Normalize_LeavesAlreadyCanonicalValuesAlone() {
        Assert.AreEqual( VoteValue.Yes, VoteValue.Normalize( "Jaa" ) );
        Assert.AreEqual( VoteValue.No, VoteValue.Normalize( "Ei" ) );
        Assert.AreEqual( VoteValue.Blank, VoteValue.Normalize( "Tyhjä" ) );
        Assert.AreEqual( VoteValue.Absent, VoteValue.Normalize( "Poissa" ) );
    }

    [TestMethod]
    public void Normalize_TrimsPadding() {
        Assert.AreEqual( VoteValue.Yes, VoteValue.Normalize( "  Jaa " ) );
        Assert.AreEqual( VoteValue.Blank, VoteValue.Normalize( " Tyhjää" ) );
    }

    [TestMethod]
    public void Normalize_TreatsNullAndEmptyAsEmpty() {
        Assert.AreEqual( "", VoteValue.Normalize( null ) );
        Assert.AreEqual( "", VoteValue.Normalize( "   " ) );
    }

    [TestMethod]
    public void Normalize_PassesUnknownValuesThroughRatherThanDroppingThem() {
        // Dropping a ballot would silently skew a party tally; an unexpected value stays
        // visible in the data instead.
        Assert.AreEqual( "Jaa?", VoteValue.Normalize( " Jaa? " ) );
    }
}
