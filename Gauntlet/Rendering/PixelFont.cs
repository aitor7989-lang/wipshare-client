using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Gauntlet.Rendering;

/// <summary>
/// A tiny 5x7 bitmap font rendered from a single white pixel — no content pipeline, no
/// font files, so the slice is fully self-contained. Glyphs are authored as ASCII art
/// ('#' = lit) which keeps them readable and reviewable straight from source.
/// </summary>
public sealed class PixelFont
{
    private const int GlyphW = 5;
    private const int GlyphH = 7;
    private readonly Texture2D _pixel;

    public PixelFont(Texture2D pixel) => _pixel = pixel;

    public int LineHeight(int scale) => GlyphH * scale;

    public int Measure(string text, int scale) =>
        text.Length == 0 ? 0 : text.Length * (GlyphW + 1) * scale - scale;

    public void Draw(SpriteBatch sb, string text, int x, int y, int scale, Color color)
    {
        int cx = x;
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            if (Glyphs.TryGetValue(c, out var rows))
            {
                for (int row = 0; row < GlyphH; row++)
                {
                    string line = rows[row];
                    for (int col = 0; col < GlyphW; col++)
                    {
                        if (col < line.Length && line[col] == '#')
                            sb.Draw(_pixel, new Rectangle(cx + col * scale, y + row * scale, scale, scale), color);
                    }
                }
            }
            cx += (GlyphW + 1) * scale;
        }
    }

    /// <summary>Draw right-aligned so the text ends at <paramref name="rightX"/>.</summary>
    public void DrawRight(SpriteBatch sb, string text, int rightX, int y, int scale, Color color) =>
        Draw(sb, text, rightX - Measure(text, scale), y, scale, color);

    public void DrawCentered(SpriteBatch sb, string text, int centerX, int y, int scale, Color color) =>
        Draw(sb, text, centerX - Measure(text, scale) / 2, y, scale, color);

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        [' '] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "     " },
        ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
        ['C'] = new[] { " ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####" },
        ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
        ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
        ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
        ['G'] = new[] { " ####", "#    ", "#    ", "#  ##", "#   #", "#   #", " ### " },
        ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['I'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####" },
        ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "#  # ", "#  # ", " ##  " },
        ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
        ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
        ['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
        ['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
        ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
        ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
        ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
        ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
        ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
        ['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #" },
        ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
        ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
        ['6'] = new[] { " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
        [':'] = new[] { "     ", "  #  ", "  #  ", "     ", "  #  ", "  #  ", "     " },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", "  ## ", "  ## " },
        [','] = new[] { "     ", "     ", "     ", "     ", "  ## ", "  ## ", " #   " },
        ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
        ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
        ['/'] = new[] { "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    " },
        ['!'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  " },
        ['?'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  " },
        ['('] = new[] { "   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # " },
        [')'] = new[] { " #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   " },
        ['%'] = new[] { "#   #", "#  # ", "  #  ", " #   ", "  #  ", " #  #", "#   #" },
        ['\''] = new[] { "  #  ", "  #  ", " #   ", "     ", "     ", "     ", "     " },
    };
}
