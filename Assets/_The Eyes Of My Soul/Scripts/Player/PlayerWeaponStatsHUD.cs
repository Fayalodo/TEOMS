using UnityEngine;

/// <summary>
/// Надпись в правом верхнем углу экрана с текущими боевыми статами игрока —
/// оружие, урон, дальность удара, толщина капсулы (радиус) и скорость атаки.
/// Читает значения напрямую из PlayerCombat, так что при смене оружия
/// (UpdateWeaponStats) обновляется сама, без дополнительных событий.
///
/// Сделано через OnGUI — тем же способом, что и прицел в PlayerCombat,
/// чтобы не тащить в проект Canvas/TMP только ради одной надписи.
/// </summary>
[RequireComponent(typeof(PlayerCombat))]
public class PlayerWeaponStatsHUD : MonoBehaviour
{
    [Header("Показ")]
    public bool visible = true;
    [Tooltip("Клавиша для показать/скрыть панель. None — если переключение не нужно.")]
    public KeyCode toggleKey = KeyCode.F3;

    [Header("Расположение и вид")]
    public float marginRight = 16f;
    public float marginTop = 16f;
    public float panelWidth = 220f;
    public int fontSize = 14;
    public Color textColor = new Color(1f, 1f, 1f, 0.95f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.45f);

    private PlayerCombat combat;
    private GUIStyle labelStyle;
    private GUIStyle headerStyle;
    private Texture2D backgroundTex;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    void EnsureStyles()
    {
        if (backgroundTex == null)
        {
            backgroundTex = new Texture2D(1, 1);
            backgroundTex.SetPixel(0, 0, Color.white);
            backgroundTex.Apply();
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = textColor }
            };
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(labelStyle)
            {
                fontStyle = FontStyle.Bold
            };
        }
    }

    void OnGUI()
    {
        if (!visible || combat == null) return;
        if (PlayerCamera.Instance != null && PlayerCamera.Instance.InputBlocked) return; // не поверх инвентаря/диалога

        EnsureStyles();

        string weaponName = combat.CurrentWeapon != null
            ? combat.CurrentWeapon.displayName
            : "Кулаки";

        // Скорость атаки — удобнее читать как "ударов/сек", чем сырой кулдаун.
        float attacksPerSecond = combat.attackCooldown > 0f ? 1f / combat.attackCooldown : 0f;

        float lineHeight = fontSize + 6f;
        int lineCount = 6; // заголовок + 5 строк статов
        float panelHeight = lineHeight * lineCount + 12f;

        float x = Screen.width - panelWidth - marginRight;
        float y = marginTop;

        Color prevColor = GUI.color;
        GUI.color = backgroundColor;
        GUI.DrawTexture(new Rect(x, y, panelWidth, panelHeight), backgroundTex);
        GUI.color = prevColor;

        float padding = 8f;
        float lx = x + padding;
        float ly = y + padding;
        float lw = panelWidth - padding * 2f;

        string rangeLine = $"Дальность: {combat.attackRange:0.##} м";
        if (combat.IsAimingAtLiveTarget)
            rangeLine += $" ({combat.CurrentTargetDistance:0.##} м)";

        GUI.Label(new Rect(lx, ly, lw, lineHeight), weaponName, headerStyle); ly += lineHeight;
        GUI.Label(new Rect(lx, ly, lw, lineHeight), $"Урон: {combat.attackDamage:0.#}", labelStyle); ly += lineHeight;
        GUI.Label(new Rect(lx, ly, lw, lineHeight), rangeLine, labelStyle); ly += lineHeight;
        GUI.Label(new Rect(lx, ly, lw, lineHeight), $"Толщина удара: {combat.attackRadius:0.##} м", labelStyle); ly += lineHeight;
        GUI.Label(new Rect(lx, ly, lw, lineHeight), $"Кулдаун: {combat.attackCooldown:0.##} с", labelStyle); ly += lineHeight;
        GUI.Label(new Rect(lx, ly, lw, lineHeight), $"Скорость: {attacksPerSecond:0.##} уд/с", labelStyle);
    }

    void OnDestroy()
    {
        if (backgroundTex != null) Destroy(backgroundTex);
    }
}
