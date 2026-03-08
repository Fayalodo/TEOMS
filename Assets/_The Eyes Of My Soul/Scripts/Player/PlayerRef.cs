using UnityEngine;

/// <summary>
/// Кеш ссылок на компоненты игрока.
/// Вешается на GameObject игрока — больше никаких FindGameObjectWithTag("Player").
/// </summary>
public class PlayerRef : MonoBehaviour
{
    public static PlayerRef Instance { get; private set; }

    public Inventory Inventory { get; private set; }
    public PlayerMovement Movement { get; private set; }
    public Health Health { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        Inventory = GetComponent<Inventory>();
        Movement  = GetComponent<PlayerMovement>();
        Health    = GetComponent<Health>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
