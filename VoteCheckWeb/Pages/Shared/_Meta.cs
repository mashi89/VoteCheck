namespace VoteCheckWeb.Pages;

// Helpers for social-card text. Feeds truncate hard — roughly 90 characters for a title
// and 160 for a description — and a card cut mid-word reads as broken, so we cut on a
// word boundary and say so with an ellipsis.
public static class Meta {

    public static string Truncate( string? text, int max ) {
        text = ( text ?? "" ).Trim();
        if ( text.Length <= max ) return text;

        var cut = text[..max];
        var lastSpace = cut.LastIndexOf( ' ' );
        // Only honour the word boundary if it is not so early that we lose most of the text.
        if ( lastSpace > max / 2 ) cut = cut[..lastSpace];
        return cut.TrimEnd( ' ', ',', '.', ';', ':', '-' ) + "…";
    }
}
