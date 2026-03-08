using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Сохранение и загрузка состояния игры в JSON.
/// Сохраняет: флаги DialogueMemory, int-значения, активные/завершённые/проваленные квесты.
/// Репутации NPC — НЕ сохраняются (они на сцене, добавишь позже когда понадобится).
///
/// Использование:
///   SaveSystem.Save()   — сохранить
///   SaveSystem.Load()   — загрузить (вызывай при старте сцены)
///   SaveSystem.Delete() — удалить сохранение (новая игра)
///
/// Файл: Application.persistentDataPath/save.json
/// </summary>
public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

    // ── Публичный API ────────────────────────────────────────────────────────

    public static void Save()
    {
        var data = new SaveData();

        // DialogueMemory
        var memory = DialogueMemory.Instance;
        data.flags = memory.GetAllFlags();
        data.ints  = memory.GetAllInts();

        // QuestManager
        var qm = QuestManager.Instance;
        if (qm != null)
        {
            foreach (var q in qm.ActiveQuests)
                if (!string.IsNullOrEmpty(q.questId))
                    data.activeQuestIds.Add(q.questId);

            foreach (var q in qm.CompletedQuests)
                if (!string.IsNullOrEmpty(q.questId))
                    data.completedQuestIds.Add(q.questId);

            foreach (var q in qm.FailedQuests)
                if (!string.IsNullOrEmpty(q.questId))
                    data.failedQuestIds.Add(q.questId);
        }

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"[SaveSystem] Сохранено → {SavePath}");
    }

    public static bool Load(QuestDefinition[] allQuests)
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[SaveSystem] Файл сохранения не найден, начинаем заново.");
            return false;
        }

        try
        {
            string json = File.ReadAllText(SavePath);
            var data = JsonUtility.FromJson<SaveData>(json);

            // DialogueMemory
            var memory = DialogueMemory.Instance;
            memory.Reset();
            foreach (var kv in data.flags) memory.SetFlag(kv.key, kv.value);
            foreach (var kv in data.ints)  memory.SetInt(kv.key, kv.value);

            // QuestManager
            var qm = QuestManager.Instance;
            if (qm != null && allQuests != null)
            {
                qm.ResetForLoad();

                // Построить словарь для быстрого поиска
                var dict = new Dictionary<string, QuestDefinition>();
                foreach (var q in allQuests)
                    if (!string.IsNullOrEmpty(q.questId))
                        dict[q.questId] = q;

                foreach (var id in data.activeQuestIds)
                    if (dict.TryGetValue(id, out var q)) qm.AcceptQuest(q);

                foreach (var id in data.completedQuestIds)
                    if (dict.TryGetValue(id, out var q)) qm.ForceComplete(q);

                foreach (var id in data.failedQuestIds)
                    if (dict.TryGetValue(id, out var q)) qm.ForceFail(q);
            }

            Debug.Log("[SaveSystem] Загружено успешно.");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Ошибка загрузки: {e.Message}");
            return false;
        }
    }

    public static void Delete()
    {
        if (File.Exists(SavePath))
            File.Delete(SavePath);

        DialogueMemory.Instance.Reset();
        QuestManager.Instance?.ResetForLoad();
        Debug.Log("[SaveSystem] Сохранение удалено.");
    }

    public static bool HasSave() => File.Exists(SavePath);
}

// ── Структуры данных ─────────────────────────────────────────────────────────

[Serializable]
public class SaveData
{
    public List<StringBoolPair> flags         = new List<StringBoolPair>();
    public List<StringIntPair>  ints          = new List<StringIntPair>();
    public List<string> activeQuestIds        = new List<string>();
    public List<string> completedQuestIds     = new List<string>();
    public List<string> failedQuestIds        = new List<string>();
}

[Serializable] public class StringBoolPair { public string key; public bool value; }
[Serializable] public class StringIntPair  { public string key; public int  value; }
