#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PAK.Client.Network.Model;

namespace PAK.Client.Network.Transports
{
    public sealed class UdpEchoTransport : ITransport
    {
        private readonly NetworkConfig _config;
        private readonly ConcurrentQueue<string> _messages = new();
        private readonly ConcurrentQueue<Exception> _errors = new();
        private UdpClient? _client;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;

        public UdpEchoTransport(NetworkConfig config)
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

            try
            {
                _client = new UdpClient();
                _client.Connect(_config.UdpHost, _config.UdpPort);
                _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
                IsConnected = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"UDP connect error ({_config.UdpHost}:{_config.UdpPort})", ex);
            }

            return Task.CompletedTask;
        }

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            if (_client == null)
            {
                throw new InvalidOperationException(
                    $"UDP client is not connected ({_config.UdpHost}:{_config.UdpPort}).");
            }

            var bytes = Encoding.UTF8.GetBytes(message ?? string.Empty);
            await WithCancellation(_client.SendAsync(bytes, bytes.Length), cancellationToken).ConfigureAwait(false);
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
                _receiveCts?.Cancel();
                _receiveCts?.Dispose();
                _client?.Close();
                _client?.Dispose();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"UDP disconnect error ({_config.UdpHost}:{_config.UdpPort})", ex);
            }
            finally
            {
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

            while (_errors.TryDequeue(out var error))
            {
                Error?.Invoke(error);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_client == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await WithCancellation(_client.ReceiveAsync(), cancellationToken).ConfigureAwait(false);
                    var message = Encoding.UTF8.GetString(result.Buffer);
                    _messages.Enqueue(message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _errors.Enqueue(new InvalidOperationException(
                        $"UDP receive error ({_config.UdpHost}:{_config.UdpPort})", ex));
                }
            }
        }

        private static async Task WithCancellation(Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(state =>
                   ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            await task.ConfigureAwait(false);
        }

        private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(state =>
                   ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            return await task.ConfigureAwait(false);
        }
    }
}
