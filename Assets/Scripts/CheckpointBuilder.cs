using UnityEngine;
using System.Collections.Generic;

public class CheckpointBuilder : MonoBehaviour
{
    [Header("Builder Settings")]
    public GameObject checkpointPrefab;
    public LayerMask waypointLayer;
    public bool isBuildMode = false;

    [Header("Ghost Settings")]
    public Material ghostMaterial;
    public float laneDetectionRadius = 5f;

    private GameObject ghostObj;
    private int currentCheckpointID = 0;
    private Waypoint hoveredWaypoint;
    private List<Waypoint> detectedWaypoints = new List<Waypoint>();

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            isBuildMode = !isBuildMode;
            if (ghostObj) ghostObj.SetActive(isBuildMode);

            if (CheckpointNetwork.Instance != null)
            {
                CheckpointNetwork.Instance.SetBuildMode(isBuildMode);
            }
        }

        if (!isBuildMode) return;

        HandleInteraction();
    }

    private Waypoint lastHoveredWaypoint;
    private bool manualRotationOverride = false;

    void HandleInteraction()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // Find closest waypoint to mouse position
        hoveredWaypoint = FindClosestWaypointToRay(ray);

        if (hoveredWaypoint != null)
        {
            if (ghostObj == null) CreateGhost();

            ghostObj.SetActive(true);
            ghostObj.transform.position = hoveredWaypoint.transform.position + Vector3.up * 1f;

            // Auto-orient ghost to road direction when hovering a new waypoint
            if (hoveredWaypoint != lastHoveredWaypoint)
            {
                lastHoveredWaypoint = hoveredWaypoint;
                manualRotationOverride = false;
                
                Vector3 roadDir = GetRoadDirection(hoveredWaypoint);
                if (roadDir.sqrMagnitude > 0.01f)
                {
                    // Orient ghost perpendicular to road direction (checkpoint spans across road)
                    ghostObj.transform.rotation = Quaternion.LookRotation(roadDir, Vector3.up);
                }
            }

            // Scroll wheel to manually rotate (override auto-orientation)
            if (Input.mouseScrollDelta.y != 0)
            {
                manualRotationOverride = true;
                ghostObj.transform.Rotate(Vector3.up, Input.mouseScrollDelta.y * 10f);
            }

            // Find aligned waypoints and calculate auto-width
            detectedWaypoints = FindAlignedWaypoints(hoveredWaypoint, ghostObj.transform);
            float width = CalculateWidthForWaypoints(detectedWaypoints, ghostObj.transform, out Vector3 centerPos);
            ghostObj.transform.position = centerPos + Vector3.up * 1f;
            ghostObj.transform.localScale = new Vector3(width, 2f, 1f);

            // Place checkpoint on click
            if (Input.GetMouseButtonDown(0))
            {
                PlaceCheckpoint();
            }
        }
        else
        {
            if (ghostObj) ghostObj.SetActive(false);
        }
    }

    /// <summary>
    /// Gets the road direction from a waypoint by averaging its incoming and outgoing directions.
    /// </summary>
    Vector3 GetRoadDirection(Waypoint wp)
    {
        Vector3 avgDirection = Vector3.zero;
        int count = 0;

        // Outgoing direction (to neighbors)
        foreach (var neighbor in wp.neighbors)
        {
            if (neighbor != null)
            {
                avgDirection += (neighbor.transform.position - wp.transform.position).normalized;
                count++;
            }
        }

        // Incoming direction (from waypoints that have this as neighbor)
        Waypoint[] allWaypoints = FindObjectsOfType<Waypoint>();
        foreach (var other in allWaypoints)
        {
            if (other != wp && other.neighbors.Contains(wp))
            {
                avgDirection += (wp.transform.position - other.transform.position).normalized;
                count++;
            }
        }

        if (count > 0)
        {
            avgDirection /= count;
            avgDirection.y = 0; // Keep horizontal
            avgDirection.Normalize();
        }

        return avgDirection;
    }

    Waypoint FindClosestWaypointToRay(Ray ray)
    {
        Waypoint[] allWaypoints = FindObjectsOfType<Waypoint>();
        Waypoint closest = null;
        float minDist = float.MaxValue;

        foreach (var wp in allWaypoints)
        {
            // Calculate distance from waypoint to ray
            Vector3 toWaypoint = wp.transform.position - ray.origin;
            float projectionLength = Vector3.Dot(toWaypoint, ray.direction);
            if (projectionLength < 0) continue; // Behind camera

            Vector3 closestPointOnRay = ray.origin + ray.direction * projectionLength;
            float distance = Vector3.Distance(wp.transform.position, closestPointOnRay);

            // Only consider waypoints within a reasonable distance from the ray
            if (distance < 5f && distance < minDist)
            {
                minDist = distance;
                closest = wp;
            }
        }

        return closest;
    }

    List<Waypoint> FindAlignedWaypoints(Waypoint primary, Transform ghostTransform)
    {
        List<Waypoint> aligned = new List<Waypoint>();
        if (primary == null) return aligned;

        aligned.Add(primary);

        Vector3 checkpointPos = primary.transform.position;
        Waypoint[] allWaypoints = FindObjectsOfType<Waypoint>();

        foreach (var wp in allWaypoints)
        {
            if (wp == primary) continue;

            Vector3 toWaypoint = wp.transform.position - checkpointPos;
            float distance = toWaypoint.magnitude;

            if (distance > laneDetectionRadius) continue;

            // Check if waypoint is along the perpendicular axis
            float forwardComponent = Mathf.Abs(Vector3.Dot(toWaypoint.normalized, ghostTransform.forward));

            if (forwardComponent < 0.3f)
            {
                aligned.Add(wp);
            }
        }

        return aligned;
    }

    float CalculateWidthForWaypoints(List<Waypoint> waypoints, Transform ghostTransform, out Vector3 centerPosition)
    {
        centerPosition = ghostTransform.position;
        if (waypoints == null || waypoints.Count == 0) return 2f;

        Vector3 right = ghostTransform.right;
        float minProj = float.MaxValue;
        float maxProj = float.MinValue;
        Vector3 refPoint = waypoints[0].transform.position;

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            Vector3 offset = wp.transform.position - refPoint;
            float proj = Vector3.Dot(offset, right);
            if (proj < minProj) minProj = proj;
            if (proj > maxProj) maxProj = proj;
        }

        float width = (maxProj - minProj) + 3f; // Add padding for road edges
        float centerOffset = (minProj + maxProj) / 2f;
        centerPosition = refPoint + right * centerOffset;

        return Mathf.Max(width, 3f);
    }

    void PlaceCheckpoint()
    {
        if (checkpointPrefab == null || ghostObj == null) return;

        GameObject newCP = Instantiate(checkpointPrefab, ghostObj.transform.position, ghostObj.transform.rotation);
        newCP.transform.localScale = ghostObj.transform.localScale;
        newCP.name = "Checkpoint_" + currentCheckpointID;

        SmartCheckpoint checkpoint = newCP.GetComponent<SmartCheckpoint>();
        checkpoint.checkpointID = currentCheckpointID;
        checkpoint.anchorWaypoints = new List<Waypoint>(detectedWaypoints);
        checkpoint.laneDetectionRadius = laneDetectionRadius;
        checkpoint.LockPlacement();

        // Set bi-directional references on waypoints
        foreach (var wp in detectedWaypoints)
        {
            if (wp != null)
            {
                wp.attachedCheckpoint = checkpoint;
            }
        }

        currentCheckpointID++;
        Debug.Log($"[CheckpointBuilder] Placed Checkpoint_{checkpoint.checkpointID} with {detectedWaypoints.Count} anchor waypoints.");
    }

    void CreateGhost()
    {
        ghostObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(ghostObj.GetComponent<BoxCollider>());
        if (ghostMaterial != null) ghostObj.GetComponent<Renderer>().material = ghostMaterial;
        ghostObj.name = "Placement_Ghost";
    }

    private void OnDrawGizmos()
    {
        if (!isBuildMode || detectedWaypoints == null) return;

        // Draw detected waypoints
        Gizmos.color = Color.yellow;
        foreach (var wp in detectedWaypoints)
        {
            if (wp != null)
            {
                Gizmos.DrawWireSphere(wp.transform.position, 0.5f);
            }
        }
    }
}