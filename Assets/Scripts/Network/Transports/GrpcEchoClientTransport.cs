#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PAK.Client.Network.Generated;
using PAK.Client.Network.Model;
using Grpc.Net.Client;

namespace PAK.Client.Network.Transports
{
    public sealed class GrpcEchoClientTransport : ITransport
    {
        private readonly NetworkConfig _config;
        private readonly ConcurrentQueue<string> _messages = new();
        private GrpcChannel? _channel;
        private EchoService.EchoServiceClient? _client;

        public GrpcEchoClientTransport(NetworkConfig config)
        {
            _config = config;
        }

        public bool IsConnected { get; private set; }

        public event Action<string>? MessageReceived;
        public event Action<Exception>? Error;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return Task.CompletedTask;
            }

            if (!_config.GrpcUseTls)
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            }

            try
            {
                _channel = GrpcChannel.ForAddress(_config.GrpcAddress);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"gRPC connect error ({_config.GrpcAddress})", ex);
            }

            _client = new EchoService.EchoServiceClient(_channel);
            IsConnected = true;

            return Task.CompletedTask;
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (_client == null)
            {
                throw new InvalidOperationException($"gRPC client is not connected ({_config.GrpcAddress}).");
            }

            var request = new EchoRequest { Message = message ?? string.Empty };
            var response = await _client.EchoAsync(request, cancellationToken: cancellationToken)
                .ResponseAsync.ConfigureAwait(false);
            _messages.Enqueue(response.Message);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                return Task.CompletedTask;
            }

            IsConnected = false;
            try
            {
                _channel?.Dispose();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"gRPC disconnect error ({_config.GrpcAddress})", ex);
            }
            finally
            {
                _channel = null;
                _client = null;
            }

            return Task.CompletedTask;
        }

        public void Poll()
        {
            while (_messages.TryDequeue(out var message))
            {
                MessageReceived?.Invoke(message);
            }

            // gRPC errors are surfaced via SendAsync/ConnectAsync exceptions.
        }
    }
}
