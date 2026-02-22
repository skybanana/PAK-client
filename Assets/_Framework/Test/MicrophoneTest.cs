using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class MicrophoneTest : MonoBehaviour
{
    public AudioSource audioSource;   // AudioSource 컴포넌트
    private string microphoneDevice;  // 마이크 장치 이름
    private AudioClip microphoneClip; // 마이크에서 입력된 오디오 클립
 
    void Start()
    {
        if(Microphone.devices.Length > 0)
        {
            // 사용 가능한 마이크 장치 가져오기
        microphoneDevice = Microphone.devices[0];  // 첫 번째 마이크 장치를 사용
 
        // 마이크에서 오디오 입력을 받아오기 시작
        microphoneClip = Microphone.Start(microphoneDevice, true, 10, 44100); // 10초 길이, 44100Hz
        audioSource.clip = microphoneClip;
        audioSource.loop = true;
 
        // 마이크 입력이 시작되면 오디오 재생 시작
        while (!(Microphone.GetPosition(microphoneDevice) > 0)) { }
        audioSource.Play();
        }
        else
        {
            Debug.LogWarning("마이크 장치를 찾을 수 없습니다.");
        }
        
    }
 
    void Update()
    {
        // 데시벨 값을 계산하여 출력
        float decibel = GetDecibel();
        Debug.Log("Current Decibel Level: " + decibel + " dB");
    }
 
    // 오디오 샘플을 받아서 RMS를 계산하고 이를 데시벨로 변환하는 함수
    float GetDecibel()
    {
        float[] data = new float[256]; // 오디오 데이터를 저장할 배열
        float sum = 0f;
 
        // 오디오 데이터를 가져오기 (현재 재생 위치에서 256 샘플)
        audioSource.GetOutputData(data, 0);
 
        // RMS 계산을 위한 제곱 합 구하기
        foreach (var sample in data)
        {
            sum += sample * sample;
        }
 
        // RMS 계산 (평균 제곱근)
        float rms = Mathf.Sqrt(sum / data.Length);
 
        // 데시벨로 변환 (데시벨은 RMS 값의 로그 스케일)
        float decibel = 20 * Mathf.Log10(rms / 0.1f);
 
        // 데시벨 값이 매우 작은 경우 처리
        if (decibel < -80) decibel = -80;
 
        return decibel;
    }
}
