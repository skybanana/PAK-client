using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using UnityEngine;
using UnityEngine.Events;

public abstract class TCPSocketMangerBase : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 50051;
    public bool useTls = false;
    public bool allowTlsFallback = true;
    public string addressOverride = "";
    public int sequenceNumber = 1;
    public bool isConnected;
    protected GrpcChannel Channel { get; private set; }
    private HttpClient httpClient;
    private CancellationTokenSource connectCts;

    /// <summary>
    /// ip, port 초기화 후 패킷 처리 메소드 등록
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    public TCPSocketMangerBase Init(string ip, int port)
    {
        this.ip = ip;
        this.port = port;
        return this;
    }
    /// <summary>
    /// 등록된 ip, port로 소켓 연결
    /// </summary>
    /// <param name="callbak"></param>
    /// <returns></returns>
    public async Task<bool> Connect(UnityAction callbak = null, CancellationToken cancellationToken = default)
    {
        if (isConnected)
        {
            callbak?.Invoke();
            return true;
        }

        var address = BuildAddress();
        if (!useTls)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }
        connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // HttpClient must live as long as the channel.
        httpClient = new HttpClient();
        Channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient,
            Credentials = useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure
        });

        isConnected = true;
        callbak?.Invoke();

        await OnConnectedAsync(connectCts.Token);
        return true;
    }

    protected virtual Task OnConnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual void OnReceive()
    {
        // gRPC receives per-call; override in derived class if you use streaming calls.
    }

    public abstract Task<string> SendAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 파괴 혹은 앱 종료시 소켓 연결 해제
    /// </summary>
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
