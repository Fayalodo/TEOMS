using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальная память диалогов. Хранит флаги и int-значения между сценами.
/// Синглтон, создаётся автоматически при первом обращении.
/// + GetAllFlags / GetAllInts для SaveSystem.
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

    // ── Флаги ────────────────────────────────────────────────────────────────

    public void SetFlag(string key, bool value)
    {
        _flags[key] = value;
        Debug.Log($"[DialogueMemory] Flag '{key}' = {value}");
    }

    public bool GetFlag(string key)
        => _flags.TryGetValue(key, out bool val) && val;

    // ── Int-значения ─────────────────────────────────────────────────────────

    public void SetInt(string key, int value)
    {
        _ints[key] = value;
        Debug.Log($"[DialogueMemory] Int '{key}' = {value}");
    }

    public int GetInt(string key)
        => _ints.TryGetValue(key, out int val) ? val : 0;

    // ── Для SaveSystem ───────────────────────────────────────────────────────

    public List<StringBoolPair> GetAllFlags()
    {
        var list = new List<StringBoolPair>();
        foreach (var kv in _flags)
            list.Add(new StringBoolPair { key = kv.Key, value = kv.Value });
        return list;
    }

    public List<StringIntPair> GetAllInts()
    {
        var list = new List<StringIntPair>();
        foreach (var kv in _ints)
            list.Add(new StringIntPair { key = kv.Key, value = kv.Value });
        return list;
    }

    // ── Утилиты ──────────────────────────────────────────────────────────────

    public void Reset()
    {
        _flags.Clear();
        _ints.Clear();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}