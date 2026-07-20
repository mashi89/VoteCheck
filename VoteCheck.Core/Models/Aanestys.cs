using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VoteCheck.Core.Models
{
    // A single voting result, as returned by the taysistunnot/*aanestykset* endpoints.
    // Field names follow the shape described in design.md §3.1. Fields whose nested shape
    // wasn't confirmed (kohta, puhemies) are kept raw (JToken) rather than guessed at.
    public sealed class Aanestys
    {
        public string? Id { get; set; }
        public string? IstunnonTunniste { get; set; }
        public LocalizedText? Aanestysotsikko { get; set; }
        public LocalizedText? Paivajarjestyksenotsikko { get; set; }
        public string? Istuntopvm { get; set; }
        public string? Istuntoalkuaika { get; set; }
        public string? Aanestysalkuaika { get; set; }
        public string? Aanestysloppuaika { get; set; }
        public bool Aanestysmitatoity { get; set; }
        public JToken? Kohta { get; set; }
        public JToken? Puhemies { get; set; }

        public AanestysTulos? Aanestystulos { get; set; }
        public List<EdustajanAanestys> Aanestystapahtumat { get; set; } = new();
        public List<Aanestysjakauma> EduskuntaryhmaJakaumat { get; set; } = new();
        public List<Aanestysjakauma> HallitusoppositioJakaumat { get; set; } = new();
        public List<Aanestysjakauma> VaalipiiriJakaumat { get; set; } = new();
    }
}
