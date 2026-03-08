using UnityEngine;

/// <summary>
/// Вешается на один GameObject в сцене (например, GameManager).
/// Отвечает за загрузку сохранения при старте и сохранение при выходе.
///
/// allQuests — перетащи сюда ВСЕ QuestDefinition из проекта,
/// чтобы SaveSystem мог восстановить квесты по ID.
/// </summary>
public class GameLoader : MonoBehaviour
{
    [Tooltip("Все QuestDefinition в проекте — нужны для загрузки сохранения")]
    public QuestDefinition[] allQuests;

    [Tooltip("Автосохранение при закрытии игры")]
    public bool autoSaveOnQuit = true;

    private void Start()
    {
        if (SaveSystem.HasSave())
            SaveSystem.Load(allQuests);
    }

    private void OnApplicationQuit()
    {
        if (autoSaveOnQuit)
            SaveSystem.Save();
    }

    // Можно вызвать из UI кнопки "Сохранить"
    public void SaveGame()  => SaveSystem.Save();

    // Можно вызвать из UI кнопки "Новая игра"
    public void NewGame()   => SaveSystem.Delete();
}
