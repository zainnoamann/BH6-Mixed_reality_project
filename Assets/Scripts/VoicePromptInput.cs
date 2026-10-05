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
///
/// The microphone runs continuously into a short ring buffer, and each clip starts a
/// little before the key was pressed and ends a little after it was released, so the
/// first and last words are not clipped.
/// </summary>
public class VoicePromptInput : MonoBehaviour
{
    [SerializeField] private string sttUrl = "http://127.0.0.1:8766/transcribe";
    [SerializeField] private TMP_InputField promptInputField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Key holdKey = Key.V;
    [SerializeField] private int maxSeconds = 15;
    [SerializeField] private bool autoGenerate = false;
    [SerializeField] private float preRollSeconds = 0.4f;
    [SerializeField] private float tailSeconds = 0.3f;

    private const int SampleRate = 16000;
    private AudioClip ring;
    private string device;
    private bool recording;
    private bool finishing;
    private int startSample;
    private float startTime;

    private void Start()
    {
        OpenMic();
    }

    private void OnDestroy()
    {
        if (device != null && Microphone.IsRecording(device)) Microphone.End(device);
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[holdKey].wasPressedThisFrame && !(promptInputField != null && promptInputField.isFocused)) StartRecording();
            if (kb[holdKey].wasReleasedThisFrame) StopRecording();
        }

        if (recording && Time.unscaledTime - startTime > maxSeconds) StopRecording();
    }

    private bool OpenMic()
    {
        if (ring != null && device != null && Microphone.IsRecording(device)) return true;
        if (Microphone.devices.Length == 0) return false;

        device = Microphone.devices[0];
        ring = Microphone.Start(device, true, maxSeconds + 2, SampleRate);
        return ring != null;
    }

    public void StartRecording()
    {
        if (recording || finishing) return;
        if (!OpenMic()) { SetStatus("No microphone found."); return; }

        int pos = Microphone.GetPosition(device);
        if (pos <= 0) { SetStatus("Microphone warming up - try again."); return; }

        int preRoll = Mathf.RoundToInt(preRollSeconds * ring.frequency);
        startSample = ((pos - preRoll) % ring.samples + ring.samples) % ring.samples;
        startTime = Time.unscaledTime;
        recording = true;
        SetStatus("Listening...");
    }

    public void StopRecording()
    {
        if (!recording) return;
        recording = false;
        StartCoroutine(FinishAfterTail());
    }

    private IEnumerator FinishAfterTail()
    {
        finishing = true;
        yield return new WaitForSecondsRealtime(tailSeconds);
        finishing = false;

        int total = ring.samples;
        int end = Microphone.GetPosition(device);
        int count = ((end - startSample) % total + total) % total;

        if (count < ring.frequency / 4) { SetStatus("Too short - hold the key while speaking."); yield break; }

        int channels = ring.channels;
        float[] all = new float[total * channels];
        ring.GetData(all, 0);

        float[] clip = new float[count * channels];
        for (int i = 0; i < count; i++)
        {
            int src = ((startSample + i) % total) * channels;
            for (int c = 0; c < channels; c++) clip[i * channels + c] = all[src + c];
        }

        float peak = 0f;
        for (int i = 0; i < clip.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(clip[i]));
        Debug.Log("[Voice] mic " + device + ", " + ring.frequency + " Hz, " + channels + " ch, " + count + " samples, peak " + peak.ToString("F3"));

        SetStatus("Transcribing...");
        yield return Transcribe(ToWav(clip, channels, ring.frequency));
    }

    private IEnumerator Transcribe(byte[] wav)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wav, "speech.wav", "audio/wav");

        using (UnityWebRequest req = UnityWebRequest.Post(sttUrl, form))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Speech service error (" + req.responseCode + ") - is stt_service.py running? Type instead.");
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

    private static byte[] ToWav(float[] samples, int channels, int rate)
    {
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            int dataLen = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(rate); w.Write(rate * channels * 2);
            w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
            foreach (float f in samples) w.Write((short)(Mathf.Clamp(f, -1f, 1f) * short.MaxValue));
            return ms.ToArray();
        }
    }
}
