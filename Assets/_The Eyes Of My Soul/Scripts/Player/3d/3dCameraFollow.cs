using UnityEngine;

public class PlayerRotation : MonoBehaviour
{
    public Cinemachine.CinemachineFreeLook freeLookCamera;
    public float rotationSpeed = 10f;

    void Update()
    {
        if (freeLookCamera == null) return;

        // берём горизонтальное вращение камеры
        float cameraY = freeLookCamera.m_XAxis.Value * 360f; // XAxis.Value от 0 до 1
        // плавное вращение персонажа
        Quaternion targetRotation = Quaternion.Euler(0f, cameraY, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
