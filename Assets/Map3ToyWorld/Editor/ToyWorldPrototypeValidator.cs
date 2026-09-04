using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ToyWorldPrototypeValidator
{
    [MenuItem("Tools/The Axiom/Validate Map3 ToyWorld Prototype")]
    public static void ValidateMenu()
    {
        int errors = ValidateScene(true);
        if (errors == 0) Debug.Log("[ToyWorldValidator] PASS - scene, references, colliders, and progression rules are valid.");
        else Debug.LogError($"[ToyWorldValidator] FAIL - {errors} error(s). See messages above.");
    }

    public static int ValidateScene(bool openScene)
    {
        if (openScene)
            EditorSceneManager.OpenScene(ToyWorldPrototypeBuilder.ScenePath, OpenSceneMode.Single);

        int errors = 0;
        GameObject root = GameObject.Find("Map3_ToyWorld_Root");
        Require(root != null, "Map3_ToyWorld_Root is missing.", ref errors);
        if (root == null) return errors;

        string[] requiredAreas =
        {
            "ToyBox_Entrance", "ToyPlaza_Hub", "Branch_BlockFort", "Branch_TrainYard",
            "Branch_DollHouse", "Final_BrokenMusicBox"
        };
        foreach (string areaName in requiredAreas)
            Require(FindChildByName(root.transform, areaName) != null, areaName + " is missing.", ref errors);

        ToyWorldLevelDirector[] directors = UnityEngine.Object.FindObjectsOfType<ToyWorldLevelDirector>(true);
        Require(directors.Length == 1, "Exactly one ToyWorldLevelDirector is required.", ref errors);
        ToyWorldLevelDirector director = directors.Length == 1 ? directors[0] : null;
        if (director != null)
        {
            Require(director.finalGate != null, "Director.finalGate is not wired.", ref errors);
            Require(director.musicBox != null, "Director.musicBox is not wired.", ref errors);
            Require(director.hubProgress != null, "Director.hubProgress is not wired.", ref errors);
        }

        PlayerMover[] players = UnityEngine.Object.FindObjectsOfType<PlayerMover>(true);
        TorqueStateController[] torqueStates = UnityEngine.Object.FindObjectsOfType<TorqueStateController>(true);
        Require(players.Length == 3, "The scene must contain all three existing PlayerSystem shapes.", ref errors);
        Require(torqueStates.Length == 3, "Each player must have a TorqueStateController adapter.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<RespawnController>(true).Length == 1,
            "Exactly one existing RespawnController is required.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<RespawnZone>(true).Length >= 7,
            "Start, hub, branch, floor, and final checkpoints are required.", ref errors);

        ToyWorldRepairItem[] items = UnityEngine.Object.FindObjectsOfType<ToyWorldRepairItem>(true);
        Require(items.Length == 3, "Exactly three repair items are required.", ref errors);
        bool[] seenItems = new bool[3];
        foreach (ToyWorldRepairItem item in items)
        {
            int index = (int)item.itemType;
            Require(index >= 0 && index < 3 && !seenItems[index], "Repair item types must be unique.", ref errors);
            if (index >= 0 && index < 3) seenItems[index] = true;
            Require(item.director != null, item.name + " has no director reference.", ref errors);
            Require(item.returnShortcut != null, item.name + " has no return shortcut reference.", ref errors);
        }

        ToyWorldInstallSocket[] sockets = UnityEngine.Object.FindObjectsOfType<ToyWorldInstallSocket>(true);
        Require(sockets.Length == 3, "Exactly three install sockets are required.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<ToyWorldExitTrigger>(true).Length == 1,
            "Exactly one final exit trigger is required.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<WindUpAxis>(true).Length >= 3,
            "Toy Box, Train Yard, and final room each need a WindUpAxis.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<ToyRailCart>(true).Length == 1,
            "Train Yard requires one physical rail cart.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<ToyRailSwitch>(true).Length == 1 &&
                UnityEngine.Object.FindObjectsOfType<ToyRailSwitchPad>(true).Length == 1,
            "Train Yard requires a functional branch switch and pad.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<RotatingPlate>(true).Length >= 5,
            "Seesaws and free rotating boards were not all generated.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<SnapBlock>(true).Length >= 16,
            "Reusable SnapBlocks are missing.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<StickerSurface>(true).Length >= 5,
            "StickerSurface coverage is incomplete.", ref errors);
        Require(UnityEngine.Object.FindObjectsOfType<ThreadAnchor>(true).Length >= 6,
            "Wire bypass anchors are incomplete.", ref errors);

        ValidateColliderAndVisualSeparation(root.transform, ref errors);
        ValidateMissingScriptsAndReferences(root.transform, ref errors);
        ValidateProgressionModel(ref errors);

        if (errors == 0)
            Debug.Log("[ToyWorldValidator] Structural validation passed, including all six branch orders and final gating.");
        return errors;
    }

    private static void ValidateColliderAndVisualSeparation(Transform root, ref int errors)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObject go = transform.gameObject;
            bool gameplayGeometry = go.name.StartsWith("GEO_", StringComparison.Ordinal) ||
                                    go.name.StartsWith("RAIL_", StringComparison.Ordinal) ||
                                    go.name.StartsWith("DOOR_", StringComparison.Ordinal) ||
                                    go.name.StartsWith("PLATFORM_", StringComparison.Ordinal) ||
                                    go.name.StartsWith("DYN_", StringComparison.Ordinal);
            if (gameplayGeometry)
                Require(go.GetComponent<Collider>() != null, go.name + " needs a gameplay Collider.", ref errors);

            Collider collider = go.GetComponent<Collider>();
            Renderer renderer = go.GetComponent<Renderer>();
            if (collider != null)
                Require(renderer == null, go.name + " mixes gameplay Collider and visual Mesh on the same object.", ref errors);

            if (go.name.StartsWith("TRG_", StringComparison.Ordinal) ||
                go.name.StartsWith("GOAL_", StringComparison.Ordinal) ||
                go.name.StartsWith("RESET_", StringComparison.Ordinal))
            {
                Require(collider != null && collider.isTrigger, go.name + " must have a trigger Collider.", ref errors);
            }
        }
    }

    private static void ValidateMissingScriptsAndReferences(Transform root, ref int errors)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObject go = transform.gameObject;
            int missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            Require(missingCount == 0, go.name + " has a Missing Script.", ref errors);

            MonoBehaviour[] behaviours = go.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null) continue;
                SerializedObject serialized = new SerializedObject(behaviour);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        Require(false, behaviour.GetType().Name + "." + property.propertyPath + " is a Missing Reference.", ref errors);
                }
            }
        }
    }

    private static void ValidateProgressionModel(ref int errors)
    {
        ToyWorldRepairItemType[][] orders =
        {
            new[] { ToyWorldRepairItemType.WindUpSpring, ToyWorldRepairItemType.PowerGear, ToyWorldRepairItemType.MelodyCylinder },
            new[] { ToyWorldRepairItemType.WindUpSpring, ToyWorldRepairItemType.MelodyCylinder, ToyWorldRepairItemType.PowerGear },
            new[] { ToyWorldRepairItemType.PowerGear, ToyWorldRepairItemType.WindUpSpring, ToyWorldRepairItemType.MelodyCylinder },
            new[] { ToyWorldRepairItemType.PowerGear, ToyWorldRepairItemType.MelodyCylinder, ToyWorldRepairItemType.WindUpSpring },
            new[] { ToyWorldRepairItemType.MelodyCylinder, ToyWorldRepairItemType.WindUpSpring, ToyWorldRepairItemType.PowerGear },
            new[] { ToyWorldRepairItemType.MelodyCylinder, ToyWorldRepairItemType.PowerGear, ToyWorldRepairItemType.WindUpSpring }
        };

        foreach (ToyWorldRepairItemType[] order in orders)
        {
            ToyWorldProgressState state = new ToyWorldProgressState();
            Require(state.TryCollect(order[0]), "First room collection failed.", ref errors);
            Require(state.TryCollect(order[1]), "Second room collection failed.", ref errors);
            Require(!state.AllCollected, "Final Gate would open at 2/3.", ref errors);
            Require(state.TryCollect(order[2]) && state.AllCollected, "Final Gate did not unlock at 3/3.", ref errors);
            int count = state.CollectedCount;
            Require(!state.TryCollect(order[2]) && state.CollectedCount == count,
                "Duplicate collection changed the count.", ref errors);

            Require(!state.TryInstall(ToyWorldRepairItemType.PowerGear),
                "Install order accepted Gear before Spring.", ref errors);
            Require(state.TryInstall(ToyWorldRepairItemType.WindUpSpring), "Spring installation failed.", ref errors);
            Require(state.TryInstall(ToyWorldRepairItemType.PowerGear), "Gear installation failed.", ref errors);
            Require(!state.CanExit, "Exit became valid before all installation and activation conditions.", ref errors);
            Require(state.TryInstall(ToyWorldRepairItemType.MelodyCylinder), "Cylinder installation failed.", ref errors);
            Require(!state.CanExit, "Exit became valid before Music Box activation.", ref errors);
            Require(state.TryActivateMusicBox() && state.CanExit, "Music Box activation did not validate exit.", ref errors);
            Require(state.TryCompleteLevel() && state.LevelCompleted, "Final completion failed.", ref errors);
        }
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static void Require(bool condition, string message, ref int errors)
    {
        if (condition) return;
        errors++;
        Debug.LogError("[ToyWorldValidator] " + message);
    }
}
