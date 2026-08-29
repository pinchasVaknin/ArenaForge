using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 300f;
    public Transform playerBody; // לכאן תגרור את ה-Player מה-Hierarchy

    float xRotation = 0f;

    void Start()
    {
        // נועל ומעלים את סמן העכבר בזמן המשחק
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f); // מונע שבירת מפרקת אחורה

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f); // מסובב את המצלמה למעלה/למטה
        playerBody.Rotate(Vector3.up * mouseX); // מסובב את כל הגוף ימינה/שמאלה
    }
}