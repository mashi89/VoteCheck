using System.Globalization;

namespace VoteCheckWeb.Pages;

// Display formatting shared by the pages. Separate from Meta, which shapes text for social
// cards rather than for the page itself.
public static class Format {

    private const string IsoDate = "yyyy-MM-dd";

    // Upstream dates arrive as ISO, sometimes with a time and a +03:00 offset attached. Only
    // the calendar date is ever shown, and it is taken literally rather than parsed as an
    // instant: a sitting on 11 September is that date in Helsinki, and converting the
    // timestamp to the server's zone would move a late-evening or early-morning division to
    // the neighbouring day. DateOnly keeps the conversion from being possible at all.
    //
    // Formatted against the invariant culture on purpose: the output must not change with
    // whatever locale the container happens to boot with.
    public static string Date( string? isoDate ) {
        var iso = Iso( isoDate );
        return DateOnly.TryParseExact( iso, IsoDate, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var parsed )
            ? parsed.ToString( "d.M.yyyy", CultureInfo.InvariantCulture )
            : iso;
    }

    // The leading calendar date, for datetime attributes and for anything that sorts.
    // Anything that is not a date comes back untouched — truncating it to ten characters
    // would turn unexpected text into a plausible-looking fragment.
    public static string Iso( string? isoDate ) {
        var text = ( isoDate ?? "" ).Trim();
        if ( text.Length < 10 ) return text;

        var head = text[ ..10 ];
        return DateOnly.TryParseExact( head, IsoDate, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out _ )
            ? head
            : text;
    }
}
