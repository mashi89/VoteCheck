using System.Collections.Generic;

namespace VoteCheck.Core.Models
{
    // A single voting result, as returned by the taysistunnot/*aanestykset* endpoints.
    // Field names and shapes confirmed against a live response (see the fixture in
    // VoteCheck.Core.Tests/Fixtures/aanestys-2026-60-1.json).
    //
    // Every vote payload embeds the full ballot list (aanestystapahtumat — one entry per
    // seated MP) alongside the pre-computed jakauma breakdowns, so per-MP vote history can
    // be derived by filtering these rather than needing a separate per-MP endpoint.
    public sealed class Aanestys
    {
        // Vote id, formatted istuntovpvuosi-istuntonumero-aanestysnumero, e.g. "2026-60-1".
        public string? Id { get; set; }

        // Session id, formatted istuntovpvuosi-istuntonumero, e.g. "2026-60".
        public string? IstunnonTunniste { get; set; }

        // Describes the ballot options ("proposal X JAA / proposal Y EI"), not the subject —
        // for the subject of the vote use Kohta.Otsikko.
        public LocalizedText? Aanestysotsikko { get; set; }
        public LocalizedText? Paivajarjestyksenotsikko { get; set; }

        // Upstream sends these as JSON strings.
        public string? Istuntovpvuosi { get; set; }
        public string? Istuntonumero { get; set; }
        public string? Aanestysnumero { get; set; }

        // Timestamps are ISO 8601 with offset (e.g. "2026-06-03T14:01:43.667+03:00"), except
        // Istuntopvm which is a date with offset ("2026-06-03+03:00") and does not parse as a
        // DateTimeOffset — kept as strings so no parsing assumptions are baked in here.
        public string? Ilmoitettualkuaika { get; set; }
        public string? Istuntopvm { get; set; }
        public string? Istuntoalkuaika { get; set; }
        public string? Aanestysalkuaika { get; set; }
        public string? Aanestysloppuaika { get; set; }

        public bool Aanestysmitatoity { get; set; }
        public Kohta? Kohta { get; set; }
        public Puhemies? Puhemies { get; set; }

        public AanestysTulos? Aanestystulos { get; set; }
        public List<EdustajanAanestys> Aanestystapahtumat { get; set; } = new();
        public List<Aanestysjakauma> EduskuntaryhmaJakaumat { get; set; } = new();
        public List<Aanestysjakauma> HallitusoppositioJakaumat { get; set; } = new();
        public List<Aanestysjakauma> VaalipiiriJakaumat { get; set; } = new();
    }
}
