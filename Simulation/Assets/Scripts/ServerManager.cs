using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ServerManager : MonoBehaviour
{
    public static ServerManager Instance { get; private set; }

    [Header("Server Settings")]
    public string serverHost = "http://localhost:3000";
    public string apiKey = "";

    [Header("Distance Driver")]
    public bool isDistanceDriverConnected = false;

    // WebSocket for distance driver
    private ClientWebSocket distanceDriverSocket;
    private CancellationTokenSource wsCancellation;
    private readonly Dictionary<string, Action<float>> pendingDistanceCallbacks =
        new Dictionary<string, Action<float>>();

    // Events
    public event Action<bool> OnDistanceDriverStatusChanged;

    // JSON request/response classes
    [Serializable]
    private class CreateNodeResponse
    {
        public int node_id;
        public int id_in_project;
    }

    [Serializable]
    private class ViolationResponseData
    {
        public bool status;
        public float carSpeed;
        public float legalLimit;
    }

    [Serializable]
    private class WsMessage
    {
        public string type;
        public string requestId;
        public int fromIdInProject;
        public int toIdInProject;
        public int projectId;
        public string message;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        DisconnectDistanceDriver();
    }

    // ============================================================
    //  HTTP: Create Node
    // ============================================================

    /// <summary>
    /// Sends a create-node request to the server with the checkpoint's world coordinates.
    /// Unity X → server x-coord, Unity Z → server y-coord (top-down 2D projection).
    /// </summary>
    public void CreateNode(Vector3 worldPosition, Action<int> onIdInProjectReceived = null)
    {
        StartCoroutine(CreateNodeCoroutine(worldPosition, onIdInProjectReceived));
    }

    private IEnumerator CreateNodeCoroutine(Vector3 worldPos, Action<int> onComplete)
    {
        string url = $"{serverHost}/create-node";
        string json = $"{{\"x-coord\":{worldPos.x},\"y-coord\":{worldPos.z}}}";

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-API-Key", apiKey);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                CreateNodeResponse response = JsonUtility.FromJson<CreateNodeResponse>(req.downloadHandler.text);
                Debug.Log($"[ServerManager] Node created: node_id={response.node_id}, id_in_project={response.id_in_project}");
                onComplete?.Invoke(response.id_in_project);
            }
            else
            {
                Debug.LogError($"[ServerManager] Create node failed: {req.error} — {req.downloadHandler?.text}");
            }
        }
    }

    // ============================================================
    //  HTTP: Report Checkpoint
    // ============================================================

    /// <summary>
    /// Reports a car passing a checkpoint to the server. Timestamp is left to the server.
    /// Returns violation data via callback.
    /// </summary>
    public void ReportCheckpoint(string carPlate, int idInProject, Action<bool, float> onResult)
    {
        StartCoroutine(ReportCheckpointCoroutine(carPlate, idInProject, onResult));
    }

    private IEnumerator ReportCheckpointCoroutine(string carPlate, int idInProject, Action<bool, float> onResult)
    {
        string url = $"{serverHost}/report-checkpoint";
        string json = $"{{\"car-plate\":\"{carPlate}\",\"id-in-project\":{idInProject}}}";

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-API-Key", apiKey);

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                ViolationResponseData response = JsonUtility.FromJson<ViolationResponseData>(req.downloadHandler.text);
                Debug.Log($"[ServerManager] Checkpoint report: plate={carPlate}, violation={response.status}, speed={response.carSpeed:F1}");
                onResult?.Invoke(response.status, response.carSpeed);
            }
            else
            {
                Debug.LogError($"[ServerManager] Report checkpoint failed: {req.error} — {req.downloadHandler?.text}");
                onResult?.Invoke(false, 0f);
            }
        }
    }

    // ============================================================
    //  WebSocket: Distance Driver
    // ============================================================

    /// <summary>
    /// Connects to the server as a distance driver via WebSocket.
    /// </summary>
    public void ConnectDistanceDriver()
    {
        if (isDistanceDriverConnected) return;
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[ServerManager] Cannot connect distance driver: no API key set.");
            return;
        }

        wsCancellation = new CancellationTokenSource();
        _ = ConnectDistanceDriverAsync();
    }

    /// <summary>
    /// Disconnects the distance driver WebSocket.
    /// </summary>
    public void DisconnectDistanceDriver()
    {
        if (wsCancellation != null)
        {
            wsCancellation.Cancel();
            wsCancellation.Dispose();
            wsCancellation = null;
        }

        if (distanceDriverSocket != null)
        {
            if (distanceDriverSocket.State == WebSocketState.Open)
            {
                try
                {
                    _ = distanceDriverSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disconnecting",
                        CancellationToken.None
                    );
                }
                catch (Exception) { }
            }
            distanceDriverSocket.Dispose();
            distanceDriverSocket = null;
        }

        if (isDistanceDriverConnected)
        {
            isDistanceDriverConnected = false;
            OnDistanceDriverStatusChanged?.Invoke(false);
            Debug.Log("[ServerManager] Distance driver disconnected.");
        }
    }

    private async Task ConnectDistanceDriverAsync()
    {
        try
        {
            distanceDriverSocket = new ClientWebSocket();
            string wsUrl = serverHost
                .Replace("http://", "ws://")
                .Replace("https://", "wss://") + "/distance-driver";

            Debug.Log($"[ServerManager] Connecting distance driver to {wsUrl}...");
            await distanceDriverSocket.ConnectAsync(new Uri(wsUrl), wsCancellation.Token);

            // Send authentication
            string authMsg = $"{{\"type\":\"auth\",\"apiKey\":\"{apiKey}\"}}";
            byte[] authBytes = Encoding.UTF8.GetBytes(authMsg);
            await distanceDriverSocket.SendAsync(
                new ArraySegment<byte>(authBytes),
                WebSocketMessageType.Text,
                true,
                wsCancellation.Token
            );

            // Start receive loop
            await ReceiveLoop();
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ServerManager] Distance driver connection error: {ex.Message}");
        }
        finally
        {
            // Ensure cleanup on main thread
            UnityMainThread(() =>
            {
                if (isDistanceDriverConnected)
                {
                    isDistanceDriverConnected = false;
                    OnDistanceDriverStatusChanged?.Invoke(false);
                }
            });
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[8192];
        StringBuilder messageBuilder = new StringBuilder();

        while (distanceDriverSocket != null &&
               distanceDriverSocket.State == WebSocketState.Open &&
               !wsCancellation.Token.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await distanceDriverSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                wsCancellation.Token
            );

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                string message = messageBuilder.ToString();
                messageBuilder.Clear();
                HandleWebSocketMessage(message);
            }
        }
    }

    private void HandleWebSocketMessage(string raw)
    {
        WsMessage msg = JsonUtility.FromJson<WsMessage>(raw);

        switch (msg.type)
        {
            case "authenticated":
                UnityMainThread(() =>
                {
                    isDistanceDriverConnected = true;
                    OnDistanceDriverStatusChanged?.Invoke(true);
                    Debug.Log($"[ServerManager] Distance driver authenticated for project {msg.projectId}.");
                });
                break;

            case "error":
                Debug.LogError($"[ServerManager] Distance driver error: {msg.message}");
                break;

            case "calculate-distance":
                UnityMainThread(() =>
                {
                    HandleDistanceRequest(msg.requestId, msg.fromIdInProject, msg.toIdInProject);
                });
                break;
        }
    }

    private void HandleDistanceRequest(string requestId, int fromIdInProject, int toIdInProject)
    {
        Debug.Log($"[ServerManager] Distance request: {fromIdInProject} -> {toIdInProject} (req: {requestId})");

        if (CheckpointNetwork.Instance == null)
        {
            SendDistanceResult(requestId, 0f);
            return;
        }

        SmartCheckpoint fromCP = CheckpointNetwork.Instance.GetNode(fromIdInProject);
        SmartCheckpoint toCP = CheckpointNetwork.Instance.GetNode(toIdInProject);

        if (fromCP == null || toCP == null)
        {
            Debug.LogWarning($"[ServerManager] Checkpoints not found for distance request: {fromIdInProject}, {toIdInProject}");
            SendDistanceResult(requestId, 0f);
            return;
        }

        // Use ghost car path to calculate distance (using waypoint path like CalculatePathDistance)
        float distance = CalculatePathDistanceBetweenCheckpoints(fromCP, toCP);
        Debug.Log($"[ServerManager] Calculated distance {fromIdInProject} -> {toIdInProject}: {distance:F1}m");
        SendDistanceResult(requestId, distance);
    }

    private float CalculatePathDistanceBetweenCheckpoints(SmartCheckpoint fromCP, SmartCheckpoint toCP)
    {
        float dist = 0f;

        if (fromCP.anchorWaypoints.Count > 0 && toCP.anchorWaypoints.Count > 0)
        {
            Waypoint wpA = fromCP.anchorWaypoints[0];
            Waypoint wpB = toCP.anchorWaypoints[0];

            if (wpA != null && wpB != null && TrafficManager.Instance != null)
            {
                List<Waypoint> path = TrafficManager.Instance.GetPath(wpA, wpB);
                if (path != null && path.Count > 1)
                {
                    for (int i = 0; i < path.Count - 1; i++)
                        dist += Vector3.Distance(path[i].transform.position, path[i + 1].transform.position);
                }
            }
        }

        if (dist <= 0.1f)
            dist = Vector3.Distance(fromCP.transform.position, toCP.transform.position);

        return dist;
    }

    private async void SendDistanceResult(string requestId, float distance)
    {
        if (distanceDriverSocket == null || distanceDriverSocket.State != WebSocketState.Open)
            return;

        try
        {
            // Format distance with invariant culture to avoid comma decimal separators
            string distStr = distance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            string json = $"{{\"type\":\"distance-result\",\"requestId\":\"{requestId}\",\"distance\":{distStr}}}";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await distanceDriverSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                wsCancellation?.Token ?? CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ServerManager] Failed to send distance result: {ex.Message}");
        }
    }

    // ============================================================
    //  Main Thread Dispatcher
    // ============================================================

    private static readonly Queue<Action> mainThreadActions = new Queue<Action>();

    private static void UnityMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    private void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue()?.Invoke();
            }
        }
    }
}
