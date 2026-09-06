using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 0.15f;

    private void Update()
    {
        if (Mouse.current == null)
            return;

        // Only rotate while RIGHT mouse button is held
        if (!Mouse.current.rightButton.isPressed)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        transform.Rotate(
            0f,
            mouseDelta.x * rotationSpeed,
            0f
        );
    }
}
