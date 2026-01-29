using System;
using System.Threading;
using System.Threading.Tasks;
using Echo;
using Grpc.Core;
using UnityEngine;

public class TcpSocketManager : GrpcSocketManagerBase
{
    private static TcpSocketManager instance;
    public event Action<string> OnEchoReceived;
    private EchoService.EchoServiceClient client;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    protected override Task OnConnectedAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        client = new EchoService.EchoServiceClient(Channel);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectedAsync(bool isReconnect)
    {
        client = null;
        return Task.CompletedTask;
    }

    public override async Task<string> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (client == null)
        {
            client = new EchoService.EchoServiceClient(Channel);
        }

        var request = new EchoRequest
        {
            Message = message ?? string.Empty
        };

        EchoReply reply;
        try
        {
            reply = await client.EchoAsync(request, cancellationToken: cancellationToken);
        }
        catch (RpcException ex)
        {
            Debug.LogError($"Echo RPC failed: {ex.Status}");
            await Disconnect();
            throw;
        }

        DispatchEcho(reply);
        return reply.Message;
    }

    private void DispatchEcho(EchoReply reply)
    {
        OnEchoReceived?.Invoke(reply.Message);
    }
}
