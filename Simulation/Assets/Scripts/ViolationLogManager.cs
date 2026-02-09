using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class ViolationLogManager : MonoBehaviour
{
    public enum SaveLocation { AppData, Desktop, CustomPath }

    [Header("Save Settings")]
    public SaveLocation saveLocation = SaveLocation.Desktop;
    public string fileName = "TrafficViolations.csv";
    
    [Tooltip("Only used if 'Custom Path' is selected. Use forward slashes '/'.")]
    public string customPath = "C:/TrafficLogs/"; 

    [Header("UI References")]
    public GameObject violationPanelRoot; 
    public Transform listContent; 
    public GameObject logRowPrefab;
    public Text statusText; 

    private string fullFilePath;

    void Start()
    {
        // 1. Determine the path based on your selection
        string folder = "";

        switch (saveLocation)
        {
            case SaveLocation.AppData:
                folder = Application.persistentDataPath;
                break;
            case SaveLocation.Desktop:
                folder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
                break;
            case SaveLocation.CustomPath:
                folder = customPath;
                // Create directory if it doesn't exist
                if (!Directory.Exists(folder)) 
                {
                    try { Directory.CreateDirectory(folder); }
                    catch { Debug.LogError("Could not create custom folder. Check permissions."); }
                }
                break;
        }

        fullFilePath = Path.Combine(folder, fileName);
        Debug.Log("Saving logs to: " + fullFilePath);

        // 2. Initialize File
        if (!File.Exists(fullFilePath))
        {
            try 
            {
                File.WriteAllText(fullFilePath, "Time,License Plate,From Node,To Node,Detected Speed,Status\n");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error writing file at {fullFilePath}: {e.Message}");
                if (statusText) statusText.text = "Error: Cannot write to file!";
                return;
            }
        }

        if (statusText) statusText.text = $"Logging to: {saveLocation}";

        // 3. Connect to Network
        if (CheckpointNetwork.Instance != null)
        {
            CheckpointNetwork.Instance.OnViolationDetected += HandleViolation;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            if (violationPanelRoot != null)
                violationPanelRoot.SetActive(!violationPanelRoot.activeSelf);
        }
    }

    void OnDestroy()
    {
        if (CheckpointNetwork.Instance != null)
        {
            CheckpointNetwork.Instance.OnViolationDetected -= HandleViolation;
        }
    }

    void HandleViolation(CarPassport car, int fromID, int toID, float speed)
    {
        string timeStr = System.DateTime.Now.ToString("HH:mm:ss");
        string csvLine = $"{timeStr},{car.licensePlate},{fromID},{toID},{speed:F1} km/h,VIOLATION\n";
        
        // Write
        try { File.AppendAllText(fullFilePath, csvLine); }
        catch { Debug.LogWarning("Could not write to log file. Is it open?"); }

        // UI Update
        if (listContent && logRowPrefab)
        {
            GameObject newRow = Instantiate(logRowPrefab, listContent);
            Text[] texts = newRow.GetComponentsInChildren<Text>();
            if (texts.Length >= 3)
            {
                texts[0].text = timeStr;
                texts[1].text = car.licensePlate;
                texts[2].text = $"{speed:F0} km/h";
                texts[2].color = Color.red;
            }
        }

        if (statusText) statusText.text = $"Last: {car.licensePlate} ({speed:F0} km/h)";
    }

    // New Helper to open the exact file location
    [ContextMenu("Open File Location")]
    public void OpenFileLocation()
    {
        if (string.IsNullOrEmpty(fullFilePath)) return;
        string folder = Path.GetDirectoryName(fullFilePath);
        Application.OpenURL(folder);
    }
}