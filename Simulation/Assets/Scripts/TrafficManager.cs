using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class TrafficManager : MonoBehaviour
{
    public static TrafficManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    /// <summary>
    /// Standard A* Pathfinding Algorithm optimized for Unity Vector3 positions.
    /// </summary>
    public List<Waypoint> GetPath(Waypoint startNode, Waypoint targetNode)
    {
        if (startNode == null || targetNode == null) return null;

        // Open set: Nodes to be evaluated
        List<Waypoint> openSet = new List<Waypoint>();
        
        // HashSet for fast lookup of closed nodes
        HashSet<Waypoint> closedSet = new HashSet<Waypoint>();

        // Dictionary to track where we came from (Parent mapping)
        Dictionary<Waypoint, Waypoint> cameFrom = new Dictionary<Waypoint, Waypoint>();

        // Dictionary to store G Cost (Distance from start)
        Dictionary<Waypoint, float> gScore = new Dictionary<Waypoint, float>();

        // Dictionary to store F Cost (G Cost + Heuristic)
        Dictionary<Waypoint, float> fScore = new Dictionary<Waypoint, float>();

        // Initialize
        openSet.Add(startNode);
        gScore[startNode] = 0;
        fScore[startNode] = Vector3.Distance(startNode.transform.position, targetNode.transform.position);

        while (openSet.Count > 0)
        {
            // Get node with lowest F score
            Waypoint current = openSet.OrderBy(n => fScore.ContainsKey(n) ? fScore[n] : float.MaxValue).First();

            if (current == targetNode)
            {
                return ReconstructPath(cameFrom, current);
            }

            openSet.Remove(current);
            closedSet.Add(current);

            foreach (Waypoint neighbor in current.neighbors)
            {
                if (closedSet.Contains(neighbor)) continue;

                // Calculate tentative G Score
                float tentativeGScore = gScore[current] + Vector3.Distance(current.transform.position, neighbor.transform.position) * neighbor.gCostMultiplier;

                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (tentativeGScore >= (gScore.ContainsKey(neighbor) ? gScore[neighbor] : float.MaxValue))
                {
                    continue; // This is not a better path
                }

                // This path is the best until now. Record it!
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeGScore;
                fScore[neighbor] = gScore[neighbor] + Vector3.Distance(neighbor.transform.position, targetNode.transform.position);
            }
        }

        // No path found
        return null;
    }

    private List<Waypoint> ReconstructPath(Dictionary<Waypoint, Waypoint> cameFrom, Waypoint current)
    {
        List<Waypoint> totalPath = new List<Waypoint> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            totalPath.Insert(0, current);
        }
        return totalPath;
    }
}