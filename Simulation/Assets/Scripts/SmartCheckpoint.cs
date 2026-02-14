using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider))]
public class SmartCheckpoint : MonoBehaviour
{
    [Header("Identity")]
    public int checkpointID;

    [Header("Multi-Lane Anchor")]
    public List<Waypoint> anchorWaypoints = new List<Waypoint>();
    public bool isPlaced = false;
    public float laneDetectionRadius = 5f;
    public float alignmentTolerance = 0.3f;

    private MeshRenderer meshRenderer;
    private Vector3 lockedPosition;
    private Quaternion lockedRotation;
    private Vector3 lockedScale;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        GetComponent<BoxCollider>().isTrigger = true;

        if (CheckpointNetwork.Instance != null)
        {
            CheckpointNetwork.Instance.RegisterNode(this);
        }
    }

    private void LateUpdate()
    {
        // Lock transform after placement
        if (isPlaced)
        {
            transform.position = lockedPosition;
            transform.rotation = lockedRotation;
            transform.localScale = lockedScale;
        }
    }

    public void LockPlacement()
    {
        isPlaced = true;
        lockedPosition = transform.position;
        lockedRotation = transform.rotation;
        lockedScale = transform.localScale;
    }

    public void SetVisuals(bool isVisible)
    {
        if (meshRenderer) meshRenderer.enabled = isVisible;
    }

    /// <summary>
    /// Finds waypoints within laneDetectionRadius that are spatially aligned
    /// (perpendicular to checkpoint orientation) and returns them.
    /// </summary>
    public List<Waypoint> FindAlignedWaypoints(Waypoint primaryWaypoint)
    {
        List<Waypoint> aligned = new List<Waypoint>();
        if (primaryWaypoint == null) return aligned;

        aligned.Add(primaryWaypoint);

        // Get checkpoint's perpendicular axis (right direction)
        Vector3 checkpointRight = transform.right;
        Vector3 checkpointPos = primaryWaypoint.transform.position;

        Waypoint[] allWaypoints = FindObjectsOfType<Waypoint>();
        foreach (var wp in allWaypoints)
        {
            if (wp == primaryWaypoint) continue;

            Vector3 toWaypoint = wp.transform.position - checkpointPos;
            float distance = toWaypoint.magnitude;

            if (distance > laneDetectionRadius) continue;

            // Check if waypoint is along the perpendicular axis (left-right of checkpoint)
            // Project onto checkpoint's right vector and check if mostly aligned
            float forwardComponent = Mathf.Abs(Vector3.Dot(toWaypoint.normalized, transform.forward));
            
            // If forward component is small, the waypoint is mostly to the side (spatially aligned)
            if (forwardComponent < alignmentTolerance)
            {
                aligned.Add(wp);
            }
        }

        return aligned;
    }

    /// <summary>
    /// Calculates the required width to encompass all anchor waypoints.
    /// Returns the width and the center position offset.
    /// </summary>
    public float CalculateWidthForWaypoints(out Vector3 centerPosition)
    {
        centerPosition = transform.position;
        if (anchorWaypoints == null || anchorWaypoints.Count == 0) return transform.localScale.x;

        // Project all waypoints onto the checkpoint's right axis
        Vector3 right = transform.right;
        float minProj = float.MaxValue;
        float maxProj = float.MinValue;
        Vector3 refPoint = anchorWaypoints[0].transform.position;

        foreach (var wp in anchorWaypoints)
        {
            if (wp == null) continue;
            Vector3 offset = wp.transform.position - refPoint;
            float proj = Vector3.Dot(offset, right);
            if (proj < minProj) minProj = proj;
            if (proj > maxProj) maxProj = proj;
        }

        float width = (maxProj - minProj) + 2f; // Add padding
        float centerOffset = (minProj + maxProj) / 2f;
        centerPosition = refPoint + right * centerOffset;

        return Mathf.Max(width, 2f); // Minimum width of 2
    }

    private void OnTriggerEnter(Collider other)
    {
        CarPassport passport = other.GetComponent<CarPassport>();
        if (passport == null) passport = other.GetComponentInParent<CarPassport>();

        if (passport != null)
        {
            // Notify the CarAgent about checkpoint passage (for route navigation)
            CarAgent agent = passport.GetComponent<CarAgent>();
            if (agent != null)
            {
                agent.OnCheckpointPassed(this);
            }

            // Report to server for speed violation detection
            if (ServerManager.Instance != null &&
                !string.IsNullOrEmpty(ServerManager.Instance.apiKey))
            {
                ServerManager.Instance.ReportCheckpoint(
                    passport.licensePlate,
                    this.checkpointID,
                    (isViolation, carSpeed) =>
                    {
                        if (isViolation)
                        {
                            passport.MarkAsSpeeder();
                        }
                    }
                );
            }
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        // Draw anchor waypoints connections
        if (anchorWaypoints != null && anchorWaypoints.Count > 0)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.cyan;
            foreach (var wp in anchorWaypoints)
            {
                if (wp != null)
                {
                    Gizmos.DrawLine(transform.position, wp.transform.position);
                    Gizmos.DrawWireSphere(wp.transform.position, 0.3f);
                }
            }
        }
    }
}