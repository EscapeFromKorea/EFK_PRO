using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > SyncObjectSystem 메뉴. 동기화 오브젝트 씬 세팅용.
///  - Create Sync Pair: 같은 pairId를 가진 Leader / Follower 박스 2개를 나란히 생성.
///  - Add Sync Object To Selection: 선택한 오브젝트(1~2개)에 SyncObject 부착. 2개를 고르면
///    같은 pairId + Leader/Follower로 자동 배정한다.
/// </summary>
public static class SyncObjectMenuItem
{
    [MenuItem("Tools/SyncObjectSystem/Create Sync Pair")]
    private static void CreateSyncPair()
    {
        Vector3 origin = Vector3.zero;
        if (SceneView.lastActiveSceneView != null)
            origin = SceneView.lastActiveSceneView.pivot;

        string pairId = "Pair_" + System.Guid.NewGuid().ToString("N").Substring(0, 4);

        SyncObject leader = MakeBox($"SyncLeader_{pairId}", origin + Vector3.left * 2f, pairId, SyncObject.SyncRole.Leader);
        SyncObject follower = MakeBox($"SyncFollower_{pairId}", origin + Vector3.right * 2f, pairId, SyncObject.SyncRole.Follower);

        Selection.objects = new Object[] { leader.gameObject, follower.gameObject };
        Debug.Log($"[SyncObjectSystem] 동기화 쌍 생성 완료 (pairId = {pairId})");
    }

    private static SyncObject MakeBox(string name, Vector3 pos, string pairId, SyncObject.SyncRole role)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "Create Sync Pair");
        go.transform.position = pos;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = role == SyncObject.SyncRole.Follower;

        SyncObject sync = go.AddComponent<SyncObject>();
        sync.pairId = pairId;
        sync.role = role;
        return sync;
    }

    [MenuItem("Tools/SyncObjectSystem/Add Sync Object To Selection")]
    private static void AddToSelection()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[SyncObjectSystem] 먼저 오브젝트를 1~2개 선택하세요.");
            return;
        }

        string pairId = "Pair_" + System.Guid.NewGuid().ToString("N").Substring(0, 4);
        for (int i = 0; i < selected.Length && i < 2; i++)
        {
            if (selected[i].GetComponent<SyncObject>() != null) continue;
            SyncObject sync = Undo.AddComponent<SyncObject>(selected[i]);
            sync.pairId = pairId;
            sync.role = i == 0 ? SyncObject.SyncRole.Leader : SyncObject.SyncRole.Follower;
        }

        Debug.Log($"[SyncObjectSystem] SyncObject 추가 완료 (pairId = {pairId})");
    }
}
