

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Text;
using Autofac;
using XPool.utils;
using XPool.config;
using XPool.core.jsonrpc;
using NetUV.Core.Handles;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Assertion = XPool.utils.Assertion;

namespace XPool.core.stratumproto
{
    public class StratumClient
    {
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private const int MaxInboundRequestLength = 0x8000;
        private const int MaxOutboundRequestLength = 0x8000;

        private ConcurrentQueue<PooledArraySegment<byte>> sendQueue;
        private Async sendQueueDrainer;
        private readonly PooledLineBuffer plb = new PooledLineBuffer(logger, MaxInboundRequestLength);
        private IDisposable subscription;
        private bool isAlive = true;
        private WorkerContextBase context;
        private bool expectingProxyProtocolHeader = false;

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        #region API-Surface

        public void Init(Loop loop, Tcp tcp, IComponentContext ctx, IMasterClock clock,
            (IPEndPoint IPEndPoint, TcpProxyProtocolConfig ProxyProtocol) endpointConfig, string connectionId,
            Action<PooledArraySegment<byte>> onNext, Action onCompleted, Action<Exception> onError)
        {
            PoolEndpoint = endpointConfig.IPEndPoint;
            ConnectionId = connectionId;
            RemoteEndpoint = tcp.GetPeerEndPoint();

            expectingProxyProtocolHeader = endpointConfig.ProxyProtocol?.Enable == true;

                        sendQueue = new ConcurrentQueue<PooledArraySegment<byte>>();
            sendQueueDrainer = loop.CreateAsync(DrainSendQueue);
            sendQueueDrainer.UserToken = tcp;

                        var sub = Disposable.Create(() =>
            {
                if (tcp.IsValid)
                {
                    logger.Debug(() => $"[{ConnectionId}] Last subscriber disconnected from receiver stream");

                    isAlive = false;
                    tcp.Shutdown();
                }
            });

                        var disposer = loop.CreateAsync((handle) =>
            {
                sub.Dispose();

                handle.Dispose();
            });

            subscription = Disposable.Create(() => { disposer.Send(); });

                        Receive(tcp, endpointConfig.ProxyProtocol, clock, onNext, onCompleted, onError);
        }

        public string ConnectionId { get; private set; }
        public IPEndPoint PoolEndpoint { get; private set; }
        public IPEndPoint RemoteEndpoint { get; private set; }
        public DateTime? LastReceive { get; set; }
        public bool IsAlive { get; set; } = true;

        public void SetContext<T>(T value) where T : WorkerContextBase
        {
            context = value;
        }

        public T GetContextAs<T>() where T: WorkerContextBase
        {
            return (T) context;
        }

        public void Respond<T>(T payload, object id)
        {
            Assertion.RequiresNonNull(payload, nameof(payload));
            Assertion.RequiresNonNull(id, nameof(id));

            Respond(new JsonRpcResponse<T>(payload, id));
        }

        public void RespondError(StratumError code, string message, object id, object result = null, object data = null)
        {
            Assertion.RequiresNonNull(message, nameof(message));

            Respond(new JsonRpcResponse(new JsonRpcException((int) code, message, null), id, result));
        }

        public void Respond<T>(JsonRpcResponse<T> response)
        {
            Assertion.RequiresNonNull(response, nameof(response));

            Send(response);
        }

        public void Notify<T>(string method, T payload)
        {
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(method), $"{nameof(method)} must not be empty");

            Notify(new JsonRpcRequest<T>(method, payload, null));
        }

        public void Notify<T>(JsonRpcRequest<T> request)
        {
            Assertion.RequiresNonNull(request, nameof(request));

            Send(request);
        }

        public void Send<T>(T payload)
        {
            Assertion.RequiresNonNull(payload, nameof(payload));

            if (isAlive)
            {
                var buf = ArrayPool<byte>.Shared.Rent(MaxOutboundRequestLength);

                try
                {
                    using (var stream = new MemoryStream(buf, true))
                    {
                        stream.SetLength(0);
                        int size;

                        using (var writer = new StreamWriter(stream, StratumConstants.Encoding))
                        {
                            serializer.Serialize(writer, payload);
                            writer.Flush();

                                                        stream.WriteByte(0xa);
                            size = (int)stream.Position;
                        }

                        logger.Trace(() => $"[{ConnectionId}] Sending: {StratumConstants.Encoding.GetString(buf, 0, size)}");

                        SendInternal(new PooledArraySegment<byte>(buf, 0, size));
                    }
                }

                catch (Exception)
                {
                    ArrayPool<byte>.Shared.Return(buf);
                    throw;
                }
            }
        }

        public void Disconnect()
        {
            subscription?.Dispose();
            subscription = null;

            IsAlive = false;
        }

