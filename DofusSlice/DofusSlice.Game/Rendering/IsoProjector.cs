using DofusSlice.Core.Grid;
using Microsoft.Xna.Framework;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// Converts between logical grid cells and isometric screen pixels — the "2.5D" look of
/// Dofus. A cell's screen position is the classic iso transform of its (x, y); the inverse
/// lets the mouse pick a cell.
/// </summary>
public sealed class IsoProjector
{
    private readonly float _hw; // half tile width
    private readonly float _hh; // half tile height
    private readonly Vector2 _origin;
    private readonly bool _topDown; // square-grid pixel mode (the 8-bit tileset look)
    private readonly float _cell;   // cell size in top-down mode

    public IsoProjector(int tileW, int tileH, Vector2 origin)
    {
        _hw = tileW / 2f;
        _hh = tileH / 2f;
        _origin = origin;
    }

    private IsoProjector(float cell, Vector2 origin)
    {
        _topDown = true;
        _cell = cell;
        _origin = origin;
    }

    public Vector2 CellCenter(CellCoord c) => _topDown
        ? new(_origin.X + c.X * _cell + _cell / 2f, _origin.Y + c.Y * _cell + _cell / 2f)
        : new(_origin.X + (c.X - c.Y) * _hw, _origin.Y + (c.X + c.Y) * _hh);

    public Vector2 CellCenter(int x, int y) => CellCenter(new CellCoord(x, y));

    public CellCoord ScreenToCell(Vector2 screen)
    {
        if (_topDown)
            return new CellCoord(
                (int)MathF.Floor((screen.X - _origin.X) / _cell),
                (int)MathF.Floor((screen.Y - _origin.Y) / _cell));
        float a = (screen.X - _origin.X) / _hw;
        float b = (screen.Y - _origin.Y) / _hh;
        int x = (int)MathF.Round((a + b) / 2f);
        int y = (int)MathF.Round((b - a) / 2f);
        return new CellCoord(x, y);
    }

    /// <summary>Centre the map's bounding diamond around <paramref name="screenCenter"/>.</summary>
    public static IsoProjector Centered(int width, int height, int tileW, int tileH, Vector2 screenCenter)
    {
        float hw = tileW / 2f, hh = tileH / 2f;
        // Midpoint of the (x-y) and (x+y) extents, converted to pixels.
        float midXminus = ((width - 1) - (height - 1)) / 2f;
        float midXplus = ((width - 1) + (height - 1)) / 2f;
        var origin = new Vector2(
            screenCenter.X - midXminus * hw,
            screenCenter.Y - midXplus * hh);
        return new IsoProjector(tileW, tileH, origin);
    }

    /// <summary>Centre a square top-down grid around <paramref name="screenCenter"/> (tileset mode).</summary>
    public static IsoProjector TopDownCentered(int width, int height, int cell, Vector2 screenCenter)
    {
        var origin = new Vector2(
            screenCenter.X - width * cell / 2f,
            screenCenter.Y - height * cell / 2f);
        return new IsoProjector(cell, origin);
    }
}
