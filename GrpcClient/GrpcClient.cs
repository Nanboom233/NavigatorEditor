using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using NavigatorEditor.Proto;

namespace NavigatorEditor.GrpcClient
{
    public sealed class GrpcClient : IAsyncDisposable
    {
        private readonly GrpcChannel _channel;
        private readonly GrpcService.GrpcServiceClient _client;
        private readonly Metadata _headers;
        private CancellationTokenSource? _cts;
        private Task? _heartbeatTask;
        private Task? _logTask;

        public event Action<string, long>? OnLog; // message, timestamp
        public event Action<string>? OnDisconnected; // reason

        private GrpcClient(Uri address)
        {
            var socketSettings = new SocketsHttpHandler
            {
                KeepAlivePingDelay = TimeSpan.FromSeconds(10), // ping every 10 seconds
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5), // max wait 5 seconds for ping ack
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always // make sure to always ping
            };
            _channel =  GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = socketSettings,
                MaxReceiveMessageSize = 64 * 1024 * 1024 // 64MB
            });
            _client = new GrpcService.GrpcServiceClient(_channel);
            _headers = new Metadata();
        }

        /// <summary>
        /// Create a GrpcClient and verify connection with heartbeat.
        /// If heartbeat fails, an exception will be thrown
        /// </summary>
        /// <param name="address">An address like http://localhost:1234</param>
        /// <exception cref="System.Net.Sockets.SocketException">Thrown if heartbeat failed</exception>
        /// <returns>A GrpcClient instance</returns>
        public static async Task<GrpcClient> CreateClient(Uri address)
        {
            var tmpClient = new GrpcClient(address);
            await tmpClient.Heartbeat();
            tmpClient.StartMonitoring();
            return tmpClient;
        }
        
        public async Task<CommandResponse> Command(string command)
        {
            try
            {
                // Send metadata headers if present
                var callOptions = _headers.Count > 0 ? new CallOptions(headers: _headers) : default;
                return await _client.commandAsync(new CommandRequest { Command = command }, callOptions);
            }
            catch (RpcException ex)
            {
                Debug.WriteLine($"RPC Error: {ex.Status}");
                throw;
            }
        }

        public async Task<HeartbeatPacket> Heartbeat()
        {
            var callOptions = _headers.Count > 0 ? new CallOptions(headers: _headers) : default;
            return await _client.heartbeatAsync(new Empty(),callOptions);
        }
        
        private void StartMonitoring()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _heartbeatTask = Task.Run(async () =>
            {
                // Send heartbeat every 3 seconds, if 2 consecutive failures, disconnect
                int failCount = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var hb = await Heartbeat();
                        var latency = hb.Timestamp - begin;
                        failCount = 0; // reset
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException) break;
                        failCount++;
                        if (failCount >= 2)
                        {
                            OnDisconnected?.Invoke($"Heartbeat failed: {ex.Message}"); 
                            _ = DisposeAsync();
                            break;
                        }
                        try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch { }
                    }
                }
            }, token);

            _logTask = Task.Run(async () =>
            {
                var callOptions = _headers.Count > 0 ? new CallOptions(headers: _headers) : default;
                using var call = _client.subscribe_logs(new Empty(), callOptions);
                try
                {
                    while (await call.ResponseStream.MoveNext(token))
                    {
                        var entry = call.ResponseStream.Current;
                        OnLog?.Invoke(entry.Message, entry.Timestamp);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException || ex is RpcException { StatusCode: StatusCode.Cancelled }) 
                    { 
                        return; 
                    }
                    if (!token.IsCancellationRequested)
                    {
                        OnDisconnected?.Invoke($"Log stream error: {ex.Message}");
                        _ = DisposeAsync();
                    }
                }
            }, token);
        }

        private async Task ShutdownAsync()
        {
            _cts?.Cancel();
            try { if (_logTask != null) await _logTask; } catch { }
            try { if (_heartbeatTask != null) await _heartbeatTask; } catch { }
            await _channel.ShutdownAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await ShutdownAsync();
        }
    }
    
}
