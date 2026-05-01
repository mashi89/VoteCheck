namespace VoteCheckGUI;

internal enum ColumnSizing { Fixed, Star }

internal static class ColumnSizingHelper
{
    internal static ColumnSizing GetSizing( string columnName ) => columnName switch
    {
        "Kohta"        => ColumnSizing.Star,
        "Äänestysaihe" => ColumnSizing.Star,
        "Ryhmä"        => ColumnSizing.Star,
        "Käsittely"    => ColumnSizing.Star,
        "Pääkohta"     => ColumnSizing.Star,
        _              => ColumnSizing.Fixed,
    };
}
