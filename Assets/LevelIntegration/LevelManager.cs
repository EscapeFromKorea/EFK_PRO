using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class LevelScene
{
    [HideInInspector] public string scenePath;
}

public sealed class LevelManager : MonoBehaviour
{
    public LevelScene[] levels;
    public static LevelManager Instance { get; private set; }
    private bool cleared;
    public bool IsCleared => cleared;
    private float previousTimeScale;
    private bool previousInputBlocked;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var prefab = Resources.Load<GameObject>("LevelManager");
        if (prefab == null) { Debug.LogError("LevelManager prefab is missing from Resources."); return; }
        Instantiate(prefab).name = "LevelManager";
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (cleared) RestoreInput();
        Instance = null;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.path == "Assets/Scenes/Map4_Laboratory/Scenes/Sectors/Map4_S8.unity" &&
            SceneManager.GetActiveScene().path == "Assets/Scenes/Map4_Laboratory/Scenes/Map4_Master.unity")
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "S8_FinalExit_Checkpoint")
                    {
                        var checkpointCollider = child.GetComponent<Collider>();
                        if (checkpointCollider == null || !checkpointCollider.isTrigger)
                            Debug.LogError("Map 4 final checkpoint requires a trigger collider.", child);
                        else if (child.GetComponent<LevelGoal>() == null)
                            child.gameObject.AddComponent<LevelGoal>();
                        return;
                    }
            Debug.LogError("Map 4 final checkpoint was not found; the goal could not be created.");
            return;
        }
        if (scene.path != "Assets/Scenes/Map1_DayDream/Map1_DayDream.unity") return;
        var summit = GameObject.Find("P12_Summit_Collision");
        var crown = GameObject.Find("P12_Summit_Crown");
        if (summit == null) { Debug.LogError("Map 1 summit was not found; the goal could not be created."); return; }
        if (crown == null) { Debug.LogError("Map 1 summit crown was not found; the goal could not be created."); return; }
        var collider = summit.GetComponent<Collider>();
        if (collider == null) { Debug.LogError("Map 1 summit has no collider."); return; }
        Bounds bounds = collider.bounds;
        var goal = new GameObject("LevelGoal_Map1");
        goal.transform.position = new Vector3(crown.transform.position.x, bounds.max.y + 2f, crown.transform.position.z);
        goal.transform.rotation = crown.transform.rotation;
        var trigger = goal.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(6f, 4f, 1f);
        goal.AddComponent<LevelGoal>();
    }
    private int CurrentIndex()
    {
        string path = SceneManager.GetActiveScene().path;
        if (levels == null) return -1;
        for (int i = 0; i < levels.Length; i++)
            if (levels[i] != null && levels[i].scenePath == path) return i;
        return -1;
    }
    public void CompleteLevel()
    {
        if (cleared) return;
        if (CurrentIndex() < 0) { Debug.LogError("The current scene is not in LevelManager's level list."); return; }
        cleared = true;
        previousTimeScale = Time.timeScale;
        previousInputBlocked = InputGate.Blocked;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Time.timeScale = 0;
        InputGate.Blocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    private void RestoreInput()
    {
        Time.timeScale = previousTimeScale;
        InputGate.Blocked = previousInputBlocked;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        cleared = false;
    }
    private void OnGUI()
    {
        if (!cleared) return;
        int current = CurrentIndex();
        bool hasNext = current >= 0 && current + 1 < levels.Length;
        float width = Mathf.Min(500, Screen.width - 40);
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2, (Screen.height - 220) / 2, width, 220), GUI.skin.box);
        GUILayout.FlexibleSpace();
        var title = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter };
        GUILayout.Label("STAGE CLEAR", title, GUILayout.Height(55));
        if (GUILayout.Button("Home", GUILayout.Height(48)))
        {
            RestoreInput();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        GUI.enabled = hasNext;
        if (GUILayout.Button(hasNext ? "Next Stage" : "Last Stage Clear", GUILayout.Height(48)))
        {
            string path = levels[current + 1].scenePath;
            if (string.IsNullOrEmpty(path) || !Application.CanStreamedLevelBeLoaded(path))
                Debug.LogError("Next stage is not enabled in Build Settings: " + path);
            else { RestoreInput(); SceneManager.LoadScene(path); }
        }
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }
}
