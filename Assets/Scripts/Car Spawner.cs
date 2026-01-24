using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CarSpawner : MonoBehaviour
{
    public enum SpawnerState { Normal, Clearing, Testing }

    [Header("References")]
    public GameObject[] cars;

    [System.Serializable]
    public class WeightedPoint
    {
        public Transform point;
        public float weight = 10f;
    }

    public WeightedPoint[] startpoints;
    public WeightedPoint[] endpoints;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnInterval = 5f;
    [SerializeField] private int maxCars = 8;
    [SerializeField] private int secondsBeforeCarDestruction = 3;

    [Header("Behavior Distribution")]
    [Range(0, 1)] public float ratioSafe = 0.5f;
    [Range(0, 1)] public float ratioSpeeding = 0.3f;
    // Smart Speeder ratio is remainder (1.0 - Safe - Speeding)

    [Header("Test Plan Settings")]
    public string testSavePath = "C:/TrafficTestPlan/";
    public int totalCarsToTest = 500;

    [Header("Status")]
    public SpawnerState currentState = SpawnerState.Normal;
    public int testCarsSpawned = 0;
    public int testCarsFinished = 0;

    // Internal
    private int carCount = 0;
    private float timer = 0f;

    // Data Collection
    private struct TestResult
    {
        public string id;
        public string behavior;
        public bool actuallySpeeding;
        public bool wasDetected;
    }
    private List<TestResult> results = new List<TestResult>();

    // Pending car spawns waiting for ghost car pre-path
    private Queue<PendingCarSpawn> pendingSpawns = new Queue<PendingCarSpawn>();

    private struct PendingCarSpawn
    {
        public Waypoint startWaypoint;
        public Waypoint endWaypoint;
        public CarBehavior behavior;
    }

    void Awake()
    {
        Random.InitState(System.DateTime.Now.Millisecond);
    }

    void Update()
    {
        // TRIGGER TEST PLAN
        if (Input.GetKeyDown(KeyCode.T) && currentState == SpawnerState.Normal)
        {
            StartCoroutine(RunTestPlan());
        }

        // SPAWN LOGIC
        if (currentState == SpawnerState.Clearing)
        {
            // Do nothing, waiting for cars to die
        }
        else if (currentState == SpawnerState.Testing)
        {
            timer += Time.deltaTime;
            if (timer >= 0.5f && carCount < maxCars && testCarsSpawned < totalCarsToTest)
            {
                RequestCarSpawn();
                timer = 0f;
            }
        }
        else // Normal
        {
            timer += Time.deltaTime;
            if ((timer >= spawnInterval && carCount < maxCars) || Input.GetKeyDown("space"))
            {
                RequestCarSpawn();
                timer = 0f;
            }
        }
    }

    IEnumerator RunTestPlan()
    {
        Debug.Log("--- STARTING TEST PLAN ---");

        currentState = SpawnerState.Clearing;
        Debug.Log("Phase 1: Clearing existing traffic...");

        yield return new WaitUntil(() => carCount == 0);

        Debug.Log("Phase 2: Spawning Test Cars...");
        currentState = SpawnerState.Testing;
        testCarsSpawned = 0;
        testCarsFinished = 0;
        results.Clear();

        yield return new WaitUntil(() => testCarsFinished >= totalCarsToTest);

        Debug.Log("Phase 3: Saving Data...");
        SaveTestResults();

        currentState = SpawnerState.Normal;
        Debug.Log("--- TEST PLAN COMPLETE ---");
    }

    void RequestCarSpawn()
    {
        if (cars.Length == 0 || startpoints.Length == 0 || endpoints.Length == 0) return;

        // Find start and end waypoints
        Waypoint startWaypoint = null, endWaypoint = null;
        int attempts = 0;
        while (startWaypoint == endWaypoint && attempts < 100)
        {
            Transform startTr = GetWeightedPoint(startpoints);
            Transform endTr = GetWeightedPoint(endpoints);

            if (startTr != null) startWaypoint = startTr.GetComponent<Waypoint>();
            if (endTr != null) endWaypoint = endTr.GetComponent<Waypoint>();

            attempts++;
        }

        if (startWaypoint == null || endWaypoint == null) return;

        // Determine behavior
        CarBehavior behavior = DetermineBehavior();

        // Request ghost car pre-path
        if (GhostCarManager.Instance != null)
        {
            GhostCarManager.Instance.RequestPrePath(startWaypoint, endWaypoint, (checkpoints, travelTime) =>
            {
                SpawnCarWithPath(startWaypoint, endWaypoint, behavior, checkpoints);
            });
        }
        else
        {
            // No ghost car manager - spawn without pre-path
            SpawnCarWithPath(startWaypoint, endWaypoint, behavior, new List<SmartCheckpoint>());
        }
    }

    CarBehavior DetermineBehavior()
    {
        float rnd = Random.value;

        if (rnd < ratioSafe)
            return CarBehavior.Safe;
        else if (rnd < ratioSafe + ratioSpeeding)
            return CarBehavior.Speeding;
        else
            return CarBehavior.SmartSpeeder;
    }

    void SpawnCarWithPath(Waypoint startWaypoint, Waypoint endWaypoint, CarBehavior behavior, List<SmartCheckpoint> routeCheckpoints)
    {
        int randomIndex = Random.Range(0, cars.Length);
        GameObject car = Instantiate(cars[randomIndex], startWaypoint.transform.position, startWaypoint.transform.rotation);

        CarAgent carAgent = car.GetComponent<CarAgent>();
        CarPassport passport = car.GetComponent<CarPassport>();

        // Setup behavior
        passport.behaviorType = behavior;

        // Setup path knowledge from ghost car
        carAgent.routeCheckpoints = routeCheckpoints;
        carAgent.currentCheckpointIndex = 0;

        // Set initial connection speed limit if we have checkpoints
        if (routeCheckpoints.Count >= 2)
        {
            var connection = CheckpointNetwork.Instance?.GetConnections()
                .Find(c => c.fromID == routeCheckpoints[0].checkpointID &&
                           c.toID == routeCheckpoints[1].checkpointID);
            if (connection != null)
            {
                carAgent.currentConnectionSpeedLimit = connection.speedLimitKmH;
            }
        }

        // Setup waypoints
        carAgent.currentWaypoint = startWaypoint;
        carAgent.destinationWaypoint = endWaypoint;

        // Setup events
        carAgent.OnDestinationReached += () =>
        {
            if (currentState == SpawnerState.Testing) RecordResult(passport);
            CarReachedDestination(car);
        };
        carAgent.OnLifeTimeExpired += DecreaseCarCount;

        // Start movement
        carAgent.StartCoroutine(carAgent.MoveToDestination());
        carCount++;

        if (currentState == SpawnerState.Testing)
        {
            testCarsSpawned++;
        }
    }

    void RecordResult(CarPassport p)
    {
        TestResult r = new TestResult();
        r.id = p.licensePlate;
        r.behavior = p.behaviorType.ToString();
        r.actuallySpeeding = p.wasActuallySpeeding;
        r.wasDetected = p.wasDetected;

        results.Add(r);
        testCarsFinished++;

        if (testCarsFinished % 50 == 0)
            Debug.Log($"Test Progress: {testCarsFinished}/{totalCarsToTest}");
    }

    private void CarReachedDestination(GameObject carToDestroy)
    {
        carCount--;
        Destroy(carToDestroy, secondsBeforeCarDestruction);
    }

    private void DecreaseCarCount()
    {
        carCount--;
        if (currentState == SpawnerState.Testing) testCarsFinished++;
    }

    void SaveTestResults()
    {
        if (!Directory.Exists(testSavePath)) Directory.CreateDirectory(testSavePath);

        string filePath = Path.Combine(testSavePath, "TestPlan.csv");
        string header = "LicensePlate,Behavior,ActuallySpeeding,WasDetected,Result\n";

        using (StreamWriter sw = new StreamWriter(filePath))
        {
            sw.Write(header);
            foreach (var r in results)
            {
                string outcome = "OK";
                if (r.actuallySpeeding && !r.wasDetected) outcome = "MISSED_SPEEDER";
                if (!r.actuallySpeeding && r.wasDetected) outcome = "FALSE_ALARM";

                string line = $"{r.id},{r.behavior},{r.actuallySpeeding},{r.wasDetected},{outcome}";
                sw.WriteLine(line);
            }
        }
        Debug.Log($"Test Plan saved to: {filePath}");
    }

    private Transform GetWeightedPoint(WeightedPoint[] points)
    {
        if (points == null || points.Length == 0) return null;

        float totalWeight = 0f;
        foreach (var p in points) totalWeight += Mathf.Max(0, p.weight);

        if (totalWeight <= 0) return points[Random.Range(0, points.Length)].point;

        float rnd = Random.Range(0, totalWeight);
        float current = 0f;

        foreach (var p in points)
        {
            current += Mathf.Max(0, p.weight);
            if (rnd <= current) return p.point;
        }
        return points[0].point;
    }
}