using Gum.DataTypes;
using Gum.DataTypes.Variables;
using Gum.Managers;

namespace DofusSlice.Game.Ui;

/// <summary>
/// Generates the starter Gum UI project (ui/TitheHud.gumx + screens + standard elements) that
/// the visual Gum editor opens and the game loads at runtime. Run with:
///
///     dotnet run --project DofusSlice.Game -- --emit-gum
///
/// The output is committed, original content (layout + colours only, no third-party art), so
/// designers can tinker in the editor and the running game hot-reloads their saves. Regenerate
/// only to reset the layout — it OVERWRITES any hand-tuned edits.
/// </summary>
public static class GumProjectEmitter
{
    public const string ProjectRelativePath = "ui/TitheHud.gumx";

    public static void Emit(string uiDir, int screenW, int screenH, int hudTop)
    {
        Directory.CreateDirectory(uiDir);
        StandardElementsManager.Self.RefreshDefaults();

        var project = new GumProjectSave { DefaultCanvasWidth = screenW, DefaultCanvasHeight = screenH };
        StandardElementsManager.Self.PopulateProjectWithDefaultStandards(project);
        if (project.GetStandardElementSave("ColoredRectangle") == null)
            StandardElementsManager.Self.AddStandardElementSaveInstance(project, "ColoredRectangle");

        var screen = new ScreenSave { Name = "CombatHud" };
        var state = new StateSave { Name = "Default" };
        screen.States.Add(state);

        void Instance(string name, string baseType) =>
            screen.Instances.Add(new InstanceSave { Name = name, BaseType = baseType, ParentContainer = screen });
        void V(string variable, object value, string type) => state.SetValue(variable, value, type);
        void Rect(string name, float x, float y, float w, float h, byte r, byte g, byte b)
        {
            Instance(name, "ColoredRectangle");
            V($"{name}.X", x, "float"); V($"{name}.Y", y, "float");
            V($"{name}.Width", w, "float"); V($"{name}.Height", h, "float");
            V($"{name}.Red", (int)r, "int"); V($"{name}.Green", (int)g, "int"); V($"{name}.Blue", (int)b, "int");
        }
        void Text(string name, float x, float y, string text, byte r, byte g, byte b, int fontSize = 18)
        {
            Instance(name, "Text");
            V($"{name}.X", x, "float"); V($"{name}.Y", y, "float");
            V($"{name}.Width", 400f, "float"); V($"{name}.Height", 30f, "float");
            V($"{name}.Text", text, "string");
            V($"{name}.UseCustomFont", false, "bool");
            V($"{name}.FontSize", fontSize, "int");
            V($"{name}.Red", (int)r, "int"); V($"{name}.Green", (int)g, "int"); V($"{name}.Blue", (int)b, "int");
        }

        // The watched-combat bottom band: panel, headline, and four crew rows (name, HP text,
        // HP bar). The game binds these by NAME — rename them in the editor and the binding
        // falls back to the hand-drawn HUD, so keep the names while restyling freely.
        Rect("BottomPanel", 0, hudTop, screenW, screenH - hudTop, 13, 14, 18);
        Text("CrewLabel", 16, hudTop + 8, "YOUR CREW", 150, 150, 160, 14);
        Text("RoundText", 16, 8, "ROUND 1", 230, 230, 235);
        Text("TurnText", 16, 30, "WATCHING", 120, 200, 120);
        for (int i = 0; i < 4; i++)
        {
            int y = hudTop + 30 + i * 32;
            Text($"CrewName{i}", 16, y, $"CREW {i}", 225, 225, 230, 14);
            Rect($"CrewBarBack{i}", 180, y + 6, 150, 8, 40, 42, 48);
            Rect($"CrewBarFill{i}", 180, y + 6, 150, 8, 96, 200, 96);
            Text($"CrewHp{i}", 344, y, "0/0", 150, 150, 160, 14);
        }

        project.Screens.Add(screen);
        project.ScreenReferences.Add(new ElementReference { Name = screen.Name, ElementType = ElementType.Screen });

        string gumx = Path.Combine(uiDir, Path.GetFileName(ProjectRelativePath));
        project.Save(gumx, saveElements: true); // writes Screens/ and Standards/ beside the gumx
        Console.WriteLine($"Gum project written to {gumx}");
    }
}
