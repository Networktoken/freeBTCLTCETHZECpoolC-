

using XPool.config;
using XPool.core.jsonrpc;

namespace XPool.core.coindintf
{
    public class DaemonResponse<T>
    {
        public JsonRpcException Error { get; set; }
        public T Response { get; set; }
        public AuthenticatedNetworkEndpointConfig Instance { get; set; }
    }
}
