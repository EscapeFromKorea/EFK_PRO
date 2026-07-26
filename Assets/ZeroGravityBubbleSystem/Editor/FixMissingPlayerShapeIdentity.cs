// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬의 플레이어(PlayerMover 보유 오브젝트)에 PlayerShapeIdentity가 빠져있으면 채워 넣는다.
///
/// 왜 필요한가: 현재 PlayerObjectMenuItem.cs는 플레이어 생성 시 PlayerShapeIdentity를 자동으로
/// 붙이지만, 이미 배치된(문 테스트용으로 위치까지 옮겨둔) 기존 플레이어들은 그 컴포넌트가 도입되기
/// 전 버전으로 만들어져 빠져있다. 무중력 버블처럼 PlayerShapeIdentity.Kind로 도형을 구분하는
/// 기믹은 이게 없으면 도형 판별에 항상 실패해 기본값(배율 1, 즉 무효과)으로만 동작한다.
///
/// 기존 오브젝트를 지우고 다시 만들면 위치/연결이 날아가므로, 지우지 않고 빠진 컴포넌트만
/// 채워 넣는 방식을 쓴다. PlayerObjectMenuItem이 만드는 ShapeStats 에셋 경로(Assets/PlayerSystem/
/// ShapeStats/{Kind}Stats.asset)를 그대로 재사용한다.
/// </summary>
public static class FixMissingPlayerShapeIdentity
{
    [MenuItem("Tools/ZeroGravityBubble/Fix Missing PlayerShapeIdentity")]
    private static void Fix()
    {
        int fixedCount = 0;

        foreach (PlayerMover mover in Object.FindObjectsOfType<PlayerMover>())
        {
            GameObject root = mover.gameObject;
            if (root.GetComponent<PlayerShapeIdentity>() != null) continue;

            PlayerShapeStats.ShapeKind kind = GuessKind(root.name);
            PlayerShapeStats stats = AssetDatabase.LoadAssetAtPath<PlayerShapeStats>(
                $"Assets/PlayerSystem/ShapeStats/{kind}Stats.asset");

            if (stats == null)
            {
                Debug.LogWarning($"[FixMissingPlayerShapeIdentity] '{root.name}'용 ShapeStats 에셋을 " +
                                  $"못 찾았다(Assets/PlayerSystem/ShapeStats/{kind}Stats.asset). 건너뜀.");
                continue;
            }

            PlayerShapeIdentity identity = Undo.AddComponent<PlayerShapeIdentity>(root);
            identity.stats = stats;

            Transform colliderChild = root.transform.Find("Player_Collider");
            if (colliderChild != null)
                identity.solidCollider = colliderChild.GetComponent<Collider>();
            else
                Debug.LogWarning($"[FixMissingPlayerShapeIdentity] '{root.name}' 아래 Player_Collider를 " +
                                  "못 찾아 solidCollider 연결을 건너뛴다.");

            EditorUtility.SetDirty(root);
            fixedCount++;
            Debug.Log($"[FixMissingPlayerShapeIdentity] '{root.name}'에 PlayerShapeIdentity 부착 완료 (Kind={kind}).");
        }

        if (fixedCount > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        else
            Debug.Log("[FixMissingPlayerShapeIdentity] 고칠 게 없다 - 이미 다 붙어있다.");
    }

    private static PlayerShapeStats.ShapeKind GuessKind(string objectName)
    {
        string n = objectName.ToLowerInvariant();
        if (n.Contains("cube")) return PlayerShapeStats.ShapeKind.Cube;
        if (n.Contains("tetra")) return PlayerShapeStats.ShapeKind.Tetrahedron;
        return PlayerShapeStats.ShapeKind.Sphere;
    }
}
