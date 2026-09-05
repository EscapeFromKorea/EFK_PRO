using UnityEngine;

// Existing Map3 HUD, restyled without replacing player controls or progression.
public sealed class ToyWorldDebugHUD : MonoBehaviour
{
    public ToyWorldLevelDirector director;
    public PuzzleResetManager resetManager;
    public bool showControls;
    private GUIStyle titleStyle, bodyStyle, smallStyle;
    private readonly Color ink = new Color(.08f, .16f, .19f, .94f);
    private readonly Color gold = new Color(.91f, .73f, .35f);
    private readonly Color ivory = new Color(.94f, .9f, .79f);

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) showControls = !showControls;
    }

    private void OnGUI()
    {
        if (director == null) director = ToyWorldLevelDirector.Instance;
        EnsureStyles();
        Matrix4x4 previous = GUI.matrix;
        float scale = Mathf.Clamp(Screen.width / 1440f, .65f, 1.3f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float height = Screen.height / scale, width = Screen.width / scale;
        Panel(new Rect(20, 42, 350, 160));
        GUI.Label(new Rect(36, 53, 318, 19), "THE TOYMAKER'S ATELIER", smallStyle);
        GUI.Label(new Rect(34, 73, 318, 35), "03 / TOYWORLD", titleStyle);
        string[] names = { "SPRING", "GEAR", "MELODY" };
        for (int i = 0; i < 3; i++)
        {
            bool got = director != null && director.HasItem((ToyWorldRepairItemType)i);
            Rect card = new Rect(36 + i * 106, 115, 98, 39);
            Paint(card, got ? new Color(.19f, .48f, .43f) : new Color(.16f, .23f, .26f));
            Paint(new Rect(card.x, card.y, 3, card.height), got ? gold : new Color(.35f, .4f, .4f));
            GUI.Label(new Rect(card.x + 9, card.y + 10, 88, 20), (got ? "+ " : "- ") + names[i], smallStyle);
        }
        GUI.Label(new Rect(36, 169, 320, 23), director == null ? "Find the three lost repair parts" :
            $"REPAIRED PARTS   {director.CollectedCount} / 3     |     F1  HELP", smallStyle);
        Panel(new Rect(20, height - 91, Mathf.Min(660, width - 40), 69));
        GUI.Label(new Rect(36, height - 82, 610, 16), "YOUR NEXT CHAPTER", smallStyle);
        GUI.Label(new Rect(36, height - 60, Mathf.Min(615, width - 70), 38), ObjectiveText(), bodyStyle);
        if (showControls)
        {
            Panel(new Rect(width - 366, 42, 346, 198));
            GUI.Label(new Rect(width - 349, 53, 315, 24), "PLAY WITH THE POSSIBILITIES", smallStyle);
            GUI.Label(new Rect(width - 349, 84, 315, 146),
                "WASD  Move     MOUSE  Look\nSPACE  Jump     TAB  Change shape\nF  Wire     E  Connect / detach blocks\nV  Sticker     Q  Change sticker\nR  Checkpoint     BACKSPACE  Hold to reset\nROLL ON / OFF gates affect cube and tetrahedron.", bodyStyle);
        }
        if (director != null && director.IsLevelCompleted)
        {
            Panel(new Rect(width * .5f - 240, height * .42f, 480, 125));
            GUI.Label(new Rect(width * .5f - 218, height * .42f + 20, 450, 36), "THE MUSIC PLAYS AGAIN", titleStyle);
            GUI.Label(new Rect(width * .5f - 218, height * .42f + 68, 440, 32), "TOYWORLD COMPLETE  /  All three parts restored", bodyStyle);
        }
        GUI.matrix = previous;
    }

    private string ObjectiveText()
    {
        if (director == null) return "Explore the toy box and find the plaza.";
        if (!director.AllItemsCollected) return "Explore the Fort, Clockwork Yard and Doll House — in any order.";
        if (!director.AllItemsInstalled) return "Return to the Music Box. Install Spring > Gear > Melody.";
        if (!director.IsMusicBoxActivated) return "Push the gold lever to bring the Music Box back to life.";
        if (!director.IsLevelCompleted) return "Reach the upper EXIT using the lift or your own route.";
        return "Every piece has found its place.";
    }

    private void Panel(Rect r)
    {
        Paint(r, ink); Paint(new Rect(r.x, r.y, 3, r.height), gold);
        Paint(new Rect(r.x + 3, r.y, r.width - 3, 1), new Color(.58f, .65f, .59f, .5f));
    }

    private static void Paint(Rect r, Color color)
    {
        Color previous = GUI.color; GUI.color = color;
        GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = ivory;
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        bodyStyle.normal.textColor = ivory;
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        smallStyle.normal.textColor = gold;
    }
}
