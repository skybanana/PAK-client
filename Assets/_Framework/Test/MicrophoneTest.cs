using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MicrophoneTest : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private int deviceIndex;
    [SerializeField] private int sampleRate = 44100;
    [SerializeField] private int lengthSeconds = 1;
    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool logDevice = true;
    [SerializeField] private bool logDecibel;

    private string deviceName;
    private AudioClip microphoneClip;
    private Coroutine startRoutine;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = true;
    }

    private void OnEnable()
    {
        if (playOnEnable)
        {
            StartLoopback();
        }
    }

    private void OnDisable()
    {
        StopLoopback();
    }

    public void StartLoopback()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
        }

        startRoutine = StartCoroutine(StartMicrophoneRoutine());
    }

    public void StopLoopback()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
            startRoutine = null;
        }

        if (!string.IsNullOrEmpty(deviceName) && Microphone.IsRecording(deviceName))
        {
            Microphone.End(deviceName);
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        microphoneClip = null;
        deviceName = null;
    }

    private IEnumerator StartMicrophoneRoutine()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("No microphone devices found.");
            yield break;
        }

        var resolvedIndex = Mathf.Clamp(deviceIndex, 0, Microphone.devices.Length - 1);
        deviceName = Microphone.devices[resolvedIndex];

        if (logDevice)
        {
            Debug.Log($"Using microphone device: {deviceName}");
        }

        var resolvedLength = Mathf.Max(1, lengthSeconds);
        var resolvedRate = sampleRate <= 0 ? 0 : sampleRate;

        microphoneClip = Microphone.Start(deviceName, true, resolvedLength, resolvedRate);
        audioSource.clip = microphoneClip;

        while (Microphone.GetPosition(deviceName) <= 0)
        {
            yield return null;
        }

        audioSource.Play();
    }

    private void Update()
    {
        if (!logDecibel || audioSource == null)
        {
            return;
        }

        var decibel = GetDecibel();
        Debug.Log($"Current Decibel Level: {decibel} dB");
    }

    private float GetDecibel()
    {
        var data = new float[256];
        audioSource.GetOutputData(data, 0);

        float sum = 0f;
        for (var i = 0; i < data.Length; i++)
        {
            var sample = data[i];
            sum += sample * sample;
        }

        var rms = Mathf.Sqrt(sum / data.Length);
        var decibel = 20f * Mathf.Log10(rms / 0.1f);
        if (decibel < -80f)
        {
            decibel = -80f;
        }

        return decibel;
    }
}
