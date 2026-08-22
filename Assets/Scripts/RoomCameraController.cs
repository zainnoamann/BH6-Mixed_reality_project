using UnityEngine;
using UnityEngine.InputSystem;

public class RoomCameraController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 0.2f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoom = 2f;
    [SerializeField] private float maxZoom = 20f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        Vector3 angles = transform.eulerAngles;

        yaw = angles.y;
        pitch = angles.x;
    }

    private void Update()
    {
        HandleRotation();
        HandleZoom();
    }

    private void HandleRotation()
    {
        if (!Mouse.current.rightButton.isPressed)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        yaw += mouseDelta.x * rotationSpeed;
        pitch -= mouseDelta.y * rotationSpeed;

        pitch = Mathf.Clamp(pitch, -80f, 80f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleZoom()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;

        transform.position += transform.forward * scroll * zoomSpeed * Time.deltaTime;
    }
}
