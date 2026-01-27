#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using PAK.Client.Network.Model;
using PAK.Client.Network.Transports;
using PAK.Client.Utils;
using UnityEngine;

namespace PAK.Client.Network
{
    public sealed class NetworkManager : MonoBehaviour
    {
        public event Action<string>? GrpcEchoReceived;
        public event Action<string>? UdpEchoReceived;
        public event Action<string>? NetworkError;

        private NetworkConfig? _config;
        private GrpcEchoClientTransport? _grpcTransport;
        private UdpEchoTransport? _udpTransport;
        private CancellationTokenSource? _cts;
        private bool _udpAwaiting;
        private DateTime _udpTimeoutAtUtc;
        private string _udpAwaitMessage = string.Empty;

        public bool IsGrpcConnected => _grpcTransport?.IsConnected ?? false;
        public bool IsUdpConnected => _udpTransport?.IsConnected ?? false;

        public void Initialize(NetworkConfig config)
        {
            _config = config;
            _grpcTransport = new GrpcEchoClientTransport(config);
            _udpTransport = new UdpEchoTransport(config);

            _grpcTransport.MessageReceived += HandleGrpcMessage;
            _grpcTransport.Error += HandleTransportError;

            _udpTransport.MessageReceived += HandleUdpMessage;
            _udpTransport.Error += HandleTransportError;
        }

        private void Update()
        {
            _grpcTransport?.Poll();
            _udpTransport?.Poll();

            if (_udpAwaiting && DateTime.UtcNow > _udpTimeoutAtUtc)
            {
                _udpAwaiting = false;
                var error = $"[NET][UDP] Timeout waiting for echo: {_udpAwaitMessage}";
                Debug.LogError(error);
                NetworkError?.Invoke(error);
            }
        }

        public async Task ConnectAsync()
        {
            if (_config == null || _grpcTransport == null || _udpTransport == null)
            {
                MainThreadDispatcher.Enqueue(() => Debug.LogError("[NET] Missing NetworkConfig or transports."));
                return;
            }

            _cts = new CancellationTokenSource();

            try
            {
                await _grpcTransport.ConnectAsync(_cts.Token).ConfigureAwait(false);
                MainThreadDispatcher.Enqueue(() => Debug.Log($"[NET][GRPC] Connect {_config.GrpcAddress}"));
            }
            catch (Exception ex)
            {
                var error = $"[NET][GRPC] Connect error ({_config.GrpcAddress}): {ex}";
                MainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError(error);
                    NetworkError?.Invoke(error);
                });
            }

            try
            {
                await _udpTransport.ConnectAsync(_cts.Token).ConfigureAwait(false);
                MainThreadDispatcher.Enqueue(() => Debug.Log($"[NET][UDP] Connect {_config.UdpHost}:{_config.UdpPort}"));
            }
            catch (Exception ex)
            {
                var error = $"[NET][UDP] Connect error ({_config.UdpHost}:{_config.UdpPort}): {ex}";
                MainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError(error);
                    NetworkError?.Invoke(error);
                });
            }
        }

        public async Task SendGrpcEchoAsync(string message)
        {
            if (_grpcTransport == null)
            {
                MainThreadDispatcher.Enqueue(() => Debug.LogError("[NET][GRPC] Transport missing."));
                return;
            }

            if (!_grpcTransport.IsConnected)
            {
                MainThreadDispatcher.Enqueue(() =>
                    Debug.LogError($"[NET][GRPC] Not connected ({_config?.GrpcAddress})."));
                return;
            }

            MainThreadDispatcher.Enqueue(() => Debug.Log($"[NET][GRPC] Echo request: {message}"));

            try
            {
                await _grpcTransport.SendAsync(message, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var error = $"[NET][GRPC] Send error ({_config?.GrpcAddress}): {ex}";
                MainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError(error);
                    NetworkError?.Invoke(error);
                });
            }
        }

        public async Task SendUdpEchoAsync(string message)
        {
            if (_udpTransport == null || _config == null)
            {
                MainThreadDispatcher.Enqueue(() => Debug.LogError("[NET][UDP] Transport or config missing."));
                return;
            }

            if (!_udpTransport.IsConnected)
            {
                MainThreadDispatcher.Enqueue(() =>
                    Debug.LogError($"[NET][UDP] Not connected ({_config.UdpHost}:{_config.UdpPort})."));
                return;
            }

            _udpAwaiting = true;
            _udpAwaitMessage = message;
            _udpTimeoutAtUtc = DateTime.UtcNow.AddSeconds(_config.UdpEchoTimeoutSeconds);

            MainThreadDispatcher.Enqueue(() => Debug.Log($"[NET][UDP] Send {message}"));

            try
            {
                await _udpTransport.SendAsync(message, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _udpAwaiting = false;
                var error = $"[NET][UDP] Send error ({_config?.UdpHost}:{_config?.UdpPort}): {ex}";
                MainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError(error);
                    NetworkError?.Invoke(error);
                });
            }
        }

        private void HandleGrpcMessage(string message)
        {
            Debug.Log($"[NET][GRPC] Echo response: {message}");
            GrpcEchoReceived?.Invoke(message);
        }

        private void HandleUdpMessage(string message)
        {
            Debug.Log($"[NET][UDP] Receive: {message}");
            if (_udpAwaiting && message == _udpAwaitMessage)
            {
                _udpAwaiting = false;
            }

            UdpEchoReceived?.Invoke(message);
        }

        private void HandleTransportError(Exception ex)
        {
            var error = $"[NET] Transport error: {ex}";
            Debug.LogError(error);
            NetworkError?.Invoke(error);
        }

        private void OnDestroy()
        {
            _ = DisconnectAsync();
        }

        private void OnApplicationQuit()
        {
            _ = DisconnectAsync();
        }

        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            _udpAwaiting = false;

            try
            {
                if (_grpcTransport != null)
                {
                    await _grpcTransport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }

                if (_udpTransport != null)
                {
                    await _udpTransport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }

                MainThreadDispatcher.Enqueue(() => Debug.Log("[NET] Disconnect"));
            }
            catch (Exception ex)
            {
                var error = $"[NET] Disconnect error: {ex}";
                MainThreadDispatcher.Enqueue(() =>
                {
                    Debug.LogError(error);
                    NetworkError?.Invoke(error);
                });
            }
        }
    }
}
