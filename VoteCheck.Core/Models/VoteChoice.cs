namespace VoteCheck.Core.Models
{
    // How an MP voted in a single division.
    //
    // Parsed from EdustajanAanestys.Kayttaytyminen, which upstream is a bilingual object whose
    // Finnish values are "Jaa" / "Ei" / "Tyhjä" / "Poissa". Jaa, Ei and Poissa are confirmed in
    // the captured fixture; Tyhja is carried over from the legacy API's vocabulary and matches
    // the "tyhjia" tally field, but no abstention appears in that sample.
    public enum VoteChoice
    {
        // The value did not match any known vocabulary term — treated as not-counted rather
        // than silently folded into another bucket.
        Unknown = 0,
        Jaa,
        Ei,
        Tyhja,
        Poissa,
    }
}
