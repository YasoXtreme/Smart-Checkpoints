using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

public class CheckpointNetwork : MonoBehaviour
{
    public static CheckpointNetwork Instance { get; private set; }

    public bool isBuildModeActive = false;
    public event Action<CarPassport, int, int, float> OnViolationDetected;
    public event Action OnNetworkChanged; // Fired when checkpoints/connections change

    [System.Serializable]
    public class Connection
    {
        public int fromID;
        public int toID;
        public float speedLimitKmH;
        public float distanceMeters;
        public float minTraversalTime; // Calculated by ghost cars
        public int currentCarCount = 0;

        public float GetMinTraversalTime()
        {
            // Always calculate from distance and speed limit
            float speedMS = speedLimitKmH / 3.6f;
            if (speedMS <= 0) return 0;
            return distanceMeters / speedMS;
        }

        public void IncrementCarCount()
        {
            currentCarCount++;
        }

        public void DecrementCarCount()
        {
            currentCarCount = Mathf.Max(0, currentCarCount - 1);
        }

        /// <summary>
        /// Returns traffic density as cars per 100 meters
        /// </summary>
        public float GetTrafficDensity()
        {
            if (distanceMeters <= 0) return 0;
            return (currentCarCount / distanceMeters) * 100f;
        }
    }

    private Dictionary<int, SmartCheckpoint> nodes = new Dictionary<int, SmartCheckpoint>();
    private List<Connection> connections = new List<Connection>();

    // Pending time calculations - now tracks ghost cars too
    private Dictionary<(int, int), List<(float time, GhostCar ghost)>> pendingTimeCalcs = 
        new Dictionary<(int, int), List<(float, GhostCar)>>();
    private Dictionary<(int, int), int> expectedCalcCount = new Dictionary<(int, int), int>();
    private Dictionary<(int, int), int> completedCalcCount = new Dictionary<(int, int), int>();

    private void Awake()
    {
        if (Instance != null) Destroy(this);
        else Instance = this;
    }

    public void RegisterNode(SmartCheckpoint cp)
    {
        if (!nodes.ContainsKey(cp.checkpointID)) nodes.Add(cp.checkpointID, cp);
        cp.SetVisuals(isBuildModeActive);
    }

    public void SetBuildMode(bool isActive)
    {
        isBuildModeActive = isActive;
        foreach (var node in nodes.Values) if (node != null) node.SetVisuals(isActive);
    }

    public void CreateConnection(int idA, int idB, float speedLimit)
    {
        if (!nodes.ContainsKey(idA) || !nodes.ContainsKey(idB)) return;

        float dist = CalculatePathDistance(idA, idB);

        Connection newConn = new Connection
        {
            fromID = idA,
            toID = idB,
            speedLimitKmH = speedLimit,
            distanceMeters = dist,
            minTraversalTime = 0 // Will be calculated by ghost cars
        };

        connections.RemoveAll(c => c.fromID == idA && c.toID == idB);
        connections.Add(newConn);

        Debug.Log($"[Network] Connection Created: {idA} -> {idB} (Dist: {dist:F1}m, Limit: {speedLimit}km/h)");

        // Trigger ghost car runs for best path visualization
        RecalculateConnectionTime(idA, idB);

        // Notify cars that network changed
        OnNetworkChanged?.Invoke();
    }

    float CalculatePathDistance(int fromID, int toID)
    {
        float dist = 0f;

        SmartCheckpoint fromCP = nodes[fromID];
        SmartCheckpoint toCP = nodes[toID];

        // Use first anchor waypoint from each checkpoint for distance
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
            dist = Vector3.Distance(nodes[fromID].transform.position, nodes[toID].transform.position);

        return dist;
    }

    /// <summary>
    /// Recalculates the minimum traversal time using ghost cars.
    /// Uses up to 4 waypoints from each checkpoint and finds minimum time.
    /// </summary>
    public void RecalculateConnectionTime(int fromID, int toID)
    {
        if (!nodes.ContainsKey(fromID) || !nodes.ContainsKey(toID)) return;

        SmartCheckpoint fromCP = nodes[fromID];
        SmartCheckpoint toCP = nodes[toID];
        
        // Get all anchor waypoints from each checkpoint
        List<Waypoint> startWPs = fromCP.anchorWaypoints.FindAll(wp => wp != null);
        List<Waypoint> endWPs = toCP.anchorWaypoints.FindAll(wp => wp != null);

        if (startWPs.Count == 0 || endWPs.Count == 0)
        {
            Debug.LogWarning($"[Network] Cannot calculate timing for {fromID} -> {toID}: missing anchor waypoints.");
            return;
        }

        // Initialize tracking for this connection
        var key = (fromID, toID);
        pendingTimeCalcs[key] = new List<(float, GhostCar)>();
        expectedCalcCount[key] = startWPs.Count * endWPs.Count;
        completedCalcCount[key] = 0;

        Debug.Log($"[Network] Starting {expectedCalcCount[key]} ghost runs for connection {fromID} -> {toID}");

        // Request ghost car runs for each start->end pair
        if (GhostCarManager.Instance != null)
        {
            foreach (var startWP in startWPs)
            {
                foreach (var endWP in endWPs)
                {
                    Waypoint capturedStart = startWP;
                    Waypoint capturedEnd = endWP;

                    GhostCarManager.Instance.RequestTimingRun(capturedStart, capturedEnd, (time, ghost) =>
                    {
                        OnGhostCarTimingComplete(fromID, toID, time, ghost, capturedStart, capturedEnd);
                    });
                }
            }
        }
        else
        {
            Debug.LogWarning("[Network] GhostCarManager not available. Using speed-based calculation.");
        }
    }

