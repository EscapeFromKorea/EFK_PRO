using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [멀티 M2] 시작 처리 — 고른 맵을 각자 로컬로 로드하고(씬 동기화는 M3) 기존 플레이어·조작·카메라 싱글톤을 치운 뒤,
/// 다 불러왔음을 호스트에 알린다. 도형 스폰은 MpLobby.LoadedRpc. 맵 안 플레이어는 M1 프로토 플레이어를 임시로 쓴다(M3에서 정식 교체).
/// </summary>
public static class MpGame
{
    // 도형 슬롯별 스폰 위치 = 그 맵 씬에 원래 있던 같은 종류 도형의 위치(P-01: 씬 배치를 존중). 슬롯 순서는 MpLobby.ShapePrefabs(구·정육면체·정사면체)
    static readonly Vector3?[] spawnBySlot = new Vector3?[3];

    public static void Begin(MpLobby lobby) => MpNet.I.StartCoroutine(Run(lobby));

    public static bool TrySpawnPoint(ulong clientId, out Vector3 pos)
    {
        pos = default;
        int slot = MpLobby.I != null ? MpLobby.I.SlotOf(clientId) : -1;
        if (slot < 0 || !spawnBySlot[slot].HasValue) return false;
        pos = spawnBySlot[slot].Value;
        return true;
    }

    static int SlotOfKind(PlayerShapeStats.ShapeKind k) => k == PlayerShapeStats.ShapeKind.Sphere ? 0 : k == PlayerShapeStats.ShapeKind.Cube ? 1 : 2;

    static IEnumerator Run(MpLobby lobby)
    {
        string scene = MpLobby.MapScenes[lobby.Map.Value];
        var op = SceneManager.LoadSceneAsync(scene);
        while (!op.isDone) yield return null;
        yield return null;
        // "씬에 안 놔도 동작"하는 자동 생성형 컨트롤러는 RuntimeInitializeOnLoadMethod(AfterSceneLoad)로 앱 시작 때 한 번만 만들어진다.
        // 첫 씬이 홈 화면이면 홈과 함께 파괴돼 맵에서는 E 상호작용·블록 운반 등이 통째로 죽는다 → 맵을 로드할 때마다 다시 보장한다.
        foreach (var t in new[] { typeof(InteractionController), typeof(BlockCarryController), typeof(CubeDockController), typeof(FrictionStickerController), typeof(SnapBlockController) })
            t.GetMethod("EnsureExists", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
        var movers = Object.FindObjectsByType<PlayerMover>(FindObjectsSortMode.None);
        for (int i = 0; i < 3; i++) spawnBySlot[i] = null;
        foreach (var m in movers)
            if (m.TryGetComponent(out PlayerShapeIdentity id)) spawnBySlot[SlotOfKind(id.Kind)] = m.transform.position + Vector3.up * 0.3f;
        NetProtoRunner.SpawnBase = movers.Length > 0 ? movers.OrderBy(m => m.name).First().transform.position + Vector3.up * 0.3f : new Vector3(0, 2, 0);
        Debug.Log($"[Mp] 맵 로드 {scene} 기존 플레이어 {movers.Length}개 스폰기준={NetProtoRunner.SpawnBase:F2}");
        // 비활성화만 하면 PlayerControlSwitcher·PlayerFollowCamera의 static 인스턴스가 살아남아 새 플레이어의 조작권을 빼앗는다 → 즉시 파괴
        foreach (var m in movers) if (m != null) Object.DestroyImmediate(m.gameObject);
        // Tab 전환 비활성(P-02). 팔로우 카메라는 남겨 두고 내 도형이 스폰되면 타깃만 맞춘다(MpCamera) — 마우스 궤도·충돌 회피 등 정식 카메라를 그대로 쓴다
        foreach (var sw in Object.FindObjectsByType<PlayerControlSwitcher>(FindObjectsSortMode.None)) Object.DestroyImmediate(sw);
        PauseMenuController.OnLeaveToMain = () => _ = MpNet.I.Leave(null); // S-03: ESC 메뉴 "메인화면으로 나가기" = 세션 종료 후 홈
        NetProtoRunner.MapMode = true;
        new GameObject("MpCamera").AddComponent<MpCamera>();
        lobby.LoadedRpc();
    }
}

/// <summary>
/// 내 도형이 스폰되면 씬의 PlayerFollowCamera 타깃을 그 도형으로 고정한다(P-02). 씬에 팔로우 카메라가 없으면(예: 맵4 마스터)
/// 프로토 카메라(Q/E 회전)로 대신한다.
/// </summary>
public class MpCamera : MonoBehaviour
{
    Transform cam;
    PlayerFollowCamera follow;
    bool bound;

    void Start()
    {
        follow = FindFirstObjectByType<PlayerFollowCamera>();
        var c = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (c != null) cam = c.transform;
    }

    void Update()
    {
        if (follow != null) return;
        if (Input.GetKey(KeyCode.Q)) NetProtoRunner.CamYaw -= 90f * Time.deltaTime;
        if (Input.GetKey(KeyCode.E)) NetProtoRunner.CamYaw += 90f * Time.deltaTime;
    }

    void LateUpdate()
    {
        var p = NetProtoRunner.LocalPlayer;
        if (p == null) return;
        if (follow != null)
        {
            if (!bound) { bound = true; follow.target = p; PlayerFollowCamera.SnapToTarget(); }
            return;
        }
        if (cam == null) return;
        var rot = Quaternion.Euler(30f, NetProtoRunner.CamYaw, 0f);
        cam.SetPositionAndRotation(p.position + rot * new Vector3(0, 0, -9f), rot);
    }
}
