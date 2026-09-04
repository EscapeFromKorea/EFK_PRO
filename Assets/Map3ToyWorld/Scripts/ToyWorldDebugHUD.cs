using UnityEngine;

public sealed class ToyWorldDebugHUD : MonoBehaviour
{
    public ToyWorldLevelDirector director;
    public PuzzleResetManager resetManager;

    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;

    private void OnGUI()
    {
        if (director == null) director = ToyWorldLevelDirector.Instance;
        EnsureStyles();

        GUI.Box(new Rect(10f, 38f, 470f, 170f), GUIContent.none);
        GUI.Label(new Rect(22f, 46f, 440f, 24f), "MAP 3 - TOY WORLD / FUNCTIONAL GRAYBOX", titleStyle);

        string progress = director == null
            ? "Progress unavailable"
            : $"Repair parts: {director.CollectedCount}/3   Installed: {director.InstalledCount}/3   " +
              $"Music Box: {(director.IsMusicBoxActivated ? "ACTIVE" : "OFF")}";
        GUI.Label(new Rect(22f, 72f, 440f, 22f), progress, bodyStyle);
        GUI.Label(new Rect(22f, 96f, 440f, 22f), "WASD Move | Space Jump | Tab Shape | F Wire | E Snap blocks", bodyStyle);
        GUI.Label(new Rect(22f, 118f, 440f, 22f), "V Sticker | Q Sticker type | R Respawn | Hold Backspace Reset puzzles", bodyStyle);
        GUI.Label(new Rect(22f, 140f, 440f, 22f), "Green portal enables existing roll mode. Red portal disables it.", bodyStyle);

        string objective = ObjectiveText();
        GUI.Label(new Rect(22f, 164f, 440f, 38f), objective, bodyStyle);
    }

    private string ObjectiveText()
    {
        if (director == null) return string.Empty;
        if (!director.AllItemsCollected) return "Objective: clear Block Fort, Train Yard, and Doll House in any order.";
        if (!director.AllItemsInstalled) return "Objective: install Spring > Gear > Cylinder inside the Music Box.";
        if (!director.IsMusicBoxActivated) return "Objective: push the existing DoorSystem lever to activate the Music Box.";
        if (!director.IsLevelCompleted) return "Objective: use the lift or a bypass route to reach the green EXIT.";
        return "MAP 3 COMPLETE";
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
    }
}
