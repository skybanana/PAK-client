#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PAK.Client.Network.Transports
{
    public interface ITransport
    {
        bool IsConnected { get; }
        event Action<string>? MessageReceived;
        event Action<Exception>? Error;
        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task SendAsync(string message, CancellationToken cancellationToken);
        void Poll();
    }
}
