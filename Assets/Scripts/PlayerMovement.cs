using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float speed = 5f;
    public float gravity = -15f; // כוח משיכה חזק כדי שירד מהר במדרגות

    Vector3 velocity;

    void Update()
    {
        // קליטת כיווני הליכה מהמקלדת
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // תנועה יחסית לכיוון שהדמות מסתכלת אליו
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * speed * Time.deltaTime);

        // יישום כוח משיכה
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; // מצמיד חזק לרצפה
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}