using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VoteCheck.Core.Models
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EdustajantoimenTila
    {
        Nykyinen,
        Keskeytynyt,
        Entinen,
    }
}
