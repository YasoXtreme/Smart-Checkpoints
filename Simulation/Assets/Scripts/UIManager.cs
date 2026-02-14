using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class UIManager : MonoBehaviour
{
    public enum AppState { Welcome, Loading, Simulation }

    [Header("Current State")]
    public AppState currentState = AppState.Welcome;
    private bool isTransitioning = false;

    [Header("UI Canvases")]
    public Canvas welcomeCanvas;     // "Press Enter to start" screen
    public Canvas loadingCanvas;     // Contains video player

    [Header("Fade Overlay")]
    public CanvasGroup fadeOverlay;  // Full-screen black panel for fades
    public float fadeDuration = 0.5f;

    [Header("Server Settings Modal")]
    public GameObject settingsModal;         // The modal panel
    public InputField serverHostInput;       // Server host input field
    public InputField apiKeyInput;           // API key input field
    public Button connectDriverButton;       // "Connect Distance Driver" button
    public Text driverStatusText;            // Shows connection status

    [Header("Video Player")]
    public VideoPlayer loadingVideoPlayer;
    public RawImage videoDisplay;             // Display for video (uses RenderTexture)

    [Header("References")]
    public CarSpawner carSpawner;

    private bool settingsModalOpen = false;

    void Start()
    {
        // Initialize all canvases to correct state
        SetCanvasState(welcomeCanvas, true);
        SetCanvasState(loadingCanvas, false);

        // Initialize fade overlay (fully transparent)
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
        }

        // Ensure simulation is not running at start
        if (carSpawner != null) carSpawner.simulationActive = false;

        // Prepare video player
        if (loadingVideoPlayer != null)
        {
            loadingVideoPlayer.Stop();
            loadingVideoPlayer.playOnAwake = false;
        }

        // Initialize settings modal
        if (settingsModal != null) settingsModal.SetActive(false);

        // Load saved settings
        if (serverHostInput != null)
            serverHostInput.text = PlayerPrefs.GetString("ServerHost", "http://localhost:3000");
        if (apiKeyInput != null)
            apiKeyInput.text = PlayerPrefs.GetString("APIKey", "");

        // Setup button listener
        if (connectDriverButton != null)
            connectDriverButton.onClick.AddListener(OnConnectDriverClicked);

        // Subscribe to distance driver status
        if (ServerManager.Instance != null)
            ServerManager.Instance.OnDistanceDriverStatusChanged += OnDriverStatusChanged;

        UpdateDriverStatusText();
    }

    void OnDestroy()
    {
        if (ServerManager.Instance != null)
            ServerManager.Instance.OnDistanceDriverStatusChanged -= OnDriverStatusChanged;
    }

    void Update()
    {
        if (isTransitioning) return;

        switch (currentState)
        {
            case AppState.Welcome:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    StartCoroutine(TransitionToSimulation());
                }
                break;

            case AppState.Simulation:
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    ToggleSettingsModal();
                }
                break;
        }
    }

    /// <summary>
    /// Transition from Welcome directly to Simulation with loading video
    /// </summary>
    IEnumerator TransitionToSimulation()
    {
        isTransitioning = true;

        // Apply saved server settings before starting
        ApplyServerSettings();

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

        currentState = AppState.Simulation;

        // Start the simulation (cars begin moving)
        if (carSpawner != null) carSpawner.StartSimulation();

        // Fade in to simulation
        yield return StartCoroutine(FadeFromBlack());

        isTransitioning = false;
    }

    // --- SETTINGS MODAL ---

    void ToggleSettingsModal()
    {
        Debug.Log(settingsModal == null);
        settingsModalOpen = !settingsModalOpen;
        if (settingsModal != null) settingsModal.SetActive(settingsModalOpen);

        if (!settingsModalOpen)
        {
            // Save and apply settings when closing
            ApplyServerSettings();
        }
    }

    void ApplyServerSettings()
    {
        if (ServerManager.Instance != null)
        {
            if (serverHostInput != null)
            {
                ServerManager.Instance.serverHost = serverHostInput.text;
                PlayerPrefs.SetString("ServerHost", serverHostInput.text);
            }
            if (apiKeyInput != null)
            {
                ServerManager.Instance.apiKey = apiKeyInput.text;
                PlayerPrefs.SetString("APIKey", apiKeyInput.text);
            }
            PlayerPrefs.Save();
        }
    }

    void OnConnectDriverClicked()
    {
        ApplyServerSettings();

        if (ServerManager.Instance == null) return;

        if (ServerManager.Instance.isDistanceDriverConnected)
        {
            ServerManager.Instance.DisconnectDistanceDriver();
        }
        else
        {
            ServerManager.Instance.ConnectDistanceDriver();
        }
    }

    void OnDriverStatusChanged(bool connected)
    {
        UpdateDriverStatusText();
    }

    void UpdateDriverStatusText()
    {
        if (driverStatusText == null) return;

        bool connected = ServerManager.Instance != null && ServerManager.Instance.isDistanceDriverConnected;
        driverStatusText.text = connected ? "Distance Driver: Connected" : "Distance Driver: Disconnected";
        driverStatusText.color = connected ? Color.green : Color.gray;

        if (connectDriverButton != null)
        {
            Text btnText = connectDriverButton.GetComponentInChildren<Text>();
            if (btnText != null)
                btnText.text = connected ? "Disconnect Driver" : "Connect Distance Driver";
        }
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
