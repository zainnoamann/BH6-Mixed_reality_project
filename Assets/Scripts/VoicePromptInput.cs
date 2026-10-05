using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

/// <summary>
/// Push-to-talk for the "describe what to add" box.
///
/// Hold the key (default V) or call StartRecording()/StopRecording() from a UI button's
/// pointer down/up or a VR controller action. On release the clip is sent to the STT
/// service (AI Models/stt/stt_service.py) and the transcript is written into the prompt
/// field, so the existing Generate button works unchanged. Set autoGenerate to skip it.
/// </summary>
public class VoicePromptInput : MonoBehaviour
{
    [SerializeField] private string sttUrl = "http://127.0.0.1:8766/transcribe";
    [SerializeField] private TMP_InputField promptInputField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Key holdKey = Key.V;
    [SerializeField] private int maxSeconds = 15;
    [SerializeField] private bool autoGenerate = false;

    private const int SampleRate = 16000;
    private AudioClip clip;
    private string device;
    private bool recording;

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb[holdKey].wasPressedThisFrame && !(promptInputField != null && promptInputField.isFocused)) StartRecording();
        if (kb[holdKey].wasReleasedThisFrame) StopRecording();
    }

    public void StartRecording()
    {
        if (recording) return;
        if (Microphone.devices.Length == 0) { SetStatus("No microphone found."); return; }

        device = Microphone.devices[0];
        clip = Microphone.Start(device, false, maxSeconds, SampleRate);
        recording = true;
        SetStatus("Listening...");
    }

    public void StopRecording()
    {
        if (!recording) return;
        recording = false;

        int samples = Microphone.GetPosition(device);
        Microphone.End(device);

        if (samples < SampleRate / 4) { SetStatus("Too short - hold the key while speaking."); return; }

        float[] data = new float[samples * clip.channels];
        clip.GetData(data, 0);
        SetStatus("Transcribing...");
        StartCoroutine(Transcribe(ToWav(data, clip.channels)));
    }

    private IEnumerator Transcribe(byte[] wav)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wav, "speech.wav", "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(sttUrl, form))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Speech service not reachable - is stt_service.py running? Type instead.");
                yield break;
            }

            string text = JsonUtility.FromJson<Reply>(req.downloadHandler.text).text;

            if (string.IsNullOrWhiteSpace(text)) { SetStatus("Didn't catch that - try again."); yield break; }

            if (promptInputField != null) promptInputField.text = text;
            SetStatus("Heard: " + text);

            if (autoGenerate && AddItemFlow.Instance != null) AddItemFlow.Instance.GenerateImage(text);
        }
    }

    [Serializable] private class Reply { public string text; }

    private void SetStatus(string s) { if (statusText != null) statusText.text = s; }

    private static byte[] ToWav(float[] samples, int channels)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            int dataLen = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(SampleRate); w.Write(SampleRate * channels * 2);
            w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            foreach (float f in samples) w.Write((short)(Mathf.Clamp(f, -1f, 1f) * short.MaxValue));
            return ms.ToArray();
        }
    }
}
