#nullable enable
using System.Threading.Tasks;
using PAK.Client.Network;
using PAK.Client.Network.Model;
using PAK.Client.Utils;
using UnityEngine;

namespace PAK.Client.Core
{
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager? Instance { get; private set; }

        [SerializeField] private bool autoSendOnStart = true;

        private NetworkManager? _networkManager;
        private NetworkConfig? _config;

        public string LastGrpcResponse { get; private set; } = string.Empty;
        public string LastUdpResponse { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        public static GameManager CreateIfNeeded()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var obj = new GameObject("GameManager");
            return obj.AddComponent<GameManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            MainThreadDispatcher.EnsureExists();

            _networkManager = gameObject.GetComponent<NetworkManager>() ?? gameObject.AddComponent<NetworkManager>();
            _networkManager.GrpcEchoReceived += OnGrpcEchoReceived;
            _networkManager.UdpEchoReceived += OnUdpEchoReceived;
            _networkManager.NetworkError += OnNetworkError;
        }

        public void Initialize(NetworkConfig config)
        {
            _config = config;
            if (_networkManager == null)
            {
                Debug.LogError("[NET] NetworkManager missing.");
                return;
            }

            _networkManager.Initialize(config);
        }

        public async void Begin()
        {
            if (_networkManager == null || _config == null)
            {
                Debug.LogError("[NET] NetworkManager or NetworkConfig missing.");
                return;
            }

            await _networkManager.ConnectAsync();

            if (autoSendOnStart)
            {
                await SendEchoesAsync();
            }
        }

        [ContextMenu("Send Echoes")]
        public async void SendEchoes()
        {
            await SendEchoesAsync();
        }

        private async Task SendEchoesAsync()
        {
            if (_networkManager == null)
            {
                Debug.LogError("[NET] NetworkManager missing.");
                return;
            }

            var grpcTask = _networkManager.SendGrpcEchoAsync("hello-grpc");
            var udpTask = _networkManager.SendUdpEchoAsync("hello-udp");
            await Task.WhenAll(grpcTask, udpTask);
        }

        private void OnGrpcEchoReceived(string message)
        {
            LastGrpcResponse = message;
        }

        private void OnUdpEchoReceived(string message)
        {
            LastUdpResponse = message;
        }

        private void OnNetworkError(string message)
        {
            LastError = message;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_networkManager != null)
            {
                _networkManager.GrpcEchoReceived -= OnGrpcEchoReceived;
                _networkManager.UdpEchoReceived -= OnUdpEchoReceived;
                _networkManager.NetworkError -= OnNetworkError;
            }
        }
    }
}
