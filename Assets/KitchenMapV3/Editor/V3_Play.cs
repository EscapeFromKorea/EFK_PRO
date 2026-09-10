#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — 플레이 세팅.
/// 캐릭터는 직접 만들지 않는다 — 정식 생성기(Tools/PlayerSystem/Create Player/…)를 호출하고
/// 위치만 옮긴다(3단 계층 + 접지 레이어 마스크 때문. 직접 만들면 Space가 영원히 안 먹는다).
/// 스폰 [검증 v3 START]: 문서 (62,6,1.0) / (65,6,1.0) / (63.5,8,1.0).
/// </summary>
public static class V3Play
{
    public static void SetupPlay()
    {
        // 1) 도형 3종 — 정식 생성기 호출
        SpawnShape("Sphere",      new Vector3(62f, 6f, 1.0f));
        SpawnShape("Cube",        new Vector3(65f, 6f, 1.0f));
        SpawnShape("Tetrahedron", new Vector3(63.5f, 8f, 1.0f));

        // 2) 컨트롤 스위처(Tab 전환 — 이름 오름차순)
        if (Object.FindObjectOfType<PlayerControlSwitcher>() == null)
            new GameObject("PlayerControlSwitcher").AddComponent<PlayerControlSwitcher>();

        // 3) 카메라: 자유시점 3인칭(V3ThirdPersonCamera — 우클릭 회전·Q/E·Z/C·V 리셋·휠 줌).
        //    [정정 2026-09-10, 컨트롤타워 36-b A2 결정①] PlayerFollowCamera는 더 이상 붙이지
        //    않는다. 예전엔 "Awake가 비활성화하니 함께 붙여도 안전"이라 봤는데, enabled=false는
        //    PFC.instance를 orbitYaw 미초기화 상태로 반쯤 살려 둬 develop
        //    PlayerMover.EffectiveInputYaw()가 그 값을 읽어 입력 yaw를 0도로 덮어쓰는 회귀를
        //    낳는다(§36-b 실측 — 상세 근거는 V3ThirdPersonCamera.Awake 주석). SpawnShape가 호출한
        //    정식 생성기(PlayerObjectMenuItem.EnsureFollowCamera)가 이 시점 이전에 먼저 붙여
        //    놨을 수도 있으므로, 있으면 무조건 파괴한다 — 새로 만들지도, 비활성화만 하지도 않는다.
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        PlayerFollowCamera existingFollow = cam.GetComponent<PlayerFollowCamera>();
        if (existingFollow != null)
            Object.DestroyImmediate(existingFollow);
        if (cam.GetComponent<V3ThirdPersonCamera>() == null)
            cam.gameObject.AddComponent<V3ThirdPersonCamera>();
        cam.transform.position = V3.Doc(63.5f, -8f, 8f);
        cam.transform.LookAt(V3.Doc(63.5f, 10f, 2f));

        // 4) 리스폰 컨트롤러 (RespawnSystem 정식 메뉴 경유)
        if (Object.FindObjectOfType<RespawnController>() == null)
            EditorApplication.ExecuteMenuItem("Tools/Respawn/Create Respawn Controller");

        // 5) 조명(블록아웃 가시성)
        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject sun = new GameObject("Directional Light");
            Light l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0);
        }
        V3.Log("Setup Play 완료 — ▶ 실행. WASD/Space 이동·점프, Tab 전환.");
    }

    static void SpawnShape(string shape, Vector3 doc)
    {
        string goName = "Player_" + shape;
        GameObject existing = GameObject.Find(goName);
        if (existing == null)
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/PlayerSystem/Create Player/" + shape))
            {
                V3.Warn("정식 생성기 메뉴 실행 실패: " + shape);
                return;
            }
            existing = GameObject.Find(goName);
        }
        if (existing == null) { V3.Warn(goName + " 를 찾지 못했다."); return; }
        existing.transform.position = V3.Doc(doc.x, doc.y, doc.z);
    }
}
#endif