        public void RespondError(object id, int code, string message)
        {
            Assertion.RequiresNonNull(id, nameof(id));
            Assertion.Requires<ArgumentException>(!string.IsNullOrEmpty(message), $"{nameof(message)} must not be empty");

            Respond(new JsonRpcResponse(new JsonRpcException(code, message, null), id));
        }

        public void RespondUnsupportedMethod(object id)
        {
            Assertion.RequiresNonNull(id, nameof(id));

            RespondError(id, 20, "Unsupported method");
        }

        public void RespondUnauthorized(object id)
        {
            Assertion.RequiresNonNull(id, nameof(id));

            RespondError(id, 24, "Unauthorized worker");
        }

        public JsonRpcRequest DeserializeRequest(PooledArraySegment<byte> data)
        {
            using (var stream = new MemoryStream(data.Array, data.Offset, data.Size))
            {
                using (var reader = new StreamReader(stream, StratumConstants.Encoding))
                {
                    using (var jreader = new JsonTextReader(reader))
                    {
                        return serializer.Deserialize<JsonRpcRequest>(jreader);
                    }
                }
            }
        }

        #endregion 
        private void Receive(Tcp tcp, TcpProxyProtocolConfig proxyProtocol, IMasterClock clock,
            Action<PooledArraySegment<byte>> onNext, Action onCompleted, Action<Exception> onError)
        {
            tcp.OnRead((handle, buffer) =>
            {
                                using (buffer)
                {
                    if (buffer.Count == 0 || !isAlive)
                        return;

                    LastReceive = clock.Now;

                    var onLineReceived = !expectingProxyProtocolHeader ?
                        onNext :
                        (lineData) =>
                        {
                                                        if (expectingProxyProtocolHeader)
                            {
                                expectingProxyProtocolHeader = false;
                                var peerAddress = tcp.GetPeerEndPoint().Address;

                                                                var line = Encoding.ASCII.GetString(lineData.Array, lineData.Offset, lineData.Size);

                                if (line.StartsWith("PROXY "))
                                {
                                    using(lineData)
                                    {
                                        var proxyAddresses = proxyProtocol.ProxyAddresses?.Select(x => IPAddress.Parse(x)).ToArray();
                                        if (proxyAddresses == null || !proxyAddresses.Any())
                                            proxyAddresses = new[] { IPAddress.Loopback };

                                        if (proxyAddresses.Any(x=> x.Equals(peerAddress)))
                                        {
                                            logger.Debug(() => $"[{ConnectionId}] Received Proxy-Protocol header: {line}");

                                                                                        var parts = line.Split(" ");
                                            var remoteAddress = parts[2];
                                            var remotePort = parts[4];

                                                                                        RemoteEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), int.Parse(remotePort));
                                            logger.Info(() => $"[{ConnectionId}] Real-IP via Proxy-Protocol: {RemoteEndpoint.Address}");
                                        }

                                        else
                                        {
                                            logger.Error(() => $"[{ConnectionId}] Received spoofed Proxy-Protocol header from {peerAddress}");
                                            Disconnect();
                                        }
                                    }

                                    return;
                                }

                                else if (proxyProtocol.Mandatory)
                                {
                                    logger.Error(() => $"[{ConnectionId}] Missing mandatory Proxy-Protocol header from {peerAddress}. Closing connection.");
                                    lineData.Dispose();
                                    Disconnect();
                                    return;
                                }
                            }

                                                        onNext(lineData);
                        };

                    plb.Receive(buffer, buffer.Count,
                        (src, dst, count) => src.ReadBytes(dst, count),
                        onLineReceived,
                        onError);
                }
            }, (handle, ex) =>
            {
                                onError(ex);
            }, handle =>
            {
                                isAlive = false;
                onCompleted();

                                sendQueueDrainer.UserToken = null;
                sendQueueDrainer.Dispose();

                                while (sendQueue.TryDequeue(out var fragment))
                    fragment.Dispose();

                plb.Dispose();

                handle.CloseHandle();
            });
        }

        private void SendInternal(PooledArraySegment<byte> buffer)
        {
            try
            {
                sendQueue.Enqueue(buffer);
                sendQueueDrainer.Send();
            }

            catch (ObjectDisposedException)
            {
                buffer.Dispose();
            }
        }

        private void DrainSendQueue(Async handle)
        {
            try
            {
                var tcp = (Tcp)handle.UserToken;

                if (tcp?.IsValid == true && !tcp.IsClosing && tcp.IsWritable && sendQueue != null)
                {
                    var queueSize = sendQueue.Count;
                    if (queueSize >= 256)
                        logger.Warn(() => $"[{ConnectionId}] Send queue backlog now at {queueSize}");

                    while (sendQueue.TryDequeue(out var segment))
                    {
                        using (segment)
                        {
                            tcp.QueueWrite(segment.Array, 0, segment.Size);
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
    }
}
