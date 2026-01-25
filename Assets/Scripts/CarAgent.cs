using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class CarAgent : MonoBehaviour
{
    [Header("Movement")]
    public float rotationSpeed = 5f;

    [Header("Radius-Based Collision")]
    public float innerRadius = 3f;
    public float outerRadius = 10f;
    public float innerAngle = 20f;
    public float outerAngle = 45f;
    public LayerMask carLayer;

    [Header("Interpolation")]
    public float acceleration = 5f;
    public float deceleration = 8f;

    [Header("Path Knowledge")]
    public List<SmartCheckpoint> routeCheckpoints = new List<SmartCheckpoint>();
    public int currentCheckpointIndex = 0;
    public float currentConnectionSpeedLimit = 50f;

    [Header("Behavior Settings")]
    [Range(0f, 1f)] public float speedOffsetPercent = 0.1f; // 10% default
    [Range(0f, 1f)] public float safeRangePercent = 0.1f;
    [Range(0f, 1f)] public float dangerousRangePercent = 0.5f;
    public float checkpointAwarenessRadius = 30f;

    [Header("Pathfinding")]
    public Waypoint currentWaypoint;
    public Waypoint destinationWaypoint;
    [SerializeField] private float maximumLifeTime = 60f;

    // Public State
    [HideInInspector] public float currentSpeed = 0f;
    [HideInInspector] public float randomFactor;
    [HideInInspector] public float targetSpeed;
    [HideInInspector] public float spawnTime;
    [HideInInspector] public CarAgent limitingCar;

    [Header("Debug Info")]
    [SerializeField] private float targetSpeedKmH; // Visible in inspector

    // Events
    public event Action OnDestinationReached;
    public event Action OnLifeTimeExpired;

    // Private
    private List<Waypoint> currentPath;
    private CarPassport passport;
    private CarAgent previousLimitingCar;
    private bool isIgnoringDeadlock = false;
    private float lifeTime = 0f;
    private CheckpointNetwork.Connection currentConnection;

    private const float SPEED_CONVERSION = 3.6f;

    void Start()
    {
        passport = GetComponent<CarPassport>();
        spawnTime = Time.time;
        randomFactor = UnityEngine.Random.Range(0f, 1f);
        
        // Subscribe to network changes to refresh route
        if (CheckpointNetwork.Instance != null)
        {
            CheckpointNetwork.Instance.OnNetworkChanged += RefreshRouteCheckpoints;
        }
        
        CalculateTargetSpeed();
        currentSpeed = targetSpeed;
    }

    void OnDestroy()
    {
        // Decrement car count from current connection
        if (currentConnection != null)
        {
            currentConnection.DecrementCarCount();
        }

        // Unsubscribe from network changes
        if (CheckpointNetwork.Instance != null)
        {
            CheckpointNetwork.Instance.OnNetworkChanged -= RefreshRouteCheckpoints;
        }
    }

    void RefreshRouteCheckpoints()
    {
        // Request new path knowledge from ghost car
        if (GhostCarManager.Instance != null && currentWaypoint != null && destinationWaypoint != null)
        {
            GhostCarManager.Instance.RequestPrePath(currentWaypoint, destinationWaypoint, (checkpoints, time) =>
            {
                routeCheckpoints = checkpoints;
                currentCheckpointIndex = 0;
                Debug.Log($"[CarAgent] Refreshed route: {checkpoints.Count} checkpoints");
            });
        }
    }

    void Update()
    {
        HandleTrafficLogic();

        lifeTime += Time.deltaTime;
        if (lifeTime > maximumLifeTime)
        {
            OnLifeTimeExpired?.Invoke();
            Destroy(gameObject);
        }
    }

    void CalculateTargetSpeed()
    {
        if (passport == null)
        {
            targetSpeed = currentConnectionSpeedLimit / SPEED_CONVERSION;
            targetSpeedKmH = currentConnectionSpeedLimit;
            return;
        }

        float baseSpeed = currentConnectionSpeedLimit;
        float offset = speedOffsetPercent * baseSpeed;

        switch (passport.behaviorType)
        {
            case CarBehavior.Safe:
                // Drive at speed limit minus offset and random reduction
                targetSpeed = (baseSpeed - offset - (randomFactor * safeRangePercent * baseSpeed)) / SPEED_CONVERSION;
                break;

            case CarBehavior.Speeding:
                // Drive above speed limit plus offset
                targetSpeed = (baseSpeed + offset + (randomFactor * dangerousRangePercent * baseSpeed)) / SPEED_CONVERSION;
                passport.wasActuallySpeeding = true;
                break;

            case CarBehavior.SmartSpeeder:
                // Check if next checkpoint is nearby
                bool nearCheckpoint = IsNearNextCheckpoint();
                if (nearCheckpoint)
                {
                    // Fake compliance: subtract offset
                    targetSpeed = (baseSpeed - offset - (randomFactor * safeRangePercent * baseSpeed)) / SPEED_CONVERSION;
                }
                else
                {
                    // Speeding: add offset
                    targetSpeed = (baseSpeed + offset + (randomFactor * dangerousRangePercent * baseSpeed)) / SPEED_CONVERSION;
                    passport.wasActuallySpeeding = true;
                }
                break;
        }

        // Update inspector-visible target speed in km/h
        targetSpeedKmH = targetSpeed * SPEED_CONVERSION;
    }

    bool IsNearNextCheckpoint()
    {
        if (routeCheckpoints == null || routeCheckpoints.Count == 0) return false;
        if (currentCheckpointIndex >= routeCheckpoints.Count) return false;

        SmartCheckpoint nextCP = routeCheckpoints[currentCheckpointIndex];
        if (nextCP == null) return false;

        float dist = Vector3.Distance(transform.position, nextCP.transform.position);
        return dist <= checkpointAwarenessRadius;
    }

    void HandleTrafficLogic()
    {
        // Recalculate target speed every frame (for Smart Speeder checkpoint awareness)
        CalculateTargetSpeed();

        float desiredSpeed = targetSpeed;
        CarAgent newLimitingCar = null;
        float closestLimitingDistance = float.MaxValue;
        bool instantStop = false;

        // Scan for nearby cars
        Collider[] hits = Physics.OverlapSphere(transform.position, outerRadius, carLayer);
        foreach (var hit in hits)
        {
            if (hit.transform.root == transform.root) continue;

            CarAgent otherCar = hit.GetComponentInParent<CarAgent>();
            if (otherCar == null) continue;

            Vector3 directionToCar = (hit.transform.position - transform.position).normalized;
            float distance = Vector3.Distance(transform.position, hit.transform.position);
            float angle = Vector3.Angle(transform.forward, directionToCar);

            // Check if within angle zones
            if (angle < outerAngle)
            {
                // Check for deadlock - if we should ignore this car
                if (isIgnoringDeadlock && otherCar == previousLimitingCar) continue;

                // INNER RADIUS: Instant stop (no interpolation)
                if (distance < innerRadius)
                {
                    instantStop = true;
                    if (distance < closestLimitingDistance)
                    {
                        closestLimitingDistance = distance;
                        newLimitingCar = otherCar;
                    }
                }
                // OUTER RADIUS zones
                else if (angle < innerAngle)
                {
                    // Inner angle: Match speed with interpolation
                    desiredSpeed = Mathf.Min(desiredSpeed, otherCar.currentSpeed);
                    if (distance < closestLimitingDistance)
                    {
                        closestLimitingDistance = distance;
                        newLimitingCar = otherCar;
                    }
                }
                else
                {
                    // Outer angle (between inner and outer): Stop with interpolation
                    desiredSpeed = 0f;
                    if (distance < closestLimitingDistance)
                    {
                        closestLimitingDistance = distance;
                        newLimitingCar = otherCar;
                    }
                }
            }
        }

        // Update limiting car
        if (newLimitingCar != previousLimitingCar)
        {
            isIgnoringDeadlock = false; // Reset deadlock flag when limiting car changes
        }
        limitingCar = newLimitingCar;
        previousLimitingCar = newLimitingCar;

        // DEADLOCK RESOLUTION
        if (limitingCar != null && limitingCar.limitingCar == this)
        {
            // Mutual blocking detected - older car wins
            if (spawnTime < limitingCar.spawnTime)
            {
                isIgnoringDeadlock = true;
                desiredSpeed = targetSpeed; // Ignore the constraint
                instantStop = false;
            }
        }

        // Apply speed
        if (instantStop)
        {
            currentSpeed = 0f; // Instant stop, no interpolation
        }
        else
        {
            // Smooth interpolation
            float rate = (desiredSpeed < currentSpeed) ? deceleration : acceleration;
            currentSpeed = Mathf.Lerp(currentSpeed, desiredSpeed, rate * Time.deltaTime);
        }
    }

    public void OnCheckpointPassed(SmartCheckpoint checkpoint)
    {
        // Find and advance checkpoint index
        for (int i = currentCheckpointIndex; i < routeCheckpoints.Count; i++)
        {
            if (routeCheckpoints[i] == checkpoint)
            {
                currentCheckpointIndex = i + 1;

                // Update connection speed limit from checkpoint network
                if (i + 1 < routeCheckpoints.Count)
                {
                    var newConnection = CheckpointNetwork.Instance?.GetConnections()
                        .Find(c => c.fromID == checkpoint.checkpointID && 
                                   c.toID == routeCheckpoints[i + 1].checkpointID);
                    if (newConnection != null)
                    {
                        currentConnectionSpeedLimit = newConnection.speedLimitKmH;

                        // Track connection change and update car counts
                        if (currentConnection != newConnection)
                        {
                            if (currentConnection != null)
                            {
                                currentConnection.DecrementCarCount();
                            }
                            currentConnection = newConnection;
                            currentConnection.IncrementCarCount();
                        }
                    }
                }
                else
                {
                    // Reached the end of the route, leaving current connection
                    if (currentConnection != null)
                    {
                        currentConnection.DecrementCarCount();
                        currentConnection = null;
                    }
                }
                break;
            }
        }
    }

    public IEnumerator MoveToDestination()
    {
        currentPath = TrafficManager.Instance.GetPath(currentWaypoint, destinationWaypoint);

        if (currentPath == null || currentPath.Count == 0)
        {
            OnDestinationReached?.Invoke();
            yield break;
        }

        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Waypoint p0 = (i - 1 >= 0) ? currentPath[i - 1] : currentPath[i];
            Waypoint p1 = currentPath[i];
            Waypoint p2 = currentPath[i + 1];
            Waypoint p3 = (i + 2 < currentPath.Count) ? currentPath[i + 2] : currentPath[i + 1];

            yield return StartCoroutine(FollowSplineSegment(p0.transform.position, p1.transform.position, p2.transform.position, p3.transform.position));

            currentWaypoint = currentPath[i + 1];
        }

        currentSpeed = 0f;
        OnDestinationReached?.Invoke();
    }

    IEnumerator FollowSplineSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float totalDistance = Vector3.Distance(p1, p2);
        float distanceTraveled = 0f;

        while (distanceTraveled < totalDistance)
        {
            if (currentSpeed <= 0.01f)
            {
                yield return null;
                continue;
            }

            float moveStep = currentSpeed * Time.deltaTime;
            distanceTraveled += moveStep;

            float t = distanceTraveled / totalDistance;
            if (t > 1f) t = 1f;

            Vector3 nextPos = SplineMath.GetCatmullRomPosition(t, p0, p1, p2, p3);
            transform.position = nextPos;

            Vector3 direction = (SplineMath.GetCatmullRomPosition(Mathf.Min(t + 0.1f, 1f), p0, p1, p2, p3) - transform.position).normalized;
            if (direction != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            yield return null;
        }
    }

    private void OnDrawGizmos()
    {
        // Draw detection radii
        Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, innerRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, outerRadius);

        // Draw path
        if (currentPath != null)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < currentPath.Count - 1; i++)
                Gizmos.DrawLine(currentPath[i].transform.position, currentPath[i + 1].transform.position);
        }
    }
}