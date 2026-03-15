/// <summary>
/// Интерфейс менеджера квестов для диалоговой системы.
/// Реализуй в своём QuestManager.
/// </summary>
public interface IQuestManager
{
    bool IsActive(QuestDefinition quest);
    bool IsCompleted(QuestDefinition quest);
    bool IsFailed(QuestDefinition quest);

    void AcceptQuest(QuestDefinition quest);
    void CompleteQuest(QuestDefinition quest);
    void FailQuest(QuestDefinition quest);
    void CheckAll();
}
