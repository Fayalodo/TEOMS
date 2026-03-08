using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальная память диалогов. Хранит флаги и int-значения между сценами.
/// Синглтон, создаётся автоматически при первом обращении.
/// </summary>
public class DialogueMemory : MonoBehaviour
{
    private static DialogueMemory _instance;
    public static DialogueMemory Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("DialogueMemory");
                _instance = go.AddComponent<DialogueMemory>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private Dictionary<string, bool> _flags = new Dictionary<string, bool>();
    private Dictionary<string, int> _ints = new Dictionary<string, int>();

    // ── Флаги ──────────────────────────────────────────────────────────────

    public void SetFlag(string key, bool value)
    {
        _flags[key] = value;
        Debug.Log($"[DialogueMemory] Flag '{key}' = {value}");
    }

    public bool GetFlag(string key)
    {
        return _flags.TryGetValue(key, out bool val) && val;
    }

    // ── Int-значения ────────────────────────────────────────────────────────

    public void SetInt(string key, int value)
    {
        _ints[key] = value;
        Debug.Log($"[DialogueMemory] Int '{key}' = {value}");
    }

    public int GetInt(string key)
    {
        return _ints.TryGetValue(key, out int val) ? val : 0;
    }

    // ── Утилиты ─────────────────────────────────────────────────────────────

    /// <summary>Полностью сбросить всю память (начало новой игры).</summary>
    public void Reset()
    {
        _flags.Clear();
        _ints.Clear();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
