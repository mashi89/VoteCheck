using System;
using System.Collections.Generic;

namespace VoteCheck.Core.Models
{
    // One window of an enumeration over the vote archive, plus enough context for a caller
    // to resume. Used by the mirror sync, which walks the archive once and then tails it.
    public sealed class VotePage
    {
        public IReadOnlyList<Aanestys> Votes { get; init; } = Array.Empty<Aanestys>();

        // Total matching the query upstream, not the size of this page.
        public int TotalCount { get; init; }

        // Index this page started at, echoed back so a cursor can be stored.
        public int StartIndex { get; init; }

        public bool HasMore => StartIndex + Votes.Count < TotalCount;
    }
}
