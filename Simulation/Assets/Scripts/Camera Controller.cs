using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private int moveSpeed = 10;
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private Vector2 zoomRange = new (1f, 10f);
    [SerializeField] private CheckpointBuilder checkpointBuilder;

    private Camera _camera;

    void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    void Update()
    {
        HandleMovement();
        if(!checkpointBuilder.isBuildMode) HandleZoom();
    }

    private void HandleMovement()
    {
        float xInput = Input.GetAxis("Horizontal");
        float zInput = Input.GetAxis("Vertical");

        // Get the camera's forward and right vectors, but ignore the vertical component.
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        forward.y = 0;
        right.y = 0;

        // Normalize the vectors to ensure consistent movement speed.
        forward.Normalize();
        right.Normalize();

        // Combine the inputs with the camera's orientation to get the final move direction.
        Vector3 moveDirection = (forward * zInput) + (right * xInput);

        transform.localPosition += moveSpeed * Time.deltaTime * moveDirection;
    }

    private void HandleZoom()
    {
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");

        if (_camera.orthographic)
        {
            _camera.orthographicSize -= scrollInput * zoomSpeed;
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize, zoomRange.x, zoomRange.y);
        }
        else
        {
            Vector3 zoomDirection = scrollInput * zoomSpeed * transform.forward;
            transform.position += zoomDirection;
        }
    }
}
