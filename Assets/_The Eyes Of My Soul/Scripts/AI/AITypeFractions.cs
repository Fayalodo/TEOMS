using UnityEngine;

// Тип ИИ
public enum AIType
{
    Animal,
    Monster,
    NeutralNPC,
    AggressiveNPC
}

// Состояния ИИ
public enum AIState
{
    Idle,
    Wander,
    Patrol,
    Observe,
    Attack,
    Flee,
    FollowingSchedule
}

// Фракции
public enum Faction
{
    Player,
    Friendly,
    Neutral,
    Hostile
}
