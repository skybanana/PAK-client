using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class UDPSocketManagerBase : MonoBehaviour
{
    [SerializeField] protected string ip = "127.0.0.1";
    [SerializeField] protected int port = 13;
    [SerializeField] protected int responseTimeoutMs = 2000;

    public bool isConnected { get; private set; }
    public event Action<string> OnMessageReceived;
    public event Action<byte[]> OnBytesReceived;

    private readonly ConcurrentQueue<string> mainThreadQueue = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<byte[]> mainThreadBytesQueue = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<string> responseQueue = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim responseSignal = new SemaphoreSlim(0);

    private UdpClient client;
    private CancellationTokenSource lifetimeCts;
    private Task receiveTask;

    public UDPSocketManagerBase Init(string ip, int port)
    {
        this.ip = ip;
        this.port = port;
        return this;
    }

    public Task<bool> Connect(CancellationToken cancellationToken = default)
    {
        if (isConnected)
            return Task.FromResult(true);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            client = new UdpClient();
            client.Connect(ip, port);
        }
        catch (Exception ex)
        {
            client?.Close();
            client = null;
            Debug.LogError($"UDP connect failed: {ex.Message}");
            throw;
        }

        isConnected = true;
        lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveTask = ReceiveLoopAsync(lifetimeCts.Token);
        return Task.FromResult(true);
    }

    public virtual async Task<string> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
        await client.SendAsync(payload, payload.Length);

        return await WaitForResponseAsync(cancellationToken);
    }

    public virtual async Task SendBytesAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (payload == null || payload.Length == 0)
            return;

        await client.SendAsync(payload, payload.Length);
    }

    protected void PumpReceivedMessages()
    {
        while (mainThreadQueue.TryDequeue(out var message))
        {
            OnMessageReceived?.Invoke(message);
        }
    }

    protected void PumpReceivedBytes()
    {
        while (mainThreadBytesQueue.TryDequeue(out var payload))
        {
            OnBytesReceived?.Invoke(payload);
        }
    }

    public Task Disconnect()
    {
        if (!isConnected)
            return Task.CompletedTask;

        isConnected = false;
        lifetimeCts?.Cancel();
        lifetimeCts?.Dispose();
        lifetimeCts = null;

        client?.Close();
        client = null;

        return Task.CompletedTask;
    }

    protected virtual void OnDestroy()
    {
        _ = Disconnect();
    }

    protected void EnsureConnected()
    {
        if (!isConnected || client == null)
        {
            throw new InvalidOperationException("UDP client is not connected. Call Connect() first.");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Debug.LogWarning($"UDP receive failed: {ex.SocketErrorCode}");
                continue;
            }

            var message = Encoding.UTF8.GetString(result.Buffer);
            mainThreadQueue.Enqueue(message);
            responseQueue.Enqueue(message);
            responseSignal.Release();
            mainThreadBytesQueue.Enqueue(result.Buffer);
        }
    }

    private async Task<string> WaitForResponseAsync(CancellationToken cancellationToken)
    {
        if (!isConnected)
            return string.Empty;

        CancellationTokenSource timeoutCts = null;
        CancellationTokenSource linkedCts = null;

        try
        {
            if (responseTimeoutMs > 0)
            {
                timeoutCts = new CancellationTokenSource(responseTimeoutMs);
            }

            if (timeoutCts != null)
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCts.Token, timeoutCts.Token);
            }
            else
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCts.Token);
            }

            await responseSignal.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (isConnected)
            {
                Debug.LogWarning("UDP response wait cancelled or timed out.");
            }

            return string.Empty;
        }
        finally
        {
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }

        if (responseQueue.TryDequeue(out var message))
        {
            return message;
        }

        return string.Empty;
    }
}
