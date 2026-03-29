using System;
using System.Collections;
using System.Threading.Tasks;
using Audio;
using Concentus;
using Concentus.Enums;
using Google.Protobuf;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private UDPSocketManager udpSocketManager;
    [SerializeField] private bool connectUdpOnStart = true;

    [Header("Microphone")]
    [SerializeField] private int deviceIndex;
    [SerializeField] private int sampleRate = 48000;
    [SerializeField] private int lengthSeconds = 1;
    [SerializeField] private bool startOnEnable = true;
    [SerializeField] private bool logDevice = true;

    [Header("Opus")]
    [SerializeField] private uint streamId = 1;
    [SerializeField] private int frameDurationMs = 10;
    [SerializeField] private int bitrate = 24000;
    [SerializeField] private int complexity = 5;
    [SerializeField] private int maxPacketSize = 4000;

    [Header("Playback")]
    [SerializeField] private bool enablePlayback = true;
    [SerializeField] private AudioSource playbackSource;
    [SerializeField] private int playbackBufferSeconds = 1;
    [SerializeField] private bool logDecodeErrors;

    [Header("Diagnostics")]
    [SerializeField] private bool logRoundTripTime = true;
    [SerializeField] private int rttSampleWindow = 50;
    [SerializeField] private float rttReportIntervalSeconds = 1.0f;

    public AudioStreamConfig CurrentConfig { get; private set; }
    public event Action<AudioStreamConfig> OnConfigReady;

    private string deviceName;
    private AudioClip microphoneClip;
    private Coroutine startRoutine;
    private bool isCapturing;
    private bool isConnectingUdp;

    private int resolvedSampleRate;
    private int resolvedChannels;
    private int frameSize;
    private int lastSamplePosition;

    private IOpusEncoder encoder;
    private IOpusDecoder decoder;
    private float[] frameBuffer;
    private float[] tempBuffer;
    private byte[] opusBuffer;
    private float[] decodeFloatBuffer;
    private byte[] decodeInputBuffer;
    private AudioClip playbackClip;
    private FloatRingBuffer playbackBuffer;
    private int decoderSampleRate;
    private int decoderChannels;

    private readonly System.Collections.Generic.Dictionary<uint, float> sendTimeBySequence =
        new System.Collections.Generic.Dictionary<uint, float>();
    private readonly System.Collections.Generic.Queue<uint> sendSequenceOrder =
        new System.Collections.Generic.Queue<uint>();
    private float lastRttReportTime;
    private RttStats rttStats;

    private uint sequence;
    private ulong timestamp;

    private void Awake()
    {
        if (udpSocketManager == null)
        {
            udpSocketManager = FindObjectOfType<UDPSocketManager>();
        }

        if (playbackSource == null)
        {
            playbackSource = GetComponent<AudioSource>();
        }
    }

    private void OnEnable()
    {
        if (udpSocketManager != null)
        {
            udpSocketManager.OnBytesReceived += HandleBytesReceived;
        }

        if (connectUdpOnStart)
        {
            _ = EnsureUdpConnectedAsync();
        }

        if (startOnEnable)
        {
            StartCapture();
        }
    }

    private void OnDisable()
    {
        if (udpSocketManager != null)
        {
            udpSocketManager.OnBytesReceived -= HandleBytesReceived;
        }

        StopCapture();
        StopPlayback();
    }

    private void Update()
    {
        if (!isCapturing || microphoneClip == null)
            return;

        ProcessMicrophone();
    }

    public void StartCapture()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
        }

        startRoutine = StartCoroutine(StartMicrophoneRoutine());
    }

    public void StopCapture()
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

        microphoneClip = null;
        deviceName = null;
        encoder = null;
        decoder = null;
        isCapturing = false;
        sequence = 0;
        timestamp = 0;
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

        while (Microphone.GetPosition(deviceName) <= 0)
        {
            yield return null;
        }

        resolvedSampleRate = microphoneClip.frequency;
        resolvedChannels = microphoneClip.channels;

        InitializeEncoder(resolvedSampleRate, resolvedChannels);

        lastSamplePosition = Microphone.GetPosition(deviceName);
        isCapturing = true;
    }

    private void InitializeEncoder(int sampleRate, int channels)
    {
        frameSize = Mathf.Max(1, sampleRate * frameDurationMs / 1000);
        encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);

        if (bitrate > 0)
        {
            encoder.Bitrate = bitrate;
        }

        if (complexity >= 0 && complexity <= 10)
        {
            encoder.Complexity = complexity;
        }

        frameBuffer = new float[frameSize * channels];
        opusBuffer = new byte[Mathf.Max(256, maxPacketSize)];
        sequence = 0;
        timestamp = 0;

        InitializeDecoder(sampleRate, channels);

        CurrentConfig = new AudioStreamConfig
        {
            StreamId = streamId,
            SampleRate = (uint)sampleRate,
            Channels = (uint)channels,
            FrameDurationMs = (uint)frameDurationMs
        };

        OnConfigReady?.Invoke(CurrentConfig);
    }

    private void ProcessMicrophone()
    {
        if (string.IsNullOrEmpty(deviceName) || !Microphone.IsRecording(deviceName))
            return;

        var micPosition = Microphone.GetPosition(deviceName);
        if (micPosition < 0)
            return;

        var availableSamples = micPosition - lastSamplePosition;
        if (availableSamples < 0)
        {
            availableSamples += microphoneClip.samples;
        }

        while (availableSamples >= frameSize)
        {
            if (!ReadFrame(lastSamplePosition, frameBuffer))
                break;

            lastSamplePosition = (lastSamplePosition + frameSize) % microphoneClip.samples;
            availableSamples -= frameSize;

            EncodeAndSend(frameBuffer);
        }
    }

    private bool ReadFrame(int startSample, float[] destination)
    {
        var clipSamples = microphoneClip.samples;
        if (startSample + frameSize <= clipSamples)
        {
            return microphoneClip.GetData(destination, startSample);
        }

        var firstSamples = clipSamples - startSample;
        var secondSamples = frameSize - firstSamples;

        if (firstSamples > 0)
        {
            if (!ReadSegment(startSample, firstSamples, destination, 0))
                return false;
        }

        if (secondSamples > 0)
        {
            var destOffset = firstSamples * resolvedChannels;
            if (!ReadSegment(0, secondSamples, destination, destOffset))
                return false;
        }

        return true;
    }

    private bool ReadSegment(int sourceOffsetSamples, int sampleCount, float[] destination, int destOffset)
    {
        var sampleCountTotal = sampleCount * resolvedChannels;
        if (tempBuffer == null || tempBuffer.Length != sampleCountTotal)
        {
            tempBuffer = new float[sampleCountTotal];
        }

        if (!microphoneClip.GetData(tempBuffer, sourceOffsetSamples))
        {
            return false;
        }

        Array.Copy(tempBuffer, 0, destination, destOffset, sampleCountTotal);
        return true;
    }

    private void EncodeAndSend(float[] samples)
    {
        if (encoder == null)
            return;

        if (udpSocketManager == null || !udpSocketManager.isConnected)
            return;

        var sendSequence = sequence;
        var encodedLength = encoder.Encode(samples.AsSpan(0, frameSize * resolvedChannels), frameSize, opusBuffer, opusBuffer.Length);
        if (encodedLength <= 0)
            return;

        var packet = new AudioPacket
        {
            StreamId = streamId,
            Sequence = sequence++,
            Timestamp = timestamp,
            Payload = ByteString.CopyFrom(opusBuffer, 0, encodedLength)
        };

        timestamp += (ulong)frameSize;
        TrackSendTime(sendSequence);
        var payload = packet.ToByteArray();
        _ = udpSocketManager.SendBytesAsync(payload);
    }

    private void HandleBytesReceived(byte[] payload)
    {
        if (!enablePlayback || payload == null || payload.Length == 0)
            return;

        AudioPacket packet;
        try
        {
            packet = AudioPacket.Parser.ParseFrom(payload);
        }
        catch (Exception ex)
        {
            if (logDecodeErrors)
            {
                Debug.LogWarning($"Audio packet parse failed: {ex.Message}");
            }
            return;
        }

        if (packet.Payload == null || packet.Payload.Length == 0)
            return;

        TrackRoundTripTime(packet.Sequence);

        EnsureDecoderReady();
        if (decoder == null)
            return;

        var payloadLength = packet.Payload.Length;
        if (decodeInputBuffer == null || decodeInputBuffer.Length < payloadLength)
        {
            decodeInputBuffer = new byte[payloadLength];
        }

        packet.Payload.CopyTo(decodeInputBuffer, 0);

        var decodedSamples = decoder.Decode(decodeInputBuffer.AsSpan(0, payloadLength), decodeFloatBuffer, frameSize);
        if (decodedSamples <= 0)
            return;

        var totalSamples = decodedSamples * decoderChannels;
        playbackBuffer?.Write(decodeFloatBuffer, totalSamples);
    }

    private async Task EnsureUdpConnectedAsync()
    {
        if (udpSocketManager == null)
        {
            Debug.LogError("UDPSocketManager not found.");
            return;
        }

        if (udpSocketManager.isConnected || isConnectingUdp)
            return;

        isConnectingUdp = true;
        try
        {
            await udpSocketManager.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"UDP connection failed: {ex.Message}");
        }
        finally
        {
            isConnectingUdp = false;
        }
    }

    private void EnsureDecoderReady()
    {
        if (decoder != null)
            return;

        var rate = resolvedSampleRate > 0 ? resolvedSampleRate : sampleRate;
        var channels = resolvedChannels > 0 ? resolvedChannels : 1;
        frameSize = Mathf.Max(1, rate * frameDurationMs / 1000);
        InitializeDecoder(rate, channels);
    }

    private void InitializeDecoder(int sampleRate, int channels)
    {
        decoderSampleRate = sampleRate;
        decoderChannels = Mathf.Max(1, channels);
        decoder = OpusCodecFactory.CreateDecoder(sampleRate, decoderChannels);

        if (frameSize <= 0)
        {
            frameSize = Mathf.Max(1, sampleRate * frameDurationMs / 1000);
        }

        decodeFloatBuffer = new float[frameSize * decoderChannels];
        decodeInputBuffer = new byte[Mathf.Max(256, maxPacketSize)];

        SetupPlayback(sampleRate, decoderChannels);
    }

    private void SetupPlayback(int sampleRate, int channels)
    {
        if (!enablePlayback)
            return;

        if (playbackSource == null)
        {
            playbackSource = gameObject.AddComponent<AudioSource>();
        }

        playbackSource.playOnAwake = false;
        playbackSource.loop = true;

        var bufferSeconds = Mathf.Max(1, playbackBufferSeconds);
        var samplesPerChannel = sampleRate * bufferSeconds;
        playbackBuffer = new FloatRingBuffer(samplesPerChannel * channels);

        playbackClip = AudioClip.Create("OpusPlayback", samplesPerChannel, channels, sampleRate, true, OnAudioRead, OnAudioSetPosition);
        playbackSource.clip = playbackClip;
        playbackSource.Play();
    }

    private void StopPlayback()
    {
        if (playbackSource != null)
        {
            playbackSource.Stop();
            playbackSource.clip = null;
        }

        playbackClip = null;
        playbackBuffer = null;
    }

    private void OnAudioRead(float[] data)
    {
        if (playbackBuffer == null)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        var read = playbackBuffer.Read(data, data.Length);
        if (read < data.Length)
        {
            Array.Clear(data, read, data.Length - read);
        }
    }

    private void OnAudioSetPosition(int newPosition)
    {
    }

    private void TrackSendTime(uint sendSequence)
    {
        if (!logRoundTripTime)
            return;

        var now = Time.realtimeSinceStartup;
        sendTimeBySequence[sendSequence] = now;
        sendSequenceOrder.Enqueue(sendSequence);

        var maxTracked = Mathf.Max(10, rttSampleWindow * 2);
        while (sendSequenceOrder.Count > maxTracked)
        {
            var seq = sendSequenceOrder.Dequeue();
            sendTimeBySequence.Remove(seq);
        }
    }

    private void TrackRoundTripTime(uint receivedSequence)
    {
        if (!logRoundTripTime)
            return;

        if (!sendTimeBySequence.TryGetValue(receivedSequence, out var sentAt))
            return;

        sendTimeBySequence.Remove(receivedSequence);
        var rttMs = (Time.realtimeSinceStartup - sentAt) * 1000f;
        rttStats.Push(rttMs, Mathf.Max(5, rttSampleWindow));

        var now = Time.realtimeSinceStartup;
        if (now - lastRttReportTime >= rttReportIntervalSeconds)
        {
            lastRttReportTime = now;
            Debug.Log($"Audio RTT avg {rttStats.Average:0.0} ms, min {rttStats.Min:0.0} ms, max {rttStats.Max:0.0} ms ({rttStats.Count} samples)");
        }
    }

    private struct RttStats
    {
        public float Average;
        public float Min;
        public float Max;
        public int Count;

        public void Push(float value, int window)
        {
            if (Count == 0)
            {
                Average = value;
                Min = value;
                Max = value;
                Count = 1;
                return;
            }

            Count = Mathf.Min(Count + 1, window);
            Average = Average + (value - Average) / Count;
            Min = Mathf.Min(Min, value);
            Max = Mathf.Max(Max, value);
        }
    }

    private sealed class FloatRingBuffer
    {
        private readonly float[] buffer;
        private int writePosition;
        private int readPosition;
        private int available;
        private readonly object sync = new object();

        public FloatRingBuffer(int capacity)
        {
            buffer = new float[Mathf.Max(1, capacity)];
        }

        public int Capacity => buffer.Length;

        public void Write(float[] data, int count)
        {
            if (data == null || count <= 0)
                return;

            lock (sync)
            {
                if (count >= buffer.Length)
                {
                    Array.Copy(data, count - buffer.Length, buffer, 0, buffer.Length);
                    readPosition = 0;
                    writePosition = 0;
                    available = buffer.Length;
                    return;
                }

                var free = buffer.Length - available;
                if (count > free)
                {
                    var drop = count - free;
                    readPosition = (readPosition + drop) % buffer.Length;
                    available -= drop;
                }

                var first = Mathf.Min(count, buffer.Length - writePosition);
                Array.Copy(data, 0, buffer, writePosition, first);
                var remaining = count - first;
                if (remaining > 0)
                {
                    Array.Copy(data, first, buffer, 0, remaining);
                }

                writePosition = (writePosition + count) % buffer.Length;
                available += count;
            }
        }

        public int Read(float[] destination, int count)
        {
            if (destination == null || count <= 0)
                return 0;

            lock (sync)
            {
                var toRead = Mathf.Min(count, available);
                var first = Mathf.Min(toRead, buffer.Length - readPosition);
                Array.Copy(buffer, readPosition, destination, 0, first);
                var remaining = toRead - first;
                if (remaining > 0)
                {
                    Array.Copy(buffer, 0, destination, first, remaining);
                }

                readPosition = (readPosition + toRead) % buffer.Length;
                available -= toRead;
                return toRead;
            }
        }
    }
}
