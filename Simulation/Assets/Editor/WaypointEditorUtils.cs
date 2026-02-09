#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class WaypointEditorUtils : MonoBehaviour
{
    // 1. Link Option (Connects A -> B -> C -> D)
    [MenuItem("Traffic/Link Selected Waypoints")]
    static void LinkWaypoints()
    {
        GameObject[] selected = Selection.gameObjects;

        if (selected.Length < 2)
        {
            Debug.LogWarning("Select at least 2 Waypoints to link them.");
            return;
        }

        int linksMade = 0;

        // Sort by hierarchy order or selection order
        for (int i = 0; i < selected.Length - 1; i++)
        {
            Waypoint source = selected[i].GetComponent<Waypoint>();
            Waypoint dest = selected[i + 1].GetComponent<Waypoint>();

            if (source != null && dest != null)
            {
                // Allow Undo (Ctrl+Z)
                Undo.RecordObject(source, "Link Waypoints");

                if (!source.neighbors.Contains(dest))
                {
                    source.neighbors.Add(dest);
                    EditorUtility.SetDirty(source); // Ensure Unity saves the change
                    linksMade++;
                }
            }
        }
        Debug.Log($"Linked {linksMade} waypoint segments.");
    }

    // 2. Unlink Option (Clears all neighbors for selected nodes)
    [MenuItem("Traffic/Unlink Selected Waypoints")]
    static void UnlinkWaypoints()
    {
        GameObject[] selected = Selection.gameObjects;
        int unlinkedCount = 0;

        foreach (GameObject go in selected)
        {
            Waypoint wp = go.GetComponent<Waypoint>();
            if (wp != null)
            {
                // Allow Undo (Ctrl+Z)
                Undo.RecordObject(wp, "Unlink Waypoints");

                wp.neighbors.Clear();
                
                EditorUtility.SetDirty(wp); // Save changes
                unlinkedCount++;
            }
        }
        Debug.Log($"Cleared connections for {unlinkedCount} waypoints.");
    }
}
#endif