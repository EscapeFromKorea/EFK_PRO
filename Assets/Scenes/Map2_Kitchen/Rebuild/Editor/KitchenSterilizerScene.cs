using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Map2KitchenDraft.Editor
{
    internal static class KitchenSterilizerScene
    {
        private const string RootName = "CH2 timed sterilizer";

        [MenuItem("Tools/Map2 Kitchen Rebuild/Apply Chapter 2 Sterilizer")]
        public static void ApplyToPlayable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before updating the sterilizer.");

            var scene = EditorSceneManager.OpenScene(KitchenConfirmedScene.ScenePath, OpenSceneMode.Single);
            Apply(Object.FindFirstObjectByType<KitchenDraftController>());
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save the playable kitchen scene.");
        }

        internal static void Apply(KitchenDraftController draft)
        {
            Transform chapter = draft.chapters[1].transform.parent;
            Transform previous = chapter.Find(RootName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            for (int i = 0; i < chapter.childCount; i++)
            {
                GameObject item = chapter.GetChild(i).gameObject;
                if (item.name.StartsWith("Fixed sterilizer nozzle ", StringComparison.Ordinal) ||
                    item.name.StartsWith("Fixed water jet ", StringComparison.Ordinal))
                    item.SetActive(false);
            }

            var steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/Steel.mat");
            var dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/SteelDark.mat");
            var mark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Map2_Kitchen/Rebuild/Materials/V3/Trim.mat");
            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Map2_Kitchen/Rebuild/Materials/WaterJet.mat");
            if (steel == null || dark == null || mark == null || water == null)
                throw new InvalidOperationException("Missing sterilizer materials.");

            Transform root = Child(chapter, RootName);
            float[] safeZ = { -20f, 12.5f, 39.5f, 66.5f };
            var recovery = new Transform[safeZ.Length];
            for (int i = 0; i < safeZ.Length; i++)
            {
                recovery[i] = Child(root, "Sterilizer safe point " + (i + 1));
                recovery[i].position = new Vector3(-118f, 8.9f, safeZ[i]);
                foreach (float side in new[] { -1f, 1f })
                    Visual(PrimitiveType.Cube, recovery[i], "Safe point belt-edge marker",
                        new Vector3(-118f + side * 3.25f, 8.095f, safeZ[i]),
                        new Vector3(.16f, .05f, 1.65f), mark);
            }

            float[] phases = { .06f, .51f, .79f, .29f, .64f, .11f, .46f, .86f, .25f, .61f, .94f, .39f };
            var stations = new KitchenSterilizer.Jet[12];
            for (int i = 0; i < stations.Length; i++)
            {
                int section = i / 3;
                float z = -10f + i * 9f;
                float bias = (i % 2 == 0 ? -1f : 1f) * (.45f + .18f * section);
                float travel = 1.10f + .15f * section;
                var center = new Vector3(-118f + bias, 15.66f, z);
                Visual(PrimitiveType.Cube, root, "Sterilizer carriage rail " + (i + 1),
                    center + Vector3.up * .29f, new Vector3(2f * travel + .76f, .16f, .28f), steel);
                Transform head = Child(root, "Sliding nozzle " + (i + 1));
                head.position = center;
                Visual(PrimitiveType.Cylinder, head, "Stainless nozzle body",
                    center, new Vector3(.64f, .21f, .64f), dark);
                Transform stream = Child(head, "Moving water jet " + (i + 1));
                stream.localPosition = Vector3.down * .23f;
                var effect = stream.gameObject.AddComponent<DraftWaterJet>();
                effect.width = .50f;
                effect.localEnd = new Vector3(0f, -7.32f, 0f);
                effect.waterMaterial = water;
                stations[i] = new KitchenSterilizer.Jet
                {
                    head = head,
                    spray = effect,
                    center = center,
                    travel = travel,
                    period = (5.8f - .46f * section + .21f * (i % 3)) / (section == 0 ? 1.10f : 1.15f),
                    phase = phases[i],
                    recovery = recovery[section]
                };
            }

            root.gameObject.AddComponent<KitchenSterilizer>().Configure(stations);
            draft.chapters[1].objective = "좌우로 움직이는 물줄기를 피해 세척기를 통과하세요";
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform Visual(PrimitiveType type, Transform parent, string name,
            Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item.transform;
        }
    }
}
