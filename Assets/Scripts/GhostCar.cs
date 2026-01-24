using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Ghost Car - An invisible, high-speed car that traverses the waypoint path
/// to discover checkpoints and calculate travel time. Does not interact with traffic.
/// </summary>
public class GhostCar : MonoBehaviour
{
    [Header("Settings")]
    public float ghostSpeed = 200f; // Very fast movement

    [Header("Debug Visualization")]
    public bool showPathGizmos = true;
    public Color pathColor = Color.green;
    
    // Flag indicating this ghost car found the best (minimum time) path
    [HideInInspector] public bool isBestPath = false;

    // Results
    [HideInInspector] public List<SmartCheckpoint> recordedCheckpoints = new List<SmartCheckpoint>();
    [HideInInspector] public float travelTime = 0f;
    [HideInInspector] public List<Vector3> recordedPath = new List<Vector3>();

    // Callbacks
    public Action<GhostCar> OnPathComplete;
    public Action<float> OnTimingComplete;

    // Internal
    private Waypoint currentWaypoint;
    private Waypoint destinationWaypoint;
    private bool isRunning = false;
    private bool hasCompletedRun = false;
    private float pathPersistTime = 5f; // How long to show the path after completion
    private float completionTime = 0f;

    void Awake()
    {
        // Make invisible - disable all renderers
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }

        // Disable colliders so we don't physically interact
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            // Keep triggers for checkpoint detection
            if (!col.isTrigger)
            {
                col.enabled = false;
            }
        }

        // Assign a unique color based on instance ID
        pathColor = Color.HSVToRGB((GetInstanceID() % 1000) / 1000f, 0.8f, 1f);
    }

    void Update()
    {
        // Clear old completed paths after persistence time
        // BUT only if this is NOT a best path (best paths are permanent)
        if (hasCompletedRun && !isBestPath && Time.time - completionTime > pathPersistTime)
        {
            recordedPath.Clear();
            hasCompletedRun = false;
        }
    }

    /// <summary>
    /// Mark this ghost car's path as the best (minimum time) path.
    /// Best paths are permanent and visible even when gizmos are toggled off.
    /// </summary>
    public void SetAsBestPath()
    {
        isBestPath = true;
        // Make best paths more visually distinct - use a brighter color
        pathColor = Color.black;
    }

    public void StartPathRun(Waypoint start, Waypoint end)
    {
        currentWaypoint = start;
        destinationWaypoint = end;
        recordedCheckpoints.Clear();
        recordedPath.Clear();
        travelTime = 0f;
        isRunning = true;
        hasCompletedRun = false;

        transform.position = start.transform.position;
        recordedPath.Add(start.transform.position);

        StartCoroutine(RunPath());
    }

    public void StartTimingRun(Waypoint start, Waypoint end)
    {
        currentWaypoint = start;
        destinationWaypoint = end;
        recordedPath.Clear();
        travelTime = 0f;
        isRunning = true;
        hasCompletedRun = false;

        transform.position = start.transform.position;
        recordedPath.Add(start.transform.position);

        StartCoroutine(RunTimingPath());
    }

    IEnumerator RunPath()
    {
        List<Waypoint> path = TrafficManager.Instance?.GetPath(currentWaypoint, destinationWaypoint);

        if (path == null || path.Count == 0)
        {
            CompleteRun();
            yield break;
        }

        float startTime = Time.time;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 start = path[i].transform.position;
            Vector3 end = path[i + 1].transform.position;

            yield return StartCoroutine(MoveBetweenPoints(start, end));
        }

        travelTime = Time.time - startTime;
        CompleteRun();
    }

    IEnumerator RunTimingPath()
    {
        List<Waypoint> path = TrafficManager.Instance?.GetPath(currentWaypoint, destinationWaypoint);

        if (path == null || path.Count == 0)
        {
            CompleteTimingRun();
            yield break;
        }

        float startTime = Time.time;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 start = path[i].transform.position;
            Vector3 end = path[i + 1].transform.position;

            yield return StartCoroutine(MoveBetweenPoints(start, end));
        }

        travelTime = Time.time - startTime;
        CompleteTimingRun();
    }

    IEnumerator MoveBetweenPoints(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        float duration = distance / ghostSpeed;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        transform.position = end;
        recordedPath.Add(end);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isRunning) return;

        SmartCheckpoint checkpoint = other.GetComponent<SmartCheckpoint>();
        if (checkpoint == null) checkpoint = other.GetComponentInParent<SmartCheckpoint>();

        if (checkpoint != null && !recordedCheckpoints.Contains(checkpoint))
        {
            recordedCheckpoints.Add(checkpoint);
        }
    }

    void CompleteRun()
    {
        isRunning = false;
        hasCompletedRun = true;
        completionTime = Time.time;
        OnPathComplete?.Invoke(this);
    }

    void CompleteTimingRun()
    {
        isRunning = false;
        hasCompletedRun = true;
        completionTime = Time.time;
        OnTimingComplete?.Invoke(travelTime);
    }

    public void Reset()
    {
        recordedCheckpoints.Clear();
        recordedPath.Clear();
        travelTime = 0f;
        isRunning = false;
        hasCompletedRun = false;
        OnPathComplete = null;
        OnTimingComplete = null;
    }

    private void OnDrawGizmos()
    {
        // Best paths are ALWAYS drawn, regardless of showPathGizmos
        if ((!showPathGizmos && !isBestPath) || recordedPath == null || recordedPath.Count < 2) return;

        Gizmos.color = pathColor;
        
        // Best paths draw higher so they're visible above other gizmos
        float yOffset = isBestPath ? 2.0f : 0.5f;

        // Draw path as connected line segments
        for (int i = 0; i < recordedPath.Count - 1; i++)
        {
            Vector3 from = recordedPath[i];
            Vector3 to = recordedPath[i + 1];

            // Offset above ground for visibility
            from.y += yOffset;
            to.y += yOffset;

            Gizmos.DrawLine(from, to);

            // Draw small sphere at each waypoint
            Gizmos.DrawWireSphere(from, 0.3f);
        }

        // Draw final position
        if (recordedPath.Count > 0)
        {
            Vector3 final = recordedPath[recordedPath.Count - 1];
            final.y += yOffset;
            Gizmos.DrawWireSphere(final, 0.5f);
        }

        // Draw direction arrows
        if (recordedPath.Count >= 2)
        {
            Vector3 start = recordedPath[0];
            Vector3 second = recordedPath[1];
            start.y += yOffset;
            second.y += yOffset;

            Vector3 dir = (second - start).normalized;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(start, dir * 2f);
        }
    }
}

