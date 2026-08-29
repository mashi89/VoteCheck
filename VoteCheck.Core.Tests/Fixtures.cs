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
    internal static class Fixtures
    {
        public static string Mp1109 => Load("kansanedustaja-1109.json");
        public static string Vote2026_60_1 => Load("aanestys-2026-60-1.json");
        public static string RecentVotes => Load("uusimmat-aanestykset-trimmed.json");
        public static string VoteSearch => Load("search-aanestys-trimmed.json");

        private static string Load(string fileName) =>
            File.ReadAllText(Path.Combine("Fixtures", fileName));
    }
}
