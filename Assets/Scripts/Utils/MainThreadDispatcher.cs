#nullable enable
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PAK.Client.Utils
{
    public sealed class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new();
        private static MainThreadDispatcher? _instance;

        public static void EnsureExists()
        {
            if (_instance != null)
            {
                return;
            }

            var obj = new GameObject("MainThreadDispatcher");
            DontDestroyOnLoad(obj);
            _instance = obj.AddComponent<MainThreadDispatcher>();
        }

        public static void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            Queue.Enqueue(action);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NET][DISPATCH] Exception: {ex}");
                }
            }
        }
    }
}
