using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the "add a new item" workflow end to end, and owns the conversation with the AI
/// server. The UI controllers stay as the view layer: their button handlers delegate here,
/// and this class tells them which panel to show.
///
///   Menu > New Item  ->  describe  ->  POST /generate        (image job)
///                    ->  image preview  ->  Regenerate / Generate 3D
///                    ->  POST /jobs/{id}/accept              (shape job)
///                    ->  model arrives, drops in front of the player in PLACEMENT mode
///                    ->  drag it on the floor
///                    ->  Accept keeps it, Undo removes it, Regenerate tries again
///
/// When the server is unreachable or has no GPU the same panels run in PLACEHOLDER mode:
/// simulated waits and a coloured block instead of a mesh, so the whole flow is testable
/// without a GPU. Failures never dead-end - the popup always closes and the status says why.
/// </summary>
public class AddItemFlow : MonoBehaviour
{
    [Header("AI server")]
    [SerializeField] private bool useAiServer = true;
    [SerializeField] private string aiServerUrl = "http://127.0.0.1:8765";
    [SerializeField] private float pollSeconds = 1f;

    [Header("Placeholder mode (no server / no GPU)")]
    [SerializeField] private float imageWaitSeconds = 2f;
    [SerializeField] private float modelWaitSeconds = 6f;

    [Header("Panels (optional - found by name when empty)")]
    [SerializeField] private GameObject loadingPopup;
    [SerializeField] private GameObject imagePreviewPanel;
    [SerializeField] private GameObject reviewPanel;
    [SerializeField] private GameObject selectionPanel;

    [Header("Widgets (optional - found under the panels when empty)")]
    [SerializeField] private RawImage generatedImage;
    [SerializeField] private Image loadingFill;
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private TMP_Text imageStatusText;
    [SerializeField] private TMP_Text resultStatusText;
    [SerializeField] private TMP_Text statusText;

    [Header("Placement")]
    [Tooltip("Height of a generated object as a fraction of the room's ceiling height.")]
    [SerializeField, Range(0.05f, 0.9f)] private float objectHeightFraction = 0.3f;

    [SerializeField] private string roomRootName = "Model";

    public static AddItemFlow Instance { get; private set; }

    private enum Stage { Idle, ImageWait, ImageReview, ModelWait, Placing }

    private static readonly string[] PlaceholderImageStages = { "Sending request", "Generating image" };

    private static readonly string[] PlaceholderModelStages =
    {
        "Preparing request", "Generating 3D model", "Building mesh", "Loading into the room",
    };

    private Stage stage = Stage.Idle;
    private string prompt = string.Empty;
    private string jobId;
    private Texture2D previewImage;
    private Color previewColour = Color.grey;
    private int generatedCount;

    private GameObject placed;
    private PlacementDragger dragger;

    private bool aiReady;
    private string aiStatus = "checking";

    private AiClient client;
    private Coroutine flow;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        Instance = this;

        client = GetComponent<AiClient>();

        if (client == null)
        {
            client = gameObject.AddComponent<AiClient>();
        }

        client.baseUrl = aiServerUrl;

        dragger = GetComponent<PlacementDragger>();

        if (dragger == null)
        {
            dragger = gameObject.AddComponent<PlacementDragger>();
        }

