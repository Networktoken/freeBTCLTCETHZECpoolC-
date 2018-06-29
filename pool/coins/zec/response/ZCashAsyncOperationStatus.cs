using System;
using System.Collections.Generic;
using System.Text;
using XPool.core.jsonrpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XPool.Blockchain.ZCash.DaemonResponses
{
    public class ZCashAsyncOperationStatus
    {
        [JsonProperty("id")]
        public string OperationId { get; set; }

        public string Status { get; set; }
        public JToken Result { get; set; }
        public JsonRpcException Error { get; set; }
    }
}
