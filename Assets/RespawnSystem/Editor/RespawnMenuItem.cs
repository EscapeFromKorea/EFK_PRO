using UnityEditor;
using UnityEngine;

/// <summary>
/// 리스폰 세팅 생성 메뉴.
///
/// PRD 작성 시점에는 "트리거 박스 하나 + 빈 오브젝트 하나"라 메뉴가 절약해 주는 게 없다고 보고
/// 만들지 않았는데, 깃발 막대가 붙으면서 계층이 4단(구역 → 막대 → 막대메쉬/깃발)이 되고
/// 콜라이더 제거·피벗 정렬처럼 손으로 하면 틀리기 쉬운 조립이 생겨 그 판단이 뒤집혔다.
///
/// [막대·깃발에 콜라이더를 남기지 않는 이유 — 중요]
/// 체크포인트의 페이드 스폰 지점은 <b>구역 중앙에서 아래로 쏜 레이가 찾은 바닥</b>이다. 막대에
/// 콜라이더가 있으면 그 레이가 막대 꼭대기를 바닥으로 잡아 플레이어가 공중에 선다. 그래서
/// CreatePrimitive가 자동으로 붙이는 콜라이더를 전부 지운다(RespawnZone 쪽에도 자식은 바닥으로
/// 치지 않는 가드를 뒀지만, 애초에 안 만드는 편이 낫다).
///
/// [막대를 구역 중앙이 아니라 가장자리에 두는 이유]
/// 중앙은 세 도형이 리스폰해 서 있을 자리다. 막대를 거기 두면 플레이어가 막대 속에서 나타난다.
/// </summary>
public static class RespawnMenuItem
{
    private const float ZoneWidth = 6f;
    private const float ZoneHeight = 18f;   // 낙하 연출 길이 = 구역 높이(권장 15~20 Unit)
    private const float PoleHeight = 4f;
    private const float OutOfBoundsSize = 20f;

    [MenuItem("Tools/Respawn/Create Checkpoint Pole")]
    public static void CreateCheckpoint()
    {
        Vector3 spawn = SceneViewCenter();

        GameObject zone = new GameObject("Checkpoint");
        // 구역 원점을 바닥으로 잡는다 — 씬에 놓을 때 지형 위에 올려두기만 하면 되도록.
        zone.transform.position = spawn;

        BoxCollider box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(ZoneWidth, ZoneHeight, ZoneWidth);
        box.center = new Vector3(0f, ZoneHeight * 0.5f, 0f);

        RespawnZone respawnZone = zone.AddComponent<RespawnZone>();

        // 막대 = 밑동이 원점인 빈 오브젝트. 깃발의 게양 시작 높이(raiseFromLocalY=0)가 여기 기준이라
        // 피벗이 밑동이어야 한다(Cylinder 프리미티브는 피벗이 중심이라 그대로 쓰면 어긋난다).
        GameObject pole = new GameObject("Pole");
        pole.transform.SetParent(zone.transform, false);
        pole.transform.localPosition = new Vector3(-ZoneWidth * 0.5f + 0.4f, 0f, 0f);

        GameObject poleMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleMesh.name = "PoleMesh";
        poleMesh.transform.SetParent(pole.transform, false);
        poleMesh.transform.localPosition = new Vector3(0f, PoleHeight * 0.5f, 0f);
        poleMesh.transform.localScale = new Vector3(0.1f, PoleHeight * 0.5f, 0.1f); // Cylinder는 높이 2가 기본
        StripCollider(poleMesh);

        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Flag";
        flag.transform.SetParent(pole.transform, false);
        // 깃발이 막대 옆으로 뻗어 나오게 X로 반쯤 밀어 둔다. 인스펙터에 배치된 이 위치가 게양 완료
        // 지점이 되고, 게양 연출은 로컬 Y 0(밑동)에서 여기까지 올라온다.
        flag.transform.localPosition = new Vector3(0.45f, PoleHeight * 0.85f, 0f);
        flag.transform.localScale = new Vector3(0.9f, 0.55f, 0.04f);
        StripCollider(flag);

        respawnZone.flagRenderer = flag.GetComponent<Renderer>();

        Undo.RegisterCreatedObjectUndo(zone, "Create Checkpoint Pole");
        Selection.activeGameObject = zone;

        Debug.Log("[Respawn] 체크포인트를 만들었다. 지형 위에 올려놓고 구간 진입부에 배치해라 — " +
                  "선택하면 낙하 스폰(하늘색)과 페이드 바닥(노랑)이 기즈모로 보인다. " +
                  "바닥 마커가 빨간색이면 구역 아래에서 바닥을 못 찾은 것이다.", zone);
    }

    /// <summary>장외 판정 볼륨("Respawn Scale") — 씬에서 크기를 조절해 리스폰 판정 범위를 눈으로
    /// 잡는다. 킬 라인(killY)을 <b>대체하지 않고 더한다</b>: 라인이 맵 아래를 무조건 덮고, 이
    /// 볼륨은 라인이 못 잡는 곳(옆으로 튕겨나감, 지형 틈에 낀 채 안 떨어짐)을 골라 덮는다.</summary>
    [MenuItem("Tools/Respawn/Create Respawn Scale")]
    public static void CreateRespawnScale()
    {
        GameObject go = new GameObject("RespawnScale");
        go.transform.position = SceneViewCenter();

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(OutOfBoundsSize, OutOfBoundsSize, OutOfBoundsSize);

        go.AddComponent<OutOfBoundsVolume>();

        Undo.RegisterCreatedObjectUndo(go, "Create Respawn Scale");
        Selection.activeGameObject = go;

        Debug.Log("[Respawn] 장외 판정 볼륨을 만들었다(보라색 기즈모). 스케일이나 BoxCollider의 Size로 " +
                  "범위를 잡아라 — 이 안에 outOfBoundsSeconds(기본 3초) 이상 머무르면 리스폰된다. " +
                  "킬 라인(killY)은 그대로 살아 있고, 이 볼륨은 거기에 더해지는 판정이다.", go);
    }

    [MenuItem("Tools/Respawn/Create Respawn Controller")]
    public static void CreateController()
    {
        if (Object.FindObjectOfType<RespawnController>() != null)
        {
            Debug.LogWarning("[Respawn] 씬에 RespawnController가 이미 있다 — 하나만 둔다.");
            return;
        }

        GameObject go = new GameObject("RespawnController");
        go.AddComponent<RespawnController>();

        Undo.RegisterCreatedObjectUndo(go, "Create Respawn Controller");
        Selection.activeGameObject = go;

        Debug.Log("[Respawn] 컨트롤러를 만들었다. killY(기본 -30)를 맵 최저 지형보다 아래로 맞춰라 — " +
                  "씬 뷰의 붉은 평면이 그 높이다.", go);
    }

    private static void StripCollider(GameObject go)
    {
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
    }

    private static Vector3 SceneViewCenter()
    {
        SceneView view = SceneView.lastActiveSceneView;
        return view != null ? view.pivot : Vector3.zero;
    }
}
