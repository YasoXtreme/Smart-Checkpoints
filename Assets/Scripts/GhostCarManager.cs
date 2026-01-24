using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Manages ghost car spawning, pooling, and callbacks for path discovery and timing calculations.
/// </summary>
public class GhostCarManager : MonoBehaviour
{
    public static GhostCarManager Instance { get; private set; }

    [Header("Settings")]
    public GameObject ghostCarPrefab;
    public int poolSize = 5;
    public float ghostSpeed = 200f;

    private List<GhostCar> pool = new List<GhostCar>();
    private Queue<GhostCar> availableGhosts = new Queue<GhostCar>();
    
    // Track ghost cars used for timing runs (keyed by connection ID pair)
    private Dictionary<(int, int), List<(GhostCar ghost, float time)>> timingRunGhosts = 
        new Dictionary<(int, int), List<(GhostCar, float)>>();

    // Track all best path ghost cars (these are permanent)
    private List<GhostCar> bestPathGhosts = new List<GhostCar>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializePool();
    }

    void Update()
    {
        // Toggle gizmo visibility with 'G' key
        if (Input.GetKeyDown(KeyCode.G))
        {
            ToggleAllGizmos();
        }
    }

    /// <summary>
    /// Toggle gizmo visibility on all ghost cars.
    /// Best path ghost cars will ALWAYS remain visible.
    /// </summary>
    public void ToggleAllGizmos()
    {
        foreach (var ghost in pool)
        {
            if (ghost != null && !ghost.isBestPath)
            {
                ghost.showPathGizmos = !ghost.showPathGizmos;
            }
        }
        Debug.Log("[GhostCarManager] Toggled gizmo visibility (best paths always visible).");
    }

    void InitializePool()
    {
        if (ghostCarPrefab == null)
        {
            Debug.LogWarning("[GhostCarManager] No ghost car prefab assigned. Creating simple ghost cars.");
        }

        for (int i = 0; i < poolSize; i++)
        {
            GhostCar ghost = CreateGhostCar();
            ghost.gameObject.SetActive(false);
            pool.Add(ghost);
            availableGhosts.Enqueue(ghost);
        }
    }

    GhostCar CreateGhostCar()
    {
        GameObject ghostObj;

        if (ghostCarPrefab != null)
        {
            ghostObj = Instantiate(ghostCarPrefab, transform);
        }
        else
        {
            // Create a minimal ghost car
            ghostObj = new GameObject("GhostCar");
            ghostObj.transform.parent = transform;

            // Add a trigger collider for checkpoint detection
            SphereCollider trigger = ghostObj.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2f;
        }

        // Add Rigidbody for OnTriggerEnter to work (required by Unity physics)
        Rigidbody rb = ghostObj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = ghostObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        GhostCar ghost = ghostObj.GetComponent<GhostCar>();
        if (ghost == null)
        {
            ghost = ghostObj.AddComponent<GhostCar>();
        }

        ghost.ghostSpeed = ghostSpeed;
        ghostObj.name = "GhostCar_" + pool.Count;

        return ghost;
    }

    GhostCar GetGhost()
    {
        if (availableGhosts.Count > 0)
        {
            GhostCar ghost = availableGhosts.Dequeue();
            ghost.gameObject.SetActive(true);
            return ghost;
        }

        // Pool exhausted - create new one
        GhostCar newGhost = CreateGhostCar();
        pool.Add(newGhost);
        return newGhost;
    }

    void ReturnGhost(GhostCar ghost)
    {
        ghost.Reset();
        ghost.gameObject.SetActive(false);
        availableGhosts.Enqueue(ghost);
    }

    /// <summary>
    /// Request a ghost car to discover the path between two waypoints.
    /// Returns the list of checkpoints encountered and travel time.
    /// </summary>
    public void RequestPrePath(Waypoint start, Waypoint end, Action<List<SmartCheckpoint>, float> callback)
    {
        if (start == null || end == null)
        {
            callback?.Invoke(new List<SmartCheckpoint>(), 0f);
            return;
        }

        GhostCar ghost = GetGhost();

        ghost.OnPathComplete = (g) =>
        {
            callback?.Invoke(new List<SmartCheckpoint>(g.recordedCheckpoints), g.travelTime);
            ReturnGhost(g);
        };

        ghost.StartPathRun(start, end);
    }

    /// <summary>
    /// Request a ghost car to calculate travel time between two waypoints.
    /// Used for connection timing calculations.
    /// The callback receives (time, ghostCar) so the caller can track which ghost found the best time.
    /// </summary>
    public void RequestTimingRun(Waypoint start, Waypoint end, Action<float, GhostCar> callback)
    {
        if (start == null || end == null)
        {
            callback?.Invoke(0f, null);
            return;
        }

        GhostCar ghost = GetGhost();

        ghost.OnTimingComplete = (time) =>
        {
            callback?.Invoke(time, ghost);
            // NOTE: Don't return ghost here - we keep it until we know if it's the best path
        };

        ghost.StartTimingRun(start, end);
    }
}
