using System.Globalization;

namespace VoteCheckWeb.Pages;

// Display formatting shared by the pages. Separate from Meta, which shapes text for social
// cards rather than for the page itself.
public static class Format {

    private const string IsoDate = "yyyy-MM-dd";

    // Finnish writes 95,1 — not 95.1. Built explicitly rather than taken from a "fi-FI"
    // culture, for the same reason the dates are: the result must not depend on what the
    // container happens to provide. A runtime without ICU, or one started with
    // InvariantGlobalization, hands back the invariant culture for any name you ask for and
    // the separator silently becomes a full stop.
    private static readonly NumberFormatInfo FinnishNumbers = new() {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",   // non-breaking space, as Finnish uses
        PercentDecimalSeparator = ",",
        PercentGroupSeparator = " ",
    };

    // A percentage to one decimal, or a whole number when it is exactly round — "100 %"
    // rather than "100,0 %", which reads as false precision on a figure that is not.
    public static string Percent( double value ) {
        var rounded = Math.Round( value, 1 );
        return rounded == Math.Truncate( rounded )
            ? rounded.ToString( "0", FinnishNumbers )
            : rounded.ToString( "0.0", FinnishNumbers );
    }

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
