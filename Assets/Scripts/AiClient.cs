using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP client for the CreativeTwin AI server (AI Models/server/server.py).
///
/// Mirrors the UI flow: start an image job, poll it, accept the image to start the 3D job,
/// poll again, download the result. Everything is a coroutine so the room stays
/// interactive while a job runs. Any failure - server down, no GPU, job error - comes
/// back through the fail callback with a readable message, and the UI drops to
/// placeholder mode rather than breaking.
/// </summary>
public class AiClient : MonoBehaviour
{
    [Tooltip("Where AI Models/start.py is serving. Use the host PC's LAN address if remote.")]
    public string baseUrl = "http://127.0.0.1:8765";

    [SerializeField] private float requestTimeoutSeconds = 15f;

    // ------------------------------------------------------------------ DTOs (JsonUtility)

    [Serializable]
    public class GpuInfo
    {
        public bool available;
        public string name;
        public int vramMb;
        public string reason;
    }

    [Serializable]
    public class HealthInfo
    {
        public bool ready;
        public GpuInfo gpu;
        public string[] missing;
    }

    [Serializable]
    public class JobInfo
    {
        public string jobId;
        public string prompt;
        public string status;     // queued | running | awaiting_review | done | failed | cancelled
        public string phase;      // image | model
        public string stage;
        public float progress;
        public string message;
        public float elapsedSeconds;
        public string imageUrl;
        public string modelUrl;

        public bool IsRunning => status == "queued" || status == "running";
        public bool IsFailed => status == "failed" || status == "cancelled";
    }

    [Serializable]
    private class GeneratePayload
    {
        public string prompt;
        public string operation = "add";
    }

    [Serializable]
    private class RegeneratePayload
    {
        public string prompt;
    }

    // ------------------------------------------------------------------ requests

    public IEnumerator GetHealth(Action<HealthInfo> ok, Action<string> fail)
    {
        yield return Send(UnityWebRequest.Get(Url("/health")), ok, fail);
    }

    public IEnumerator StartImageJob(string prompt, Action<JobInfo> ok, Action<string> fail)
    {
        string body = JsonUtility.ToJson(new GeneratePayload { prompt = prompt });
        yield return Send(Post("/generate", body), ok, fail);
    }

    public IEnumerator GetJob(string jobId, Action<JobInfo> ok, Action<string> fail)
    {
        yield return Send(UnityWebRequest.Get(Url("/jobs/" + jobId)), ok, fail);
    }

    public IEnumerator AcceptImage(string jobId, Action<JobInfo> ok, Action<string> fail)
    {
        yield return Send(Post("/jobs/" + jobId + "/accept", "{}"), ok, fail);
    }

    public IEnumerator Regenerate(string jobId, string prompt, Action<JobInfo> ok, Action<string> fail)
    {
        string body = JsonUtility.ToJson(new RegeneratePayload { prompt = prompt });
        yield return Send(Post("/jobs/" + jobId + "/regenerate", body), ok, fail);
    }

    public IEnumerator CancelJob(string jobId)
    {
        using (UnityWebRequest request = UnityWebRequest.Delete(Url("/jobs/" + jobId)))
        {
            request.timeout = (int)requestTimeoutSeconds;
            yield return request.SendWebRequest();
        }
    }

    /// <summary>Polls until the job leaves the running state, reporting each update.</summary>
    public IEnumerator WaitForJob(
        string jobId,
        float intervalSeconds,
        Action<JobInfo> onUpdate,
        Action<JobInfo> onFinished,
        Action<string> fail)
    {
        while (true)
        {
            JobInfo latest = null;
            string error = null;

            yield return GetJob(jobId, job => latest = job, message => error = message);

            if (error != null)
            {
                fail?.Invoke(error);
                yield break;
            }

            onUpdate?.Invoke(latest);

            if (!latest.IsRunning)
            {
                onFinished?.Invoke(latest);
                yield break;
            }

            yield return new WaitForSeconds(intervalSeconds);
        }
    }

    public IEnumerator DownloadTexture(string relativeUrl, Action<Texture2D> ok, Action<string> fail)
    {
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(Url(relativeUrl)))
        {
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                fail?.Invoke(Describe(request));
                yield break;
            }

            ok?.Invoke(DownloadHandlerTexture.GetContent(request));
        }
    }

    public IEnumerator DownloadBytes(string relativeUrl, Action<byte[]> ok, Action<string> fail)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(Url(relativeUrl)))
        {
            request.timeout = 120;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                fail?.Invoke(Describe(request));
                yield break;
            }

            ok?.Invoke(request.downloadHandler.data);
        }
    }

    // ------------------------------------------------------------------ plumbing

    private string Url(string path)
    {
        return baseUrl.TrimEnd('/') + path;
    }

    private UnityWebRequest Post(string path, string json)
    {
        UnityWebRequest request = new UnityWebRequest(Url(path), "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private IEnumerator Send<T>(UnityWebRequest request, Action<T> ok, Action<string> fail)
    {
        using (request)
        {
            request.timeout = (int)requestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                fail?.Invoke(Describe(request));
                yield break;
            }

            T parsed;

            try
            {
                parsed = JsonUtility.FromJson<T>(request.downloadHandler.text);
            }
            catch (Exception exc)
            {
                fail?.Invoke("Bad response from AI server: " + exc.Message);
                yield break;
            }

            ok?.Invoke(parsed);
        }
    }

    private static string Describe(UnityWebRequest request)
    {
        if (request.result == UnityWebRequest.Result.ConnectionError)
        {
            return "AI server not reachable at " + request.url + " (is start.py running?)";
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        // FastAPI wraps errors as {"detail": ...}; keep it short for the UI.
        if (!string.IsNullOrEmpty(body) && body.Length < 400)
        {
            return "AI server: HTTP " + request.responseCode + " " + body;
        }

        return "AI server: HTTP " + request.responseCode + " " + request.error;
    }
}
