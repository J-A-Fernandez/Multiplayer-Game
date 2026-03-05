using System;
using UnityEngine;

[Serializable]
public struct AxialCoord : IEquatable<AxialCoord>
{
    public int q; // column
    public int r; // row

    public AxialCoord(int q, int r)
    {
        this.q = q;
        this.r = r;
    }

    public int s => -q - r;

    public Vector2 ToWorld(float hexSize)
    {
        // Pointy-top hex layout
        float x = hexSize * Mathf.Sqrt(3f) * (q + r * 0.5f);
        float y = hexSize * 1.5f * r;
        return new Vector2(x, y);
    }

    public int Distance(AxialCoord other)
    {
        int dq = Mathf.Abs(q - other.q);
        int dr = Mathf.Abs(r - other.r);
        int ds = Mathf.Abs(s - other.s);
        return (dq + dr + ds) / 2;
    }

    public static readonly AxialCoord[] Directions =
    {
        new AxialCoord(+1,  0),
        new AxialCoord(+1, -1),
        new AxialCoord( 0, -1),
        new AxialCoord(-1,  0),
        new AxialCoord(-1, +1),
        new AxialCoord( 0, +1),
    };

    public AxialCoord Neighbor(int dir)
    {
        var d = Directions[dir];
        return new AxialCoord(q + d.q, r + d.r);
    }

    public bool Equals(AxialCoord other) => q == other.q && r == other.r;
    public override bool Equals(object obj) => obj is AxialCoord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(q, r);
    public override string ToString() => $"({q},{r})";
}