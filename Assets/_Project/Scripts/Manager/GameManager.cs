using System.Threading.Tasks;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameManager : MonoBehaviour
{
    [SerializeField] private TcpSocketManager tcpSocketManager;
    [SerializeField] private string helloMessage = "Hello";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool logConnectionResult = true;
    private bool isSending;
    private bool hasLoggedConnection;

    private async void Start()
    {
        ResolveTcpSocketManager();

        if (connectOnStart)
        {
            await EnsureConnectedAsync();
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

    private async Task SendHelloAsync()
    {
        if (isSending)
            return;

        isSending = true;
        try
        {
            if (!await EnsureConnectedAsync())
                return;

            await tcpSocketManager.SendAsync(helloMessage);
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
