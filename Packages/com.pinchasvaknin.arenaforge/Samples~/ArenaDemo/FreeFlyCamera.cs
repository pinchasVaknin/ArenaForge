using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ArenaForge.Samples
{
    /// <summary>
    /// WASD to move, Q and E for height, hold the right mouse button to look. Shift moves faster.
    /// </summary>
    /// <remarks>
    /// Enough camera to walk a generated map and no more. It reads the Input System devices
    /// directly rather than through an action asset, so the demo scene works in a project that has
    /// not bound anything.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("ArenaForge/Free Fly Camera")]
    public sealed class FreeFlyCamera : MonoBehaviour
    {
        [SerializeField]
        float _metresPerSecond = 12f;

        [SerializeField]
        float _sprintMultiplier = 3f;

        [SerializeField]
        float _degreesPerPixel = 0.12f;

        float _yaw;
        float _pitch;

        void Start()
        {
            Vector3 angles = transform.eulerAngles;
            _yaw = angles.y;
            _pitch = angles.x;
        }

        void Update()
        {
            Look();
            Move();
        }

        void Look()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * _degreesPerPixel;
            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void Move()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            // Forward and strafe follow where the camera is looking; up and down stay on the world
            // axis, so looking at the ground does not turn E into a way to fly into it.
            Vector3 direction =
                transform.rotation * new Vector3(
                    Axis(keyboard.dKey, keyboard.aKey), 0f, Axis(keyboard.wKey, keyboard.sKey)) +
                Vector3.up * Axis(keyboard.eKey, keyboard.qKey);

            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            float speed = _metresPerSecond * (keyboard.leftShiftKey.isPressed ? _sprintMultiplier : 1f);
            transform.position += direction.normalized * (speed * Time.deltaTime);
        }

        static float Axis(ButtonControl positive, ButtonControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
