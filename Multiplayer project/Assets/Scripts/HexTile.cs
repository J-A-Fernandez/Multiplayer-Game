using UnityEngine;
//using TMPro;

public class HexTile : MonoBehaviour
{
    public AxialCoord coord;
    public ResourceType resource; // Lumber, Brick, Stone,Lamb,Grain
    public int number; //Tile numbers for dice roll
    public bool hasRobber; //Robber on tile

    public Intersection[] corners = new Intersection[6];

    [Header("Visuals (assign in prefab)")]
    [SerializeField] private SpriteRenderer fillRenderer;

    private void Awake()
    {
        if (fillRenderer == null)
        {
            var fill = transform.Find("Fill");
            if (fill != null) fillRenderer = fill.GetComponent<SpriteRenderer>();
        }
    }
    public bool Production(int dice) =>
        !hasRobber && resource != ResourceType.Desert && number == dice;

    public void RefreshVisual()
    {
        if (fillRenderer != null)
            fillRenderer.color = ResourceColor(resource);
        Debug.Log($"{coord} resource={resource} fillNull={(fillRenderer == null)}");
    }

    private Color ResourceColor(ResourceType r)
    {
        return r switch
        {
            ResourceType.Brick => new Color(0.75f, 0.25f, 0.20f),
            ResourceType.Lumber => new Color(0.20f, 0.55f, 0.25f),
            ResourceType.Wool => new Color(0.60f, 0.90f, 0.60f),
            ResourceType.Grain => new Color(0.95f, 0.85f, 0.30f),
            ResourceType.Ore => new Color(0.55f, 0.55f, 0.65f),
            ResourceType.Desert => new Color(0.90f, 0.80f, 0.55f),
            _ => Color.white
        };
    }

}
