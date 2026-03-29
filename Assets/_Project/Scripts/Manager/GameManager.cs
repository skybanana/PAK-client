using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameManager : MonoBehaviour
{
    [SerializeField] private TcpSocketManager tcpSocketManager;
    [SerializeField] private UDPSocketManager udpSocketManager;
    [SerializeField] private string helloMessage = "Hello";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool logConnectionResult = true;
    [SerializeField] private bool logEchoReply = true;
    [SerializeField] private bool logUdpEchoReply = true;
    private bool isSending;
    private bool hasLoggedConnection;
    private bool hasLoggedUdpConnection;
    private string lastEchoMessage;
    private string lastUdpEchoMessage;

    private void OnEnable()
    {
        ResolveTcpSocketManager();
        ResolveUdpSocketManager();
        if (tcpSocketManager != null)
        {
            tcpSocketManager.OnEchoReceived += HandleEchoReceived;
        }
        // if (udpSocketManager != null)
        // {
        //     udpSocketManager.OnEchoReceived += HandleUdpEchoReceived;
        // }
    }

    private void OnDisable()
    {
        if (tcpSocketManager != null)
        {
            tcpSocketManager.OnEchoReceived -= HandleEchoReceived;
        }
        if (udpSocketManager != null)
        {
            udpSocketManager.OnEchoReceived -= HandleUdpEchoReceived;
        }
    }

    private async void Start()
    {
        ResolveTcpSocketManager();
        ResolveUdpSocketManager();

        if (connectOnStart)
        {
            await EnsureConnectedAsync();
            await EnsureUdpConnectedAsync();
        }
    }

    private void Update()
    {
        if (IsAnyInputTriggered())
        {
            Debug.Log("pressed");
            _ = SendHelloAsync();
        }
    }

    private void ResolveTcpSocketManager()
    {
        if (tcpSocketManager == null)
        {
            tcpSocketManager = FindObjectOfType<TcpSocketManager>();
        }
    }

    private void ResolveUdpSocketManager()
    {
        if (udpSocketManager == null)
        {
            udpSocketManager = FindObjectOfType<UDPSocketManager>();
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (tcpSocketManager == null)
        {
            Debug.LogError("TcpSocketManager not found. Ensure DontDestroy scene is loaded.");
            return false;
        }

        if (!tcpSocketManager.isConnected)
        {
            try
            {
                var connected = await tcpSocketManager.Connect();
                if (connected && logConnectionResult && !hasLoggedConnection)
                {
                    Debug.Log("Server connection succeeded.");
                    hasLoggedConnection = true;
                }
            }
            catch (System.Exception ex)
            {
                if (logConnectionResult && !hasLoggedConnection)
                {
                    Debug.LogError($"Server connection failed: {ex.Message}");
                    hasLoggedConnection = true;
                }
                return false;
            }
        }
        else if (logConnectionResult && !hasLoggedConnection)
        {
            Debug.Log("Server connection already established.");
            hasLoggedConnection = true;
        }

        return tcpSocketManager.isConnected;
    }

    private async Task<bool> EnsureUdpConnectedAsync()
    {
        if (udpSocketManager == null)
        {
            Debug.LogError("UDPSocketManager not found. Ensure DontDestroy scene is loaded.");
            return false;
        }

        if (!udpSocketManager.isConnected)
        {
            try
            {
                var connected = await udpSocketManager.Connect();
                if (connected && logConnectionResult && !hasLoggedUdpConnection)
                {
                    Debug.Log("UDP server connection succeeded.");
                    hasLoggedUdpConnection = true;
                }
            }
            catch (System.Exception ex)
            {
                if (logConnectionResult && !hasLoggedUdpConnection)
                {
                    Debug.LogError($"UDP server connection failed: {ex.Message}");
                    hasLoggedUdpConnection = true;
                }
                return false;
            }
        }
        else if (logConnectionResult && !hasLoggedUdpConnection)
        {
            Debug.Log("UDP server connection already established.");
            hasLoggedUdpConnection = true;
        }

        return udpSocketManager.isConnected;
    }

    private async Task SendHelloAsync()
    {
        if (isSending)
            return;

        isSending = true;
        try
        {
            var tcpReady = await EnsureConnectedAsync();
            var udpReady = await EnsureUdpConnectedAsync();

            if (!tcpReady && !udpReady)
                return;

            Task tcpTask = Task.CompletedTask;
            Task udpTask = Task.CompletedTask;

            if (tcpReady)
            {
                tcpTask = tcpSocketManager.SendAsync(helloMessage);
            }

            if (udpReady)
            {
                udpTask = udpSocketManager.SendAsync(helloMessage);
            }

            await Task.WhenAll(tcpTask, udpTask);
        }
        finally
        {
            isSending = false;
        }
    }

    public void SendHello()
    {
        _ = SendHelloAsync();
    }

    private void HandleEchoReceived(string message)
    {
        lastEchoMessage = message;
        if (logEchoReply)
        {
            Debug.Log($"Echo reply received: {message}");
        }
    }

    private void HandleUdpEchoReceived(string message)
    {
        lastUdpEchoMessage = message;
        if (logUdpEchoReply)
        {
            Debug.Log($"UDP echo reply received: {message}");
        }
    }

#if ENABLE_INPUT_SYSTEM
    private static bool IsAnyInputTriggered()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            return true;

        var mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                              mouse.rightButton.wasPressedThisFrame ||
                              mouse.middleButton.wasPressedThisFrame))
            return true;

        return false;
    }
#else
    private static bool IsAnyInputTriggered()
    {
        return Input.anyKeyDown ||
               Input.GetMouseButtonDown(0) ||
               Input.GetMouseButtonDown(1) ||
               Input.GetMouseButtonDown(2);
    }
#endif
}
