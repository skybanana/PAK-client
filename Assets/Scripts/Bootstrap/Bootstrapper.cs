#nullable enable
using PAK.Client.Core;
using PAK.Client.Network.Model;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAK.Client.Bootstrap
{
    public sealed class Bootstrapper : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrapScene()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name != "Bootstrap")
            {
                SceneManager.LoadScene("Bootstrap");
            }
        }

        [SerializeField] private NetworkConfig? networkConfig;

        private void Start()
        {
            if (networkConfig == null)
            {
                Debug.LogError("[BOOT] NetworkConfig is missing.");
                return;
            }

            var gameManager = GameManager.CreateIfNeeded();
            gameManager.Initialize(networkConfig);
            gameManager.Begin();
        }
    }
}
