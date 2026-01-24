using UnityEngine;

public enum CarBehavior { Safe, Speeding, SmartSpeeder }

public class CarPassport : MonoBehaviour
{
    [Header("Identity")]
    public string licensePlate;
    public CarBehavior behaviorType;

    [Header("Detection Data")]
    public bool wasActuallySpeeding = false;
    public bool wasDetected = false;

    [Header("Visuals")]
    public GameObject speederTag;

    // Network Data
    [HideInInspector] public int lastCheckpointID = -1;
    [HideInInspector] public float lastTimestamp = 0f;

    private void Start()
    {
        if (string.IsNullOrEmpty(licensePlate)) licensePlate = GenerateRandomPlate();
        if (speederTag) speederTag.SetActive(false);
    }

    public void MarkAsSpeeder()
    {
        wasDetected = true; // Recorded for the Test Plan
        
        var rend = GetComponentInChildren<Renderer>();
        if(rend) rend.material.color = Color.red;
        if (speederTag) speederTag.SetActive(true);
    }

    string GenerateRandomPlate()
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string nums = "0123456789";
        return $"{chars[Random.Range(0, 26)]}{chars[Random.Range(0, 26)]}{chars[Random.Range(0, 26)]}-{nums[Random.Range(0, 10)]}{nums[Random.Range(0, 10)]}{nums[Random.Range(0, 10)]}";
    }
}