        ResolveUi();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        StartCoroutine(CheckHealth());
    }

    /// <summary>
    /// Finds the panels and widgets by name so the scene does not have to be re-wired.
    /// Anything assigned in the Inspector wins.
    /// </summary>
    private void ResolveUi()
    {
        loadingPopup = loadingPopup != null ? loadingPopup : Find("LoadingPopup");
        imagePreviewPanel = imagePreviewPanel != null ? imagePreviewPanel : Find("ImagePreviewPanel");
        reviewPanel = reviewPanel != null ? reviewPanel : Find("GenerationReviewPanel");
        selectionPanel = selectionPanel != null ? selectionPanel : Find("SelectionPanel");

        if (generatedImage == null && imagePreviewPanel != null)
        {
            GameObject go = Find("GeneratedImage");
            if (go != null) generatedImage = go.GetComponent<RawImage>();
        }

        if (loadingFill == null)
        {
            GameObject go = Find("Fill");
            if (go != null) loadingFill = go.GetComponent<Image>();
        }

        loadingText = loadingText != null ? loadingText : FindText("LoadingText");
        imageStatusText = imageStatusText != null ? imageStatusText : FindText("ImageStatusText");
        resultStatusText = resultStatusText != null ? resultStatusText : FindText("ResultStatus");
        statusText = statusText != null ? statusText : FindText("StatusText");
    }

    private static GameObject Find(string name)
    {
        // Panels start inactive, so GameObject.Find will not see them.
        foreach (Transform t in UnityEngine.Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name == name && t.gameObject.scene.IsValid())
            {
                return t.gameObject;
            }
        }

        return null;
    }

    private static TMP_Text FindText(string name)
    {
        GameObject go = Find(name);
        return go != null ? go.GetComponent<TMP_Text>() : null;
    }

    // ------------------------------------------------------------------ server availability

    private IEnumerator CheckHealth()
    {
        if (!useAiServer)
        {
            SetAi(false, "disabled");
            yield break;
        }

        yield return client.GetHealth(
            info =>
            {
                if (info.ready)
                {
                    string gpu = info.gpu != null && info.gpu.available
                        ? info.gpu.name + " " + (info.gpu.vramMb / 1024) + " GB"
                        : "GPU";

                    SetAi(true, "ready - " + gpu);
                }
                else
                {
                    string reason = info.missing != null && info.missing.Length > 0
                        ? info.missing[0]
                        : "not ready";

                    SetAi(false, reason);
                }
            },
            error => SetAi(false, "offline"));
    }

    private void SetAi(bool ready, string reason)
    {
        aiReady = ready;
        aiStatus = reason;

        Debug.Log("AddItemFlow: AI " + (ready ? "ready" : "unavailable - placeholder mode") +
                  " (" + reason + ")", this);
    }

    // ------------------------------------------------------------------ entry points
    // Called from the UI controllers. Names match the buttons they sit behind.

    /// <summary>Generate button, in New Item mode.</summary>
    public void GenerateImage(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            Say(statusText, "Status: describe the item first");
            return;
        }

        prompt = description.Trim();
        previewColour = ColourFor(prompt);
        previewImage = null;
        jobId = null;

        // Pick up a server that was started after Play began.
        StartCoroutine(CheckHealth());

        Restart(ImageJob());
    }

    /// <summary>Regenerate, on the image preview panel.</summary>
    public void RegenerateImage()
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        Show(imagePreviewPanel, false);
        Restart(ImageJob());
    }

    /// <summary>Generate 3D, on the image preview panel.</summary>
    public void GenerateModel()
    {
        Show(imagePreviewPanel, false);
        Restart(ModelJob());
    }

    /// <summary>Cancel, on the loading popup. Kills the pipeline on the server too.</summary>
    public void CancelGeneration()
    {
        Abandon();
        Show(loadingPopup, false);
        Show(imagePreviewPanel, false);
        stage = Stage.Idle;
        Say(statusText, "Status: cancelled");
    }

    /// <summary>Cancel, on the image preview panel.</summary>
    public void CancelPreview()
    {
        Abandon();
        Show(imagePreviewPanel, false);
        stage = Stage.Idle;
    }

    /// <summary>Accept, on the review panel: keep the object where it was dragged.</summary>
    public void AcceptResult()
    {
        dragger.Finish();
        placed = null;
        stage = Stage.Idle;

        Show(reviewPanel, false);
        Say(statusText, "Status: item added");
    }

    /// <summary>Undo, on the review panel: remove the object that was just added.</summary>
    public void UndoResult()
    {
        DiscardPlaced();
        Show(reviewPanel, false);
        stage = Stage.Idle;
        Say(statusText, "Status: item removed");
    }

    /// <summary>Regenerate, on the review panel: throw this one away and build another.</summary>
    public void RegenerateResult()
    {
        DiscardPlaced();
        Show(reviewPanel, false);
        Restart(ModelJob());
    }

    // ------------------------------------------------------------------ image stage

    private IEnumerator ImageJob()
    {
        stage = Stage.ImageWait;

        ShowLoading("Generating image");

        if (!aiReady)
        {
            yield return Simulate(imageWaitSeconds, PlaceholderImageStages);
            ShowImageReview(null);
            yield break;
        }

        string error = null;
        AiClient.JobInfo job = null;

        if (string.IsNullOrEmpty(jobId))
        {
            yield return client.StartImageJob(prompt, j => job = j, e => error = e);
        }
        else
        {
            yield return client.Regenerate(jobId, prompt, j => job = j, e => error = e);
        }

        if (error != null) { Fail(error); yield break; }

        jobId = job.jobId;

        AiClient.JobInfo done = null;
        yield return client.WaitForJob(jobId, pollSeconds, ShowProgress, j => done = j, e => error = e);

        if (error != null || done == null) { Fail(error ?? "lost contact with the AI server"); yield break; }

        if (done.status != "awaiting_review" || string.IsNullOrEmpty(done.imageUrl))
        {
            Fail("image generation " + done.status + ": " + done.message);
            yield break;
        }

        Texture2D texture = null;
        yield return client.DownloadTexture(done.imageUrl, t => texture = t, e => error = e);

        if (error != null) { Fail(error); yield break; }

        ShowImageReview(texture);
    }

    private void ShowImageReview(Texture2D texture)
    {
        flow = null;
        stage = Stage.ImageReview;
        previewImage = texture;

        Show(loadingPopup, false);

        if (generatedImage != null)
        {
            if (texture != null)
            {
                generatedImage.texture = texture;
                generatedImage.color = Color.white;
            }
            else
            {
                // Placeholder mode: a flat colour stands in for the generated image.
                generatedImage.texture = null;
                generatedImage.color = previewColour;
            }
        }

        Say(imageStatusText, aiReady ? "\"" + prompt + "\"" : "\"" + prompt + "\"  (preview - no AI server)");
        Show(imagePreviewPanel, true);
    }

    // ------------------------------------------------------------------ model stage

    private IEnumerator ModelJob()
    {
        stage = Stage.ModelWait;

        ShowLoading("Generating 3D model");

        if (!aiReady || string.IsNullOrEmpty(jobId))
        {
            yield return Simulate(modelWaitSeconds, PlaceholderModelStages);
            EnterPlacement(BuildPlaceholder());
            yield break;
        }

        string error = null;
        AiClient.JobInfo job = null;

        yield return client.AcceptImage(jobId, j => job = j, e => error = e);

        if (error != null) { Fail(error); yield break; }

        AiClient.JobInfo done = null;
        yield return client.WaitForJob(jobId, pollSeconds, ShowProgress, j => done = j, e => error = e);

        if (error != null || done == null) { Fail(error ?? "lost contact with the AI server"); yield break; }

        if (done.status != "done" || string.IsNullOrEmpty(done.modelUrl))
        {
            Fail("3D generation " + done.status + ": " + done.message);
            yield break;
        }

        Say(loadingText, "Downloading model");

        byte[] glb = null;
        yield return client.DownloadBytes(done.modelUrl, b => glb = b, e => error = e);

        if (error != null) { Fail(error); yield break; }

        Say(loadingText, "Loading into the room");

        GameObject model = null;
        yield return GeneratedModelLoader.Load(
            glb, previewImage, previewColour, "Generated: " + prompt, TargetHeight(), g => model = g);

        EnterPlacement(model);
    }

    // ------------------------------------------------------------------ placement

    private void EnterPlacement(GameObject model)
    {
        flow = null;
        Show(loadingPopup, false);

        if (model == null)
        {
            Fail("could not create the object");
            return;
        }

        placed = model;
        generatedCount++;
        placed.name = "Generated_" + generatedCount;

        MakeSelectable(placed);

        dragger.Begin(placed.transform, roomRootName);
        stage = Stage.Placing;

        Say(resultStatusText, "Drag it into place, then Accept.");
        Show(reviewPanel, true);
    }

    /// <summary>Give the new object the same treatment the room's furniture gets.</summary>
    private void MakeSelectable(GameObject target)
    {
        if (target.GetComponentInChildren<Collider>() == null)
        {
            target.AddComponent<BoxCollider>();
        }

        InteractionSetup setup = FindFirstObjectByType<InteractionSetup>();

        if (setup != null)
        {
            setup.Prepare(target.transform);
        }
    }

    private void DiscardPlaced()
    {
        dragger.Cancel();

        if (placed != null)
        {
            Destroy(placed);
            placed = null;
        }
    }

    private float TargetHeight()
    {
        float ceiling = RoomMetrics.TryMeasure(gameObject.scene, roomRootName, out Bounds room)
            ? room.size.y
            : 2.4f;

        return ceiling * objectHeightFraction;
    }

    private GameObject BuildPlaceholder()
    {
        float height = TargetHeight();

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.transform.localScale = new Vector3(height * 0.8f, height, height * 0.8f);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material material = new Material(shader != null ? shader : Shader.Find("Standard"));

        if (previewImage != null)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", previewImage);
            material.mainTexture = previewImage;
        }
        else
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", previewColour);
            material.color = previewColour;
        }

        block.GetComponent<Renderer>().material = material;

        return block;
    }

    // ------------------------------------------------------------------ progress + failure

    private void ShowLoading(string title)
    {
        Show(imagePreviewPanel, false);
        Show(reviewPanel, false);
        Say(loadingText, title);

        if (loadingFill != null)
        {
            loadingFill.fillAmount = 0f;
        }

        Show(loadingPopup, true);
    }

    private void ShowProgress(AiClient.JobInfo job)
    {
        if (job == null)
        {
            return;
        }

        if (loadingFill != null)
        {
            loadingFill.fillAmount = Mathf.Clamp01(job.progress);
        }

        Say(loadingText, string.Format("{0}   {1:0}s", job.stage, job.elapsedSeconds));
    }

    private IEnumerator Simulate(float seconds, string[] stages)
    {
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);

            if (loadingFill != null)
            {
                loadingFill.fillAmount = t;
            }

            int index = Mathf.Min(stages.Length - 1, Mathf.FloorToInt(t * stages.Length));
            Say(loadingText, string.Format("{0}   {1:0}s / ~{2:0}s", stages[index], elapsed, seconds));

            yield return null;
        }

        flow = null;
    }

    /// <summary>Any failure closes the popup and says why - the flow never dead-ends.</summary>
    private void Fail(string message)
    {
        flow = null;
        jobId = null;
        stage = Stage.Idle;

        Debug.LogWarning("AddItemFlow: " + message, this);

        if (message.Contains("not reachable") || message.Contains("503"))
        {
            SetAi(false, "offline");
        }

        Show(loadingPopup, false);
        Say(statusText, "Status: " + message);
        Show(selectionPanel, true);
    }

    private void Restart(IEnumerator routine)
    {
        Abandon();
        flow = StartCoroutine(routine);
    }

    /// <summary>Stops the local coroutine and cancels the server job, if there is one.</summary>
    private void Abandon()
    {
        if (flow != null)
        {
            StopCoroutine(flow);
            flow = null;
        }

        if (!string.IsNullOrEmpty(jobId) && client != null && aiReady)
        {
            StartCoroutine(client.CancelJob(jobId));
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void Show(GameObject panel, bool visible)
    {
        if (panel != null)
        {
            panel.SetActive(visible);
        }
    }

    private static void Say(TMP_Text label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    /// <summary>Deterministic colour from the prompt, so a regenerate looks like the same idea.</summary>
    private static Color ColourFor(string text)
    {
        int hash = 17;

        foreach (char c in text.ToLowerInvariant())
        {
            hash = hash * 31 + c;
        }

        return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.45f, 0.75f);
    }
}
