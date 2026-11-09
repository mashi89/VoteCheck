using System.IO;

namespace VoteCheck.Core.Tests
{
    // Real responses captured from api.eduskunta.fi, used to pin the model shapes to actual
    // API output rather than to assumptions. Copied next to the test assembly at build time.
    //
    //   kansanedustaja-1109.json          GET /kansanedustajat/1109
    //   aanestys-2026-60-1.json           GET /taysistunnot/aanestykset/2026-60-1
    //   uusimmat-aanestykset-trimmed.json GET /taysistunnot/uusimmat-aanestykset
    //                                     (trimmed to 2 votes x 3 ballots for size)
    //   search-aanestys-trimmed.json      POST /search, category "aanestys",
    //                                     2023+ ascending (trimmed likewise)
    //   kansanedustajat-list-trimmed.json GET /kansanedustajat (2 of 1000 records: one
    //                                     sitting, one former, whole records kept)
    //   valtiopaivaasia-he-32-2026.json   GET /valtiopaivaasiat/HE%2032%2F2026%20vp — the
    //                                     matter aanestys-2026-60-1 decides a step of
    //   poytakirja-ptk-108-2026.xml       GET /asiakirjat/edktunnus/EDK-2026-AK-63386/xml —
    //                                     the full minutes of session 108/2026: proposals
    //                                     described in the debate, and accept/reject votes
    //   poytakirja-ptk-39-2024.xml        GET /asiakirjat/edktunnus/EDK-2024-AK-15838/xml —
    //                                     a supplementary budget with a numbered proposal list
    //   poytakirja-ptk-76-2023-trimmed.xml GET /asiakirjat/edktunnus/EDK-2023-AK-48473/xml —
    //                                     the 2024 budget's final session, trimmed to its first
    //                                     four items: the closing confidence vote and two
    //                                     chapters (MuuAsiakohta), each numbered from 1
    internal static class Fixtures
    {
        public static string Mp1109 => Load("kansanedustaja-1109.json");
        public static string MpList => Load("kansanedustajat-list-trimmed.json");
        public static string Vote2026_60_1 => Load("aanestys-2026-60-1.json");
        public static string RecentVotes => Load("uusimmat-aanestykset-trimmed.json");
        public static string VoteSearch => Load("search-aanestys-trimmed.json");
        public static string MatterHe32_2026 => Load("valtiopaivaasia-he-32-2026.json");
        public static string MinutesPtk108_2026 => Load("poytakirja-ptk-108-2026.xml");
        public static string MinutesPtk39_2024 => Load("poytakirja-ptk-39-2024.xml");
        public static string MinutesPtk76_2023Trimmed => Load("poytakirja-ptk-76-2023-trimmed.xml");

        private static string Load(string fileName) =>
            File.ReadAllText(Path.Combine("Fixtures", fileName));
    }
}
