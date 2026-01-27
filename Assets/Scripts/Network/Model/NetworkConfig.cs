#nullable enable
using UnityEngine;

namespace PAK.Client.Network.Model
{
    [CreateAssetMenu(fileName = "NetworkConfig", menuName = "PAK/Network Config")]
    public sealed class NetworkConfig : ScriptableObject
    {
        [Header("gRPC")]
        [SerializeField] private string grpcHost = "127.0.0.1";
        [SerializeField] private int grpcPort = 50051;
        [SerializeField] private bool grpcUseTls;

        [Header("UDP")]
        [SerializeField] private string udpHost = "127.0.0.1";
        [SerializeField] private int udpPort = 50052;
        [SerializeField, Range(1f, 10f)] private float udpEchoTimeoutSeconds = 2f;

        public string GrpcHost => grpcHost;
        public int GrpcPort => grpcPort;
        public bool GrpcUseTls => grpcUseTls;

        public string UdpHost => udpHost;
        public int UdpPort => udpPort;
        public float UdpEchoTimeoutSeconds => udpEchoTimeoutSeconds;

        public string GrpcAddress
        {
            get
            {
                var scheme = grpcUseTls ? "https" : "http";
                return $"{scheme}://{grpcHost}:{grpcPort}";
            }
        }
    }
}