    void OnGhostCarTimingComplete(int fromID, int toID, float time, GhostCar ghost, Waypoint startWP, Waypoint endWP)
    {
        var key = (fromID, toID);

        if (!pendingTimeCalcs.ContainsKey(key)) return;

        // Track completion
        completedCalcCount[key]++;

        // Only add valid times (path found)
        if (time > 0 && ghost != null)
        {
            pendingTimeCalcs[key].Add((time, ghost));
            Debug.Log($"[Network] Ghost run {fromID} -> {toID}: {time:F2}s (via {startWP?.name} -> {endWP?.name})");
        }
        else
        {
            Debug.Log($"[Network] Ghost run {fromID} -> {toID}: NO PATH (via {startWP?.name} -> {endWP?.name})");
            // Return ghost with no valid path to pool
            if (ghost != null)
            {
                ghost.Reset();
                ghost.gameObject.SetActive(false);
            }
        }

        // Check if all calculations are complete
        if (completedCalcCount[key] >= expectedCalcCount[key])
        {
            if (pendingTimeCalcs[key].Count > 0)
            {
                // Find minimum time and its ghost car
                float minTime = float.MaxValue;
                GhostCar bestGhost = null;

                foreach (var (t, g) in pendingTimeCalcs[key])
                {
                    if (t < minTime)
                    {
                        minTime = t;
                        bestGhost = g;
                    }
                }

                // Mark the best ghost as permanent
                if (bestGhost != null)
                {
                    bestGhost.SetAsBestPath();
                    Debug.Log($"[Network] Marked best path ghost for {fromID} -> {toID} as permanent (time: {minTime:F2}s)");
                }

                // Return non-best ghosts to pool
                foreach (var (t, g) in pendingTimeCalcs[key])
                {
                    if (g != bestGhost && g != null)
                    {
                        g.Reset();
                        g.gameObject.SetActive(false);
                    }
                }

                // Update connection
                Connection conn = connections.Find(c => c.fromID == fromID && c.toID == toID);
                if (conn != null)
                {
                    conn.minTraversalTime = minTime;
                    Debug.Log($"[Network] Connection {fromID} -> {toID} FINAL min traversal time: {minTime:F2}s (from {pendingTimeCalcs[key].Count} valid paths)");
                }
            }
            else
            {
                Debug.LogWarning($"[Network] Connection {fromID} -> {toID}: No valid paths found by ghost cars!");
            }

            // Cleanup
            pendingTimeCalcs.Remove(key);
            expectedCalcCount.Remove(key);
            completedCalcCount.Remove(key);
        }
    }

    public void UpdateConnectionSpeed(int fromID, int toID, float newSpeed)
    {
        Connection c = connections.Find(x => x.fromID == fromID && x.toID == toID);
        if (c != null)
        {
            c.speedLimitKmH = newSpeed;
            // Notify cars that network changed
            OnNetworkChanged?.Invoke();
        }
    }

    public void ReportCheckpointPass(CarPassport car, int currentID)
    {
        int lastID = car.lastCheckpointID;
        float entryTime = Time.time;

        if (lastID == -1)
        {
            // First checkpoint
        }
        else if (lastID == currentID)
        {
            // Ignore re-triggering same checkpoint
            return;
        }
        else
        {
            Connection conn = connections.Find(c => c.fromID == lastID && c.toID == currentID);

            if (conn != null)
            {
                float timeTaken = entryTime - car.lastTimestamp;
                float minLegalTime = conn.GetMinTraversalTime();

                float carSpeed = conn.distanceMeters / timeTaken * 3.6f;

                Debug.Log($"[Network] Car {car.licensePlate}: {conn.distanceMeters:F1}m in {timeTaken:F2}s = {carSpeed:F1} km/h (Limit: {conn.speedLimitKmH}, Min Time: {minLegalTime:F2}),");

                if (timeTaken < minLegalTime)
                {
                    Debug.Log($"[Network] >>> VIOLATION: {car.licensePlate} <<<");
                    car.MarkAsSpeeder();
                    OnViolationDetected?.Invoke(car, lastID, currentID, carSpeed);
                }
            }
        }

        car.lastCheckpointID = currentID;
        car.lastTimestamp = entryTime;
    }

    public List<Connection> GetConnections() { return connections; }
    public SmartCheckpoint GetNode(int id) { return nodes.ContainsKey(id) ? nodes[id] : null; }
    public List<SmartCheckpoint> GetAllNodes() { return new List<SmartCheckpoint>(nodes.Values); }
}