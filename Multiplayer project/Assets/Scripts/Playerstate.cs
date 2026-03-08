using UnityEngine;

[System.Serializable]
public class PlayerState
{
    public int playerId;
    public Color playerColor = Color.white;

    // Resources
    public int brick, lumber, wool, grain, ore;

    // Victory points
    public int victoryPoints;

    // Largest Army
    public int knightsPlayed;

    // Dev cards (playable)
    public int devKnight;
    public int devRoadBuilding;
    public int devYearOfPlenty;
    public int devMonopoly;
    public int devVictoryPoint;

    // Dev cards bought THIS turn (locked until next turn)
    public int newDevKnight;
    public int newDevRoadBuilding;
    public int newDevYearOfPlenty;
    public int newDevMonopoly;

    public void AddResource(ResourceType type, int amount)
    {
        switch (type)
        {
            case ResourceType.Brick: brick += amount; break;
            case ResourceType.Lumber: lumber += amount; break;
            case ResourceType.Wool: wool += amount; break;
            case ResourceType.Grain: grain += amount; break;
            case ResourceType.Ore: ore += amount; break;
            case ResourceType.Desert: break;
        }
    }

    public int TotalResources() => brick + lumber + wool + grain + ore;
}