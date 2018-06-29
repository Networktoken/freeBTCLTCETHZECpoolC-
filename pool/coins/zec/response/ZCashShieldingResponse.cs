using XPool.core.jsonrpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XPool.Blockchain.ZCash.DaemonResponses
{
    public class ZCashShieldingResponse
    {
        [JsonProperty("opid")]
        public string OperationId { get; set; }
    }
}
