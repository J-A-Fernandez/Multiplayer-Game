using UnityEngine;

public class HexTile : MonoBehaviour
{
    public AxialCoord coord;
    public ResourceType resource; // Lumber, Brick, Stone,Lamb,Grain
    public int number; //Tile numbers for dice roll
    public bool hasRobber; //Robber on tile

    public Intersection[] corners = new Intersection[6];

    public bool Production(int dice) =>
        !hasRobber && resource != ResourceType.Desert && number == dice;
}
