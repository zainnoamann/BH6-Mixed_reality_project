using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
///   W A S D  - walk, relative to where the camera is facing
///   Shift    - faster
///   Space    - up      
///   Ctrl     - down    
/// </summary>

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float sprintSpeed = 4.0f;

    [Header("Direction")]
    [Tooltip("Movement follows this transform's facing. Assign the Main Camera.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Gravity")]
    [Tooltip("Turn off if the floor has no collider and you keep falling through.")]
    [SerializeField] private bool useGravity = true;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float verticalSpeed = 2.0f;

    private CharacterController controller;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        Vector3 move = ReadDirection();

        float speed = Keyboard.current.leftShiftKey.isPressed
            ? sprintSpeed
            : walkSpeed;

        Vector3 velocity = move * speed;
        velocity.y = ReadVertical();

        controller.Move(velocity * Time.deltaTime);
    }

    private Vector3 ReadDirection()
    {
        float x = 0f;
        float z = 0f;

        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed) z -= 1f;
        if (Keyboard.current.wKey.isPressed) z += 1f;

        if (x == 0f && z == 0f)
            return Vector3.zero;

        // Flatten the camera's facing so looking up or down does not
        // push the player into the floor or the ceiling.
        Vector3 forward = cameraTransform != null
            ? cameraTransform.forward
            : transform.forward;

        Vector3 right = cameraTransform != null
            ? cameraTransform.right
            : transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        return Vector3.ClampMagnitude(forward * z + right * x, 1f);
    }

    private float ReadVertical()
    {
        if (!useGravity)
        {
            float v = 0f;

            if (Keyboard.current.spaceKey.isPressed) v += verticalSpeed;
            if (Keyboard.current.leftCtrlKey.isPressed) v -= verticalSpeed;

            return v;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        return verticalVelocity;
    }
}
