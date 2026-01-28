using UnityEngine;

public class CameraPivotRotation : MonoBehaviour
{
    public Transform player;
    public float rotationSpeed = 180f;

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X");

        if (Mathf.Abs(mouseX) > 0.01f)
        {
            transform.Rotate(Vector3.up, mouseX * rotationSpeed * Time.deltaTime);
        }
    }
}
