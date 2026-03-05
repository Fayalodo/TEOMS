using UnityEngine;

/// <summary>
/// Хелпер для выбора спрайта по 8 направлениям.
/// Используется в PlayerMovement и PlayerCombat — без дубликации кода.
/// </summary>
public static class SpriteDirectionHelper
{
    public static Sprite GetSpriteForDirection(
        Vector3 worldDir,
        bool relativeToCamera,
        Transform cameraTransform,
        Sprite forward, Sprite forwardRight, Sprite right, Sprite backRight,
        Sprite back,    Sprite backLeft,    Sprite left,  Sprite forwardLeft,
        Sprite idle)
    {
        Vector3 dir = worldDir;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f)
            return idle;

        if (relativeToCamera && cameraTransform != null)
        {
            Vector3 camF = cameraTransform.forward; camF.y = 0f; camF.Normalize();
            Vector3 camR = cameraTransform.right;   camR.y = 0f; camR.Normalize();
            float fwd   = Vector3.Dot(dir, camF);
            float right2 = Vector3.Dot(dir, camR);
            dir = new Vector3(right2, 0f, fwd);
        }

        if (dir.sqrMagnitude > 0.001f) dir.Normalize();

        float angle           = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleNormalized = (angle + 360f) % 360f;
        int   sector          = Mathf.RoundToInt(angleNormalized / 45f) % 8;

        Sprite chosen = sector switch
        {
            0 => forward,
            1 => forwardRight,
            2 => right,
            3 => backRight,
            4 => back,
            5 => backLeft,
            6 => left,
            7 => forwardLeft,
            _ => null
        };

        return chosen != null ? chosen : idle;
    }
}
