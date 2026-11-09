namespace VoteCheckWeb.Pages;

// Where two wordings of the same section differ, word by word, as a reader needs it: the
// changed passages with a few words either side, not two near-identical walls of legal text.
//
// A section vote puts the committee's wording against a dissent's, and the two usually share
// most of their words. Shown whole, the difference is left for the reader to find; here it is
// the first thing they see. The words themselves are never altered — every word shown is from
// one of the two texts, marked as kept, removed or added.
public static class WordDiff {

    public enum Kind { Same, Removed, Added }

    public sealed record Segment( Kind Kind, string Text );

    // One changed passage with its surrounding context.
    public sealed record Hunk( IReadOnlyList<Segment> Segments, bool CutBefore, bool CutAfter );

    // Above this many words on either side the comparison is not attempted: the table grows
    // with the product of the two lengths, and a section that long is better read whole.
    private const int MaxWords = 1500;

    public static IReadOnlyList<Hunk> Hunks( string from, string to, int context = 6 ) {
        var a = Words( from );
        var b = Words( to );
        if ( a.Length > MaxWords || b.Length > MaxWords ) return [];

        var ops = Compare( a, b );
        if ( ops.All( o => o.Kind == Kind.Same ) ) return [];

        // Indices of changed operations, grown by the context on each side, then merged
        // wherever two changes are close enough that their context would overlap.
        var hunks = new List<Hunk>();
        var i = 0;
        while ( i < ops.Count ) {
            if ( ops[ i ].Kind == Kind.Same ) { i++; continue; }

            var start = Math.Max( 0, i - context );
            var end = i;
            var sameRun = 0;
            for ( var j = i; j < ops.Count; j++ ) {
                if ( ops[ j ].Kind == Kind.Same ) {
                    if ( ++sameRun > 2 * context ) break;
                } else {
                    sameRun = 0;
                    end = j;
                }
            }
            var stop = Math.Min( ops.Count - 1, end + context );

            hunks.Add( new Hunk( Merge( ops.Skip( start ).Take( stop - start + 1 ) ), start > 0, stop < ops.Count - 1 ) );
            i = stop + 1;
        }
        return hunks;
    }

    // Longest common subsequence over words; ties prefer removals before additions so a
    // replaced word reads "old → new".
    private static List<(Kind Kind, string Word)> Compare( string[] a, string[] b ) {
        var lcs = new int[ a.Length + 1, b.Length + 1 ];
        for ( var x = a.Length - 1; x >= 0; x-- )
            for ( var y = b.Length - 1; y >= 0; y-- )
                lcs[ x, y ] = a[ x ] == b[ y ] ? lcs[ x + 1, y + 1 ] + 1 : Math.Max( lcs[ x + 1, y ], lcs[ x, y + 1 ] );

        var ops = new List<(Kind, string)>();
        int p = 0, q = 0;
        while ( p < a.Length && q < b.Length ) {
            if ( a[ p ] == b[ q ] ) { ops.Add( ( Kind.Same, a[ p ] ) ); p++; q++; }
            else if ( lcs[ p + 1, q ] >= lcs[ p, q + 1 ] ) { ops.Add( ( Kind.Removed, a[ p ] ) ); p++; }
            else { ops.Add( ( Kind.Added, b[ q ] ) ); q++; }
        }
        while ( p < a.Length ) ops.Add( ( Kind.Removed, a[ p++ ] ) );
        while ( q < b.Length ) ops.Add( ( Kind.Added, b[ q++ ] ) );
        return ops;
    }

    // Consecutive words of the same kind become one segment, so the page marks a passage
    // rather than each word in it.
    private static IReadOnlyList<Segment> Merge( IEnumerable<(Kind Kind, string Word)> ops ) {
        var segments = new List<Segment>();
        foreach ( var (kind, word) in ops ) {
            if ( segments.Count > 0 && segments[ ^1 ].Kind == kind )
                segments[ ^1 ] = segments[ ^1 ] with { Text = segments[ ^1 ].Text + " " + word };
            else
                segments.Add( new Segment( kind, word ) );
        }
        return segments;
    }

    private static string[] Words( string text ) =>
        text.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
}
