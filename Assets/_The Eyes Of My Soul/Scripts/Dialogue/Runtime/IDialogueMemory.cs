/// <summary>
/// Интерфейс хранилища диалоговой памяти.
/// Реализуй в своём DialogueMemory, чтобы его можно было подменить в тестах.
/// </summary>
public interface IDialogueMemory
{
    bool GetFlag(string key);
    void SetFlag(string key, bool value);

    int  GetInt(string key);
    void SetInt(string key, int value);
}
