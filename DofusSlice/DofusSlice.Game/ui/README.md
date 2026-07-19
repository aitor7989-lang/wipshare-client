# The Gum UI project (tinker here)

`TitheHud.gumx` is a [Gum](https://github.com/vchelaru/Gum) project: open it in the visual
Gum editor (grab a release from https://github.com/vchelaru/Gum/releases, Windows) and move,
resize, recolor and restyle the game's UI with the mouse. **The running game hot-reloads
every save** — keep the game open next to the editor and your changes appear in seconds.

- The game binds data BY ELEMENT NAME (`RoundText`, `TurnText`, `CrewName0..3`,
  `CrewBarFill0..3`, `CrewHp0..3`, `BottomPanel`…). Restyle and rearrange freely, but keep
  the names, or the game quietly falls back to its hand-drawn HUD for the missing pieces.
- These files are generated originals (layout + colours only) and are committed. To reset to
  the stock layout: `dotnet run --project DofusSlice.Game -- --emit-gum` (OVERWRITES edits).
- More surfaces (trade panels, placement, equip) can be ported the same way — ask for it.
