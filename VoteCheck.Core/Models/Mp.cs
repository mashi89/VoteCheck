using Newtonsoft.Json.Linq;

namespace VoteCheck.Core.Models
{
    // Field names follow the Kansanedustaja shape from https://api.eduskunta.fi/openapi.json
    // (see design.md §3.1). Only the fields the v1 roadmap needs are modeled as typed
    // properties; party/electoral-district membership shapes weren't confirmed against a
    // live response, so they're kept raw until verified.
    public sealed class Mp
    {
        public int Henkilonro { get; set; }
        public string? Etunimet { get; set; }
        public string? Sukunimi { get; set; }
        public string? Kutsumanimi { get; set; }
        public string? Kotikunta { get; set; }
        public EdustajantoimenTila? EdustajantoimenTila { get; set; }
        public JToken? ViimeisinEduskuntaryhma { get; set; }
        public JToken? ViimeisinVaalipiiri { get; set; }

        public string DisplayName =>
            $"{(string.IsNullOrWhiteSpace(Kutsumanimi) ? Etunimet : Kutsumanimi)} {Sukunimi}".Trim();
    }
}
