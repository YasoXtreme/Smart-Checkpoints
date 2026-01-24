using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GraphUIEditor : MonoBehaviour
{
    [Header("References")]
    public Canvas softwareCanvas; 
    public Camera mainCamera; 

    [Header("UI Containers")]
    public RectTransform lineContainer; 
    public RectTransform nodeContainer;

    [Header("Properties Panel")]
    public GameObject editPanel; 
    public Text distanceDisplay; 
    public InputField speedInput; 
    public Button closePanelButton; 

    [Header("Prefabs")]
    public GameObject uiNodePrefab; 
    public GameObject connectionLinePrefab; 

    [Header("Visual Settings")]
    public float nodeRadius = 25f; 
    public float mouseOffset = 15f;
    public float bidirectionalSeparation = 10f;
    
    [Header("Interactive Colors")]
    public Color hoverColor = Color.yellow; // NEW: Pick your hover color here!
    public Color normalColor = Color.white;

    [Header("Editing State")]
    public bool isSoftwareOpen = false;
    private int selectedSourceID = -1;
    
    private CheckpointNetwork.Connection currentEditingConnection;

    // Cache
    private Dictionary<int, GameObject> uiNodeInstances = new Dictionary<int, GameObject>();
    private Dictionary<string, GameObject> activeLineInstances = new Dictionary<string, GameObject>();
    private GameObject dragLineInstance;

    void Start()
    {
        if (softwareCanvas != null) softwareCanvas.enabled = false;
        if (mainCamera == null) mainCamera = Camera.main;
        if (editPanel != null) editPanel.SetActive(false);

        if (speedInput != null) speedInput.onEndEdit.AddListener(OnSpeedChanged);
        if (closePanelButton != null) closePanelButton.onClick.AddListener(ClosePanel);

        if(lineContainer) ForceStretch(lineContainer);
        if(nodeContainer) ForceStretch(nodeContainer);
    }

    void ForceStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero; 
        rect.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) ToggleSoftware();

        if (isSoftwareOpen)
        {
            SyncNodes();
            SyncLines();
            HandleInteraction();
        }
    }

    void ToggleSoftware()
    {
        isSoftwareOpen = !isSoftwareOpen;
        if (softwareCanvas != null) softwareCanvas.enabled = isSoftwareOpen;
        if (!isSoftwareOpen) ClosePanel();
    }

    // --- PANEL LOGIC ---
    void OpenPanel(CheckpointNetwork.Connection conn)
    {
        currentEditingConnection = conn;
        editPanel.SetActive(true);
        if (distanceDisplay) distanceDisplay.text = $"Distance: {conn.distanceMeters:F1} m";
        if (speedInput) speedInput.text = conn.speedLimitKmH.ToString();
    }

    void OnSpeedChanged(string val)
    {
        if (currentEditingConnection != null && float.TryParse(val, out float newSpeed))
        {
            CheckpointNetwork.Instance.UpdateConnectionSpeed(
                currentEditingConnection.fromID, 
                currentEditingConnection.toID, 
                newSpeed
            );
        }
    }

    void ClosePanel()
    {
        editPanel.SetActive(false);
        currentEditingConnection = null;
    }

    // --- SYNC LINES (With Hover Logic) ---
    void SyncLines()
    {
        if (CheckpointNetwork.Instance == null) return;
        var connections = CheckpointNetwork.Instance.GetConnections();
        
        HashSet<string> existingKeys = new HashSet<string>();
        foreach(var c in connections) existingKeys.Add(c.fromID + "-" + c.toID);
        HashSet<string> processedKeys = new HashSet<string>();

        foreach (var conn in connections)
        {
            string key = conn.fromID + "-" + conn.toID;
            string reverseKey = conn.toID + "-" + conn.fromID;
            processedKeys.Add(key);

            bool isBidirectional = existingKeys.Contains(reverseKey);

            if (!activeLineInstances.ContainsKey(key))
            {
                GameObject newLine = Instantiate(connectionLinePrefab, lineContainer);
                
                // Add Button
                Button btn = newLine.GetComponent<Button>();
                if (btn == null) btn = newLine.AddComponent<Button>();
                
                // --- NEW: HOVER COLOR SETUP ---
                ColorBlock cb = btn.colors;
                cb.normalColor = normalColor;
                cb.highlightedColor = hoverColor; // The magic happens here
                cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
                cb.colorMultiplier = 1f;
                cb.fadeDuration = 0.1f;
                btn.colors = cb;

                // --- NEW: SMART TARGET GRAPHIC ---
                // If the main image is invisible (Hitbox), try to tint the child (Visible Line)
                Image mainImg = newLine.GetComponent<Image>();
                if (newLine.transform.childCount > 0 && (mainImg == null || mainImg.color.a < 0.05f))
                {
                    // Target the first child (Visual Line)
                    btn.targetGraphic = newLine.transform.GetChild(0).GetComponent<Graphic>();
                }
                
                // Setup Click
                var localConn = conn; 
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OpenPanel(localConn));

                if(newLine.GetComponent<Image>()) newLine.GetComponent<Image>().raycastTarget = true;
                DisableRaycastOnChildren(newLine);

                activeLineInstances.Add(key, newLine);
            }

            if (uiNodeInstances.ContainsKey(conn.fromID) && uiNodeInstances.ContainsKey(conn.toID))
            {
                GameObject nodeA = uiNodeInstances[conn.fromID];
                GameObject nodeB = uiNodeInstances[conn.toID];

                if (nodeA.activeSelf && nodeB.activeSelf)
                {
                    activeLineInstances[key].SetActive(true);

                    // Offset Calculation
                    Vector2 posA = nodeA.GetComponent<RectTransform>().anchoredPosition;
                    Vector2 posB = nodeB.GetComponent<RectTransform>().anchoredPosition;

                    if (isBidirectional)
                    {
                        Vector2 direction = (posB - posA).normalized;
                        Vector2 rightVec = new Vector2(direction.y, -direction.x);
                        Vector2 shift = rightVec * bidirectionalSeparation;
                        posA += shift;
                        posB += shift;
                    }

                    UpdateLineTransform(
                        activeLineInstances[key].GetComponent<RectTransform>(), 
                        posA, 
                        posB, 
                        nodeRadius
                    );
                }
                else activeLineInstances[key].SetActive(false);
            }
        }

        List<string> toRemove = new List<string>();
        foreach (var key in activeLineInstances.Keys)
        {
            if (!processedKeys.Contains(key)) { Destroy(activeLineInstances[key]); toRemove.Add(key); }
        }
        foreach (var key in toRemove) activeLineInstances.Remove(key);
    }

    // --- INTERACTION ---
    void SyncNodes()
    {
        if (CheckpointNetwork.Instance == null) return;
        var realNodes = CheckpointNetwork.Instance.GetAllNodes();
        foreach (var node in realNodes)
        {
            if (!uiNodeInstances.ContainsKey(node.checkpointID))
            {
                GameObject newNode = Instantiate(uiNodePrefab, nodeContainer);
                Text textComp = newNode.GetComponentInChildren<Text>();
                if (textComp != null) textComp.text = node.checkpointID.ToString();
                uiNodeInstances.Add(node.checkpointID, newNode);
            }
        }
        foreach (var kvp in uiNodeInstances)
        {
            SmartCheckpoint realNode = CheckpointNetwork.Instance.GetNode(kvp.Key);
            GameObject uiNode = kvp.Value;
            if (realNode != null && uiNode != null)
            {
                Vector3 screenPos = mainCamera.WorldToScreenPoint(realNode.transform.position);
                if (screenPos.z < 0) uiNode.SetActive(false);
                else 
                {
                    uiNode.SetActive(true);
                    Vector2 localPoint;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(nodeContainer, screenPos, 
                        (softwareCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : mainCamera), out localPoint);
                    uiNode.GetComponent<RectTransform>().anchoredPosition = localPoint;
                }
            }
        }
    }

    void HandleInteraction()
    {
        int hoveredID = GetHoveredNodeID();
        foreach(var node in uiNodeInstances.Values) node.GetComponent<Image>().color = Color.white;
        if(hoveredID != -1) uiNodeInstances[hoveredID].GetComponent<Image>().color = Color.red;

        if (Input.GetMouseButtonDown(1)) 
        {
            if (hoveredID != -1) { selectedSourceID = hoveredID; CreateDragLine(); }
        }

        if (selectedSourceID != -1 && dragLineInstance != null)
        {
            if (uiNodeInstances.ContainsKey(selectedSourceID))
            {
                Vector2 startPos = uiNodeInstances[selectedSourceID].GetComponent<RectTransform>().anchoredPosition;
                Vector2 localMousePos;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(softwareCanvas.transform as RectTransform, 
                    Input.mousePosition, (softwareCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : mainCamera), out localMousePos);
                UpdateLineTransform(dragLineInstance.GetComponent<RectTransform>(), startPos, localMousePos, mouseOffset);
            }
        }

        if (Input.GetMouseButtonUp(1))
        {
            if (selectedSourceID != -1 && hoveredID != -1 && selectedSourceID != hoveredID)
                CheckpointNetwork.Instance.CreateConnection(selectedSourceID, hoveredID, 60f);
            selectedSourceID = -1;
            if (dragLineInstance) Destroy(dragLineInstance);
        }
    }

    void UpdateLineTransform(RectTransform lineRect, Vector2 start, Vector2 end, float offsetFromEnd)
    {
        lineRect.pivot = new Vector2(0, 0.5f);
        lineRect.position = Vector3.zero; 
        lineRect.anchoredPosition = start;
        Vector2 direction = (end - start).normalized;
        float fullDistance = Vector2.Distance(start, end);
        float visualLength = Mathf.Max(0, fullDistance - offsetFromEnd);
        lineRect.sizeDelta = new Vector2(visualLength, lineRect.sizeDelta.y);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        lineRect.localRotation = Quaternion.Euler(0, 0, angle);
    }

    int GetHoveredNodeID()
    {
        Camera uiCam = (softwareCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : mainCamera;
        Vector2 mousePos = Input.mousePosition;
        foreach (var kvp in uiNodeInstances)
        {
            if (kvp.Value == null || !kvp.Value.activeSelf) continue;
            RectTransform rect = kvp.Value.GetComponent<RectTransform>();
            if (RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos, uiCam)) return kvp.Key;
        }
        return -1;
    }

    void CreateDragLine()
    {
        if (dragLineInstance) Destroy(dragLineInstance);
        dragLineInstance = Instantiate(connectionLinePrefab, softwareCanvas.transform); 
        DisableRaycast(dragLineInstance); 
        dragLineInstance.GetComponent<Image>().color = Color.yellow; 
    }

    void DisableRaycastOnChildren(GameObject obj)
    {
        foreach(var graphic in obj.GetComponentsInChildren<Graphic>()) 
        {
            if(graphic.gameObject != obj) graphic.raycastTarget = false;
        }
    }
    
    void DisableRaycast(GameObject obj)
    {
        foreach(var graphic in obj.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
    }
}