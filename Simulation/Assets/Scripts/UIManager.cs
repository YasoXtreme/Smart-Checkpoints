using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class UIManager : MonoBehaviour
{
    public enum AppState { Welcome, Loading, Software, Simulation }

    [Header("Current State")]
    public AppState currentState = AppState.Welcome;
    private AppState pendingState; // State to transition to after loading
    private bool simulationCreated = false;
    private bool isTransitioning = false;

    [Header("UI Canvases")]
    public Canvas welcomeCanvas;     // "Press Enter to start software" screen
    public Canvas loadingCanvas;     // Contains video player
    public Canvas softwareCanvas;    // Graph editor UI (controlled by GraphUIEditor)

    [Header("Fade Overlay")]
    public CanvasGroup fadeOverlay;  // Full-screen black panel for fades
    public float fadeDuration = 0.5f;

    [Header("Create Simulation UI")]
    public GameObject dropdownPanel;          // Dropdown container
    public Button createSimulationButton;     // "Create New Simulation" button
    public Text connectionStatusText;         // "Connected to: Simulation" text

    [Header("Video Player")]
    public VideoPlayer loadingVideoPlayer;
    public RawImage videoDisplay;             // Display for video (uses RenderTexture)

    [Header("References")]
    public GraphUIEditor graphUIEditor;
    public CarSpawner carSpawner;

    void Start()
    {
        // Initialize all canvases to correct state
        SetCanvasState(welcomeCanvas, true);
        SetCanvasState(loadingCanvas, false);
        SetCanvasState(softwareCanvas, false);

        // Initialize fade overlay (fully transparent)
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
        }

        // Initialize create simulation UI
        if (connectionStatusText != null) connectionStatusText.gameObject.SetActive(false);
        if (createSimulationButton != null)
        {
            createSimulationButton.onClick.AddListener(OnCreateSimulationClicked);
        }

        // Ensure simulation is not running at start
        if (carSpawner != null) carSpawner.simulationActive = false;

        // Prepare video player
        if (loadingVideoPlayer != null)
        {
            loadingVideoPlayer.Stop();
            loadingVideoPlayer.playOnAwake = false;
        }
    }

    void Update()
    {
        if (isTransitioning) return;

        switch (currentState)
        {
            case AppState.Welcome:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    StartCoroutine(TransitionWithLoading(AppState.Software));
                }
                break;

            case AppState.Software:
            case AppState.Simulation:
                if (Input.GetKeyDown(KeyCode.Tab) && simulationCreated)
                {
                    ToggleSoftwareSimulation();
                }
                break;
        }
    }

    /// <summary>
    /// Called when "Create New Simulation" button is clicked
    /// </summary>
    public void OnCreateSimulationClicked()
    {
        if (simulationCreated || isTransitioning) return;

        StartCoroutine(CreateSimulationSequence());
    }

    IEnumerator CreateSimulationSequence()
    {
        isTransitioning = true;
        simulationCreated = true;

        // Update UI: hide button, show connection status
        if (createSimulationButton != null) createSimulationButton.gameObject.SetActive(false);
        if (connectionStatusText != null)
        {
            connectionStatusText.text = "Connected to: Simulation";
            connectionStatusText.gameObject.SetActive(true);
        }

        // Close dropdown panel
        if (dropdownPanel != null) dropdownPanel.SetActive(false);

        // Fade out software
        yield return StartCoroutine(FadeToBlack());

        // Hide software canvas
        if (graphUIEditor != null) graphUIEditor.CloseSoftware();
        SetCanvasState(softwareCanvas, false);

        // Show loading and play video
        SetCanvasState(loadingCanvas, true);
        yield return StartCoroutine(FadeFromBlack());

        // Reset and play video
        PrepareAndPlayVideo();
        yield return StartCoroutine(WaitForVideoEnd());

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Hide loading, show simulation
        SetCanvasState(loadingCanvas, false);
        currentState = AppState.Simulation;

        // Start the simulation (cars begin moving)
        if (carSpawner != null) carSpawner.StartSimulation();

        // Fade in to simulation
        yield return StartCoroutine(FadeFromBlack());

        isTransitioning = false;
    }

    /// <summary>
    /// Transition from Welcome to Software with loading video
    /// </summary>
    IEnumerator TransitionWithLoading(AppState targetState)
    {
        isTransitioning = true;
        pendingState = targetState;

        // Fade out welcome screen
        yield return StartCoroutine(FadeToBlack());

        // Hide welcome canvas
        SetCanvasState(welcomeCanvas, false);

        // Show loading canvas
        SetCanvasState(loadingCanvas, true);
        yield return StartCoroutine(FadeFromBlack());

        // Reset and play video
        PrepareAndPlayVideo();
        yield return StartCoroutine(WaitForVideoEnd());

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Hide loading canvas
        SetCanvasState(loadingCanvas, false);

        // Show target state
        if (pendingState == AppState.Software)
        {
            SetCanvasState(softwareCanvas, true);
            if (graphUIEditor != null) graphUIEditor.OpenSoftware();
        }

        currentState = pendingState;

        // Fade from black
        yield return StartCoroutine(FadeFromBlack());

        isTransitioning = false;
    }

    /// <summary>
    /// Toggle between Software and Simulation with simple fade (no loading video)
    /// </summary>
    void ToggleSoftwareSimulation()
    {
        if (currentState == AppState.Software)
        {
            StartCoroutine(FadeSwitchTo(AppState.Simulation));
        }
        else if (currentState == AppState.Simulation)
        {
            StartCoroutine(FadeSwitchTo(AppState.Software));
        }
    }

    IEnumerator FadeSwitchTo(AppState targetState)
    {
        isTransitioning = true;

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Hide current state
        if (currentState == AppState.Software)
        {
            if (graphUIEditor != null) graphUIEditor.CloseSoftware();
            SetCanvasState(softwareCanvas, false);
        }
        else if (currentState == AppState.Simulation)
        {
            // Simulation view doesn't have a specific canvas to hide
            // Just update state
        }

        // Show target state
        if (targetState == AppState.Software)
        {
            SetCanvasState(softwareCanvas, true);
            if (graphUIEditor != null) graphUIEditor.OpenSoftware();
        }
        else if (targetState == AppState.Simulation)
        {
            // Simulation view - software canvas is hidden, simulation runs in background
        }

        currentState = targetState;

        // Fade from black
        yield return StartCoroutine(FadeFromBlack());

        isTransitioning = false;
    }

    // --- VIDEO PLAYER METHODS ---

    void PrepareAndPlayVideo()
    {
        if (loadingVideoPlayer == null) return;

        // Stop any current playback and reset to frame 0
        loadingVideoPlayer.Stop();
        loadingVideoPlayer.frame = 0;
        loadingVideoPlayer.time = 0;

        // Prepare and play
        loadingVideoPlayer.Prepare();
        loadingVideoPlayer.Play();
    }

    IEnumerator WaitForVideoEnd()
    {
        if (loadingVideoPlayer == null)
        {
            yield return new WaitForSeconds(1f); // Fallback if no video
            yield break;
        }

        // Wait for video to start playing
        while (!loadingVideoPlayer.isPlaying)
        {
            yield return null;
        }

        // Wait for video to finish
        while (loadingVideoPlayer.isPlaying)
        {
            yield return null;
        }

        // Stop and reset for next use
        loadingVideoPlayer.Stop();
        loadingVideoPlayer.frame = 0;
        loadingVideoPlayer.time = 0;
    }

    // --- FADE METHODS ---

    IEnumerator FadeToBlack()
    {
        if (fadeOverlay == null) yield break;

        fadeOverlay.blocksRaycasts = true;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        fadeOverlay.alpha = 1f;
    }

    IEnumerator FadeFromBlack()
    {
        if (fadeOverlay == null) yield break;

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeOverlay.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        fadeOverlay.alpha = 0f;
        fadeOverlay.blocksRaycasts = false;
    }

    // --- HELPER METHODS ---

    void SetCanvasState(Canvas canvas, bool active)
    {
        if (canvas != null) canvas.enabled = active;
    }
}
