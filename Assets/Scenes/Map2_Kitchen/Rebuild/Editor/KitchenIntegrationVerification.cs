using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Map2KitchenDraft.Editor
{
    public static class KitchenIntegrationVerification
    {
        public static void VerifyAssetReferencesAndScenes()
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(KitchenConfirmedScene.ScenePath));
            foreach (string scenePath in Directory.GetFiles(root + "/Scenes", "*.unity"))
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var objects = scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                    .Select(item => item.gameObject)
                    .ToArray();
                var missingScripts = objects.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);
                var meshes = objects.SelectMany(item => item.GetComponents<MeshFilter>()).ToArray();
                var invalidMeshes = meshes.Count(mesh => mesh.sharedMesh == null || mesh.sharedMesh.vertexCount == 0);
                var failedMaterials = objects.SelectMany(item => item.GetComponents<Renderer>())
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Count(material => material == null || material.shader == null ||
                        material.shader.name == "Hidden/InternalErrorShader");
                var failedCollisionMeshes = objects.SelectMany(item => item.GetComponents<MeshCollider>())
                    .Count(collider => collider.sharedMesh == null || collider.sharedMesh.vertexCount == 0);

                Debug.Log($"[Map2 integration] {scene.name}: {meshes.Length} mesh filters, " +
                    $"{missingScripts} missing scripts, {invalidMeshes} empty render meshes, " +
                    $"{failedMaterials} missing materials/shaders, {failedCollisionMeshes} empty collision meshes");
                if (missingScripts != 0 || invalidMeshes != 0 || failedMaterials != 0 || failedCollisionMeshes != 0)
                    throw new InvalidOperationException("Invalid Map2 asset references in " + scene.name);
            }
            KitchenRebuildVerification.RunForScene(KitchenConfirmedScene.ScenePath, "integration_scene.json");
        }
    }
}
