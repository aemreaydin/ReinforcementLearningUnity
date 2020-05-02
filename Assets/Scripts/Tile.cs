using UnityEngine;


public enum TileState
{
    Empty,
    Destination,
    Start,
    Death
}

public class Tile : MonoBehaviour
{
    public TileState state = TileState.Empty;
}
