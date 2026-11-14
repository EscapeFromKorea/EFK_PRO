using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Collections.Generic;

[CustomPropertyDrawer(typeof(LevelScene))]
public sealed class LevelSceneDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var path = property.FindPropertyRelative("scenePath");
        var current = AssetDatabase.LoadAssetAtPath<SceneAsset>(path.stringValue);
        EditorGUI.BeginChangeCheck();
        var selected = (SceneAsset)EditorGUI.ObjectField(position, label, current, typeof(SceneAsset), false);
        if (EditorGUI.EndChangeCheck()) path.stringValue = selected == null ? "" : AssetDatabase.GetAssetPath(selected);
    }
}

public sealed class LevelBuildScenes : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => Sync();
    [MenuItem("Tools/Level Integration/Sync Build Scenes")]
    public static void Sync()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LevelIntegration/Resources/LevelManager.prefab");
        var manager = prefab != null ? prefab.GetComponent<LevelManager>() : null;
        if (manager == null || manager.levels == null) throw new BuildFailedException("LevelManager configuration is missing.");
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var level in manager.levels)
        {
            if (level == null || string.IsNullOrEmpty(level.scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(level.scenePath) == null)
                throw new BuildFailedException("Every LevelManager stage must reference an existing scene.");
            int index = scenes.FindIndex(s => s.path == level.scenePath);
            if (index < 0) scenes.Add(new EditorBuildSettingsScene(level.scenePath, true));
            else scenes[index] = new EditorBuildSettingsScene(level.scenePath, true);
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
