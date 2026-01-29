using System;
using UnityEngine;

public class UDPSocketManager : UDPSocketManagerBase
{
    private static UDPSocketManager instance;

    [SerializeField] private bool connectOnStart = true;
    public event Action<string> OnEchoReceived;

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

    private void OnEnable()
    {
        OnMessageReceived += HandleMessageReceived;
    }

    private void OnDisable()
    {
        OnMessageReceived -= HandleMessageReceived;
    }

    private async void Start()
    {
        if (!connectOnStart)
            return;

        try
        {
            await Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"UDP connection failed: {ex.Message}");
        }
    }

    private void Update()
    {
        PumpReceivedMessages();
    }

    private void HandleMessageReceived(string message)
    {
        OnEchoReceived?.Invoke(message);
    }
}
