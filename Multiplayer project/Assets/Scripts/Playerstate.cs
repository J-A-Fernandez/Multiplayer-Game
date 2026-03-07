using UnityEngine;

[System.Serializable]
public class Playerstate
{
    public int playerId;
    public Color playerColor = Color.white;

    public int brick, lumber, wool, grain, ore;

    public int victoryPoints;

    public void AddResource(ResourceType type , int amount)
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

}
