using UnityEngine;

[System.Serializable]
public class Building
{
    public int ownerId;
    public BuildingType type;

    public int payout => type == BuildingType.City ? 2 : 1;

    public Building(int ownerId, BuildingType type)
    {
        this.ownerId = ownerId;
        this.type = type;
    }
}
