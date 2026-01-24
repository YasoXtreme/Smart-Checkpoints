using UnityEngine;
using System.Collections.Generic;

public class Waypoint : MonoBehaviour
{
    [Header("Connections")]
    [Tooltip("Drag the next possible waypoints here to create a one-way road connection.")]
    public List<Waypoint> neighbors = new List<Waypoint>();

    [Header("Settings")]
    [Tooltip("Cost to traverse this segment (higher = traffic/slower road)")]
    public float gCostMultiplier = 1f;

    [Header("Checkpoint Reference")]
    [Tooltip("The checkpoint attached to this waypoint (set automatically)")]
    public SmartCheckpoint attachedCheckpoint;

    // Visual debugging for the Editor
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.5f);

        Gizmos.color = Color.white;
        foreach (var neighbor in neighbors)
        {
            if (neighbor != null)
            {
                // Draw arrow or line to neighbor
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
                
                // Draw a small directional arrow indicator
                Vector3 direction = (neighbor.transform.position - transform.position).normalized;
                Vector3 midPoint = Vector3.Lerp(transform.position, neighbor.transform.position, 0.5f);
                Gizmos.DrawRay(midPoint, Quaternion.Euler(0, 150, 0) * direction * 2f);
                Gizmos.DrawRay(midPoint, Quaternion.Euler(0, -150, 0) * direction * 2f);
            }
        }
    }
}