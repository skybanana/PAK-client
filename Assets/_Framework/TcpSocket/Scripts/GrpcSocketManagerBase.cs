using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using UnityEngine;
using UnityEngine.Events;

public abstract class GrpcSocketManagerBase : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 50051;
    public bool useTls = true;
    public string addressOverride = "";
    public GrpcWebMode grpcWebMode = GrpcWebMode.GrpcWeb;
    public bool allowInsecureCertificatesInEditor = false;

    public int sequenceNumber = 1;
    public bool isConnected;
    protected GrpcChannel Channel { get; private set; }

    private HttpClient httpClient;
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

        httpClient = CreateHttpClient();
        Channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        isConnected = true;
        callback?.Invoke();

        await OnConnectedAsync(connectCts.Token);
        return true;
    }

    protected virtual HttpClient CreateHttpClient()
    {
        var httpHandler = new HttpClientHandler();
#if UNITY_EDITOR
        if (allowInsecureCertificatesInEditor)
        {
            httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
#endif
        var grpcWebHandler = new GrpcWebHandler(grpcWebMode, httpHandler);
        return new HttpClient(grpcWebHandler);
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

        httpClient?.Dispose();
        httpClient = null;

        await OnDisconnectedAsync(isReconnect);
    }

    protected virtual Task OnDisconnectedAsync(bool isReconnect) => Task.CompletedTask;

    protected void EnsureConnected()
    {
        if (!isConnected || Channel == null)
        {
            throw new InvalidOperationException("gRPC-Web channel is not connected. Call Connect() first.");
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
