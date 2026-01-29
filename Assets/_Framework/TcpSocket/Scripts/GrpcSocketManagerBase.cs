using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Grpc.Net.Client;
using UnityEngine;
using UnityEngine.Events;

public abstract class GrpcSocketManagerBase : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 50051;
    public bool useTls = true;
    public string addressOverride = "";
    public bool http2OnlyForCleartext = true;
    public bool skipCertificateVerificationInEditor = false;

    public int sequenceNumber = 1;
    public bool isConnected;
    protected GrpcChannel Channel { get; private set; }

    private YetAnotherHttpHandler httpHandler;
    private CancellationTokenSource connectCts;

    public GrpcSocketManagerBase Init(string ip, int port)
    {
        this.ip = ip;
        this.port = port;
        return this;
    }

    public async Task<bool> Connect(UnityAction callback = null, CancellationToken cancellationToken = default)
    {
        if (isConnected)
        {
            callback?.Invoke();
            return true;
        }

        var address = BuildAddress();
        connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        httpHandler = CreateHttpHandler();
        Channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = httpHandler,
            DisposeHttpClient = true
        });

        isConnected = true;
        callback?.Invoke();

        await OnConnectedAsync(connectCts.Token);
        return true;
    }

    protected virtual YetAnotherHttpHandler CreateHttpHandler()
    {
        var handler = new YetAnotherHttpHandler();

        if (!useTls && http2OnlyForCleartext)
        {
            handler.Http2Only = true;
        }

#if UNITY_EDITOR
        if (skipCertificateVerificationInEditor)
        {
            handler.SkipCertificateVerification = true;
        }
#endif

        return handler;
    }

    protected virtual Task OnConnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual void OnReceive()
    {
        // gRPC-Web receives per-call; override in derived class if you use streaming calls.
    }

    public abstract Task<string> SendAsync(string message, CancellationToken cancellationToken = default);

    protected virtual void OnDestroy()
    {
        _ = Disconnect();
    }

    public async Task Disconnect(bool isReconnect = false)
    {
        if (!isConnected)
            return;

        isConnected = false;

        connectCts?.Cancel();
        connectCts?.Dispose();
        connectCts = null;

        Channel?.Dispose();
        Channel = null;

        httpHandler?.Dispose();
        httpHandler = null;

        await OnDisconnectedAsync(isReconnect);
    }

    protected virtual Task OnDisconnectedAsync(bool isReconnect) => Task.CompletedTask;

    protected void EnsureConnected()
    {
        if (!isConnected || Channel == null)
        {
            throw new InvalidOperationException("gRPC channel is not connected. Call Connect() first.");
        }
    }

    protected string BuildAddress()
    {
        if (!string.IsNullOrWhiteSpace(addressOverride))
            return addressOverride;

        var scheme = useTls ? "https" : "http";
        return $"{scheme}://{ip}:{port}";
    }
}
