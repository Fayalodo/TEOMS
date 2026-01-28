using UnityEngine;
using Cinemachine;

public class CinemachineZoomTiltSlow1 : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("Шаг зума за один щелчок колеса")]
    public float zoomStep = 0.6f;

    [Tooltip("Сглаживание приближения")]
    public float zoomSmooth = 10f;

    public float minDistance = 3f;
    public float maxDistance = 12f;

    [Header("Tilt")]
    public float minTilt = 10f;   // близко
    public float maxTilt = 40f;   // далеко
    public float tiltSmooth = 6f;

    [Header("Follow Offset (Y)")]
    public float minOffsetY = 1.5f;  // при 10°
    public float maxOffsetY = 0f;    // при 40°
    public float offsetSmooth = 6f;

    private CinemachineVirtualCamera vcam;
    private CinemachineFramingTransposer transposer;

    private float targetDistance;

    void Awake()
    {
        vcam = GetComponent<CinemachineVirtualCamera>();
        transposer = vcam.GetCinemachineComponent<CinemachineFramingTransposer>();

        targetDistance = transposer.m_CameraDistance;
    }

    void Update()
    {
        // ===== SCROLL INPUT =====
        float scroll = Input.mouseScrollDelta.y;

        if (scroll != 0f)
        {
            scroll = Mathf.Sign(scroll); // -1 или +1

            targetDistance -= scroll * zoomStep;
            targetDistance = Mathf.Clamp(
                targetDistance,
                minDistance,
                maxDistance
            );
        }

        // ===== SMOOTH ZOOM =====
        transposer.m_CameraDistance = Mathf.Lerp(
            transposer.m_CameraDistance,
            targetDistance,
            Time.deltaTime * zoomSmooth
        );

        // ===== NORMALIZED VALUE =====
        float t = Mathf.InverseLerp(
            maxDistance,
            minDistance,
            transposer.m_CameraDistance
        );

        // ===== TILT =====
        float targetTilt = Mathf.Lerp(maxTilt, minTilt, t);
        Vector3 rot = transform.eulerAngles;
        rot.x = Mathf.LerpAngle(rot.x, targetTilt, Time.deltaTime * tiltSmooth);
        transform.eulerAngles = rot;

        // ===== TRACK OBJECT OFFSET =====
        float targetOffsetY = Mathf.Lerp(maxOffsetY, minOffsetY, t);
        Vector3 offset = transposer.m_TrackedObjectOffset;
        offset.y = Mathf.Lerp(offset.y, targetOffsetY, Time.deltaTime * offsetSmooth);
        transposer.m_TrackedObjectOffset = offset;
    }
}
