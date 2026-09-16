using UnityEngine;
using Cinemachine;

public class FreeLookZoomTilt : MonoBehaviour
{
    public CinemachineFreeLook freeLook;

    [Header("Zoom")]
    public float zoomStep = 0.6f;
    public float zoomSmooth = 10f;
    public float minDistance = 3f;
    public float maxDistance = 12f;

    [Header("Tilt")]
    public float minTilt = 10f;
    public float maxTilt = 40f;
    public float tiltSmooth = 6f;

    [Header("Follow Offset (Y)")]
    public float minOffsetY = 1.5f;
    public float maxOffsetY = 0f;
    public float offsetSmooth = 6f;

    private float targetDistance;

    void Start()
    {
        if (freeLook == null)
        {
            freeLook = GetComponent<CinemachineFreeLook>();
        }

        targetDistance = freeLook.m_Orbits[1].m_Radius; // средн€€ орбита как база
    }

    void Update()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            scroll = Mathf.Sign(scroll);
            targetDistance -= scroll * zoomStep;
            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
        }

        // ===== SMOOTH ZOOM =====
        for (int i = 0; i < 3; i++)
        {
            float current = freeLook.m_Orbits[i].m_Radius;
            freeLook.m_Orbits[i].m_Radius = Mathf.Lerp(current, targetDistance, Time.deltaTime * zoomSmooth);
        }

        // ===== NORMALIZED VALUE =====
        float t = Mathf.InverseLerp(maxDistance, minDistance, targetDistance);

        // ===== TILT =====
        float targetTilt = Mathf.Lerp(maxTilt, minTilt, t);
        freeLook.m_XAxis.Value = freeLook.m_XAxis.Value; // оставл€ем горизонтальное вращение
        freeLook.m_YAxis.Value = Mathf.Lerp(freeLook.m_YAxis.Value, targetTilt / 360f, Time.deltaTime * tiltSmooth);

        // ===== TRACK OBJECT OFFSET =====
        float targetOffsetY = Mathf.Lerp(maxOffsetY, minOffsetY, t);
        for (int i = 0; i < 3; i++)
        {
            freeLook.m_Orbits[i].m_Height = Mathf.Lerp(freeLook.m_Orbits[i].m_Height, targetOffsetY, Time.deltaTime * offsetSmooth);
        }
    }
}
