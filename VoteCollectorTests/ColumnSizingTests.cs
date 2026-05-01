using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckGUI;

namespace VoteCollectorTests;

[TestClass]
public class ColumnSizingTests
{
    [DataTestMethod]
    [DataRow( "Kohta" )]
    [DataRow( "Äänestysaihe" )]
    [DataRow( "Ryhmä" )]
    [DataRow( "Käsittely" )]
    [DataRow( "Pääkohta" )]
    public void ContentColumns_UseStar( string columnName )
    {
        Assert.AreEqual( ColumnSizing.Star, ColumnSizingHelper.GetSizing( columnName ) );
    }

    [DataTestMethod]
    [DataRow( "Jaa" )]
    [DataRow( "Ei" )]
    [DataRow( "Tyhjä" )]
    [DataRow( "Poissa" )]
    [DataRow( "AanestysId" )]
    [DataRow( "Puolue" )]
    [DataRow( "Etunimi" )]
    [DataRow( "Sukunimi" )]
    [DataRow( "IstuntoPvm" )]
    public void NarrowColumns_UseFixed( string columnName )
    {
        Assert.AreEqual( ColumnSizing.Fixed, ColumnSizingHelper.GetSizing( columnName ) );
    }
}
