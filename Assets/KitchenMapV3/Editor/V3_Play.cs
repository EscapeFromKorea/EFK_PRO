#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// KitchenMapV3 — 플레이 세팅.
/// 캐릭터는 직접 만들지 않는다 — 정식 생성기(Tools/PlayerSystem/Create Player/…)를 호출하고
/// 위치만 옮긴다(3단 계층 + 접지 레이어 마스크 때문. 직접 만들면 Space가 영원히 안 먹는다).
/// 스폰 [검증 v3 START]: 문서 (62,6,1.0) / (65,6,1.0) / (63.5,8,1.0).
///
/// [2026-09-12, Codex 검수 K01·K04·K10 반영]
///  · K01: 첫 줄에서 V3.EnsureOwnedScene 가드 — 팀 씬·additive·표식 없는 동명 오브젝트면 무변경 중단.
///    이번 호출이 만든 Player_*·스위처·카메라·RespawnController·조명에는 V3_Owned 표식을 붙인다.
///  · K04: 반환형 bool — 필수 생성(도형 3종·리스폰 컨트롤러) 실패는 Debug.LogError로 올리고 false를
///    돌려준다(V3_Batch.RunAll이 exitCode에 반영). 예전엔 V3.Warn(경고)만 내고 void라 무인 검증이
///    실패를 성공으로 보고할 수 있었다.
///  · K10: URP 프로젝트에서 정식 생성기가 세모에 붙이는 Standard 재질(PlayerObjectMenuItem.
///    CreateDefaultMaterial — Standard→Diffuse 순 선택)은 오류 셰이더(마젠타)로 그려진다 — 이 씬의
///    참조만 URP Lit 재질로 교체한다(팀 코드 무수정, .mat 파일 없음).
/// </summary>
public static class V3Play
{
    public static bool SetupPlay()
    {
        if (!V3.EnsureOwnedScene("Setup Play")) return false;
        // [H05 최소 수정 2, r3 후속 2026-09-14 — 사본 전용] 표식 없는 활성 카메라는 어떤 변경(도형 스폰·스위처 생성)도 하기 전에 거부한다.
        {
            Camera cam0 = Camera.main;
            if (cam0 != null && !V3.IsOwned(cam0.gameObject))
            {
                Debug.LogError($"[KitchenMapV3] Setup Play: 활성 카메라 '{cam0.name}'에 V3_Owned 표식이 없다 — 이 도구가 만든 카메라가 아니므로 인수하지 않는다. 변경 없음(스폰 전 중단).");
                return false;
            }
        }
        bool ok = true;

        // 1) 도형 3종 — 정식 생성기 호출
        ok &= SpawnShape("Sphere",      new Vector3(62f, 6f, 1.0f));
        ok &= SpawnShape("Cube",        new Vector3(65f, 6f, 1.0f));
        ok &= SpawnShape("Tetrahedron", new Vector3(63.5f, 8f, 1.0f));

        // Local stair trial: preserve team code; install real-contact relay and friction wrapper
        // only on the three owned players this setup created/reused.
        foreach (string name in new[] { "Player_Sphere", "Player_Cube", "Player_Tetrahedron" })
        {
            GameObject player = V3.FindInActiveScene(name, true);
            if (player == null || !V3.IsOwned(player)) continue;
            var shape = player.GetComponent<PlayerShapeController>();
            if (shape == null || shape.groundContact == null) { ok=false; continue; }
            if (player.GetComponent<V3_WallSlip>() == null) player.AddComponent<V3_WallSlip>();
            var relay = player.GetComponent<V3_GroundContactRelay>();
            if (relay == null) relay=player.AddComponent<V3_GroundContactRelay>();
            relay.receiver=shape.groundContact;
        }

        // 2) 컨트롤 스위처(Tab 전환 — 이름 오름차순). 생성기(EnsureSwitcher)가 이미 만들었으면 이번
        //    호출 산출물이라 표식만 붙인다 — 표식 없는 외부 스위처는 위 가드가 이미 걸렀다.
        PlayerControlSwitcher switcher = Object.FindObjectOfType<PlayerControlSwitcher>(true);
        if (switcher == null)
            switcher = new GameObject("PlayerControlSwitcher").AddComponent<PlayerControlSwitcher>();
        V3.MarkOwned(switcher.gameObject, "V3_Play");

        // 3) 카메라: 자유시점 3인칭(V3ThirdPersonCamera — 우클릭 회전·Q/E·Z/C·V 리셋·휠 줌).
        //    [정정 2026-09-10, 컨트롤타워 36-b A2 결정①] PlayerFollowCamera는 더 이상 붙이지
        //    않는다. 예전엔 "Awake가 비활성화하니 함께 붙여도 안전"이라 봤는데, enabled=false는
        //    PFC.instance를 orbitYaw 미초기화 상태로 반쯤 살려 둬 develop
        //    PlayerMover.EffectiveInputYaw()가 그 값을 읽어 입력 yaw를 0도로 덮어쓰는 회귀를
        //    낳는다(§36-b 실측 — 상세 근거는 V3ThirdPersonCamera.Awake 주석). SpawnShape가 호출한
        //    정식 생성기(PlayerObjectMenuItem.EnsureFollowCamera)가 이 시점 이전에 먼저 붙여
        //    놨을 수도 있으므로, 있으면 무조건 파괴한다 — 새로 만들지도, 비활성화만 하지도 않는다.
        //    [K01] Camera.main이 있으면 이 씬(가드 통과 = V3 전용 씬)의 기본 카메라로 보고 인수한다
        //    (표식 부착) — 새 씬(Basic)의 기본 Main Camera가 이 경우다. 씬 밖 카메라는 가드가 막는다.
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            V3.MarkOwned(camGo, "V3_Play");
        }
        else if (!V3.IsOwned(cam.gameObject))
        {
            // [H05 최소 수정, r3 후속 2026-09-14 — 사본 전용] 표식 없는 카메라는 인수(표식 부착·컴포넌트 변경)하지 않는다.
            // 이름·허용 폴더·미저장 상태만으로 다른 소유자의 카메라를 넘겨받지 않는다(Codex 후속지시 §2-C). 부분 변경 없이 중단.
            // 영향: 새 씬(Basic)의 기본 Main Camera가 있으면 Setup Play가 거부된다 — 빈 새 씬(EmptyScene)에서 실행하거나 그 카메라를
            // 직접 지운 뒤 실행해야 한다(보고서 §C에 사용자 결정 항목으로 표시).
            Debug.LogError($"[KitchenMapV3] Setup Play: 활성 카메라 '{cam.name}'에 V3_Owned 표식이 없다 — 이 도구가 만든 카메라가 아니므로 인수하지 않는다. 변경 없음.");
            return false;
        }
        PlayerFollowCamera existingFollow = cam.GetComponent<PlayerFollowCamera>();
        if (existingFollow != null)
            Object.DestroyImmediate(existingFollow);
        if (cam.GetComponent<V3ThirdPersonCamera>() == null)
            cam.gameObject.AddComponent<V3ThirdPersonCamera>();
        // [K09] 아래 시작 위치는 Play 전 씬뷰 확인용일 뿐이다 — ▶ 직후 첫 LateUpdate에서
        // V3ThirdPersonCamera.SnapToTarget이 충돌 당김(벽·가구 뒤로 나가지 않게)까지 적용해 덮어쓴다.
        cam.transform.position = V3.Doc(63.5f, -8f, 8f);
        cam.transform.LookAt(V3.Doc(63.5f, 10f, 2f));

        // 3b) [K10] URP에서 세모 재질(Standard) 교체.
        FixTetrahedronMaterialForPipeline();

        // 4) 리스폰 컨트롤러 (RespawnSystem 정식 메뉴 경유). 표식 없는 기존 컨트롤러는 가드가 걸렀다.
        RespawnController rc = Object.FindObjectOfType<RespawnController>(true);
        if (rc == null)
        {
            if (!EditorApplication.ExecuteMenuItem("Tools/Respawn/Create Respawn Controller"))
            {
                Debug.LogError("[KitchenMapV3] Setup Play: 'Tools/Respawn/Create Respawn Controller' 메뉴 실행 실패 — RespawnSystem이 프로젝트에 있는지 확인.");
                ok = false;
            }
            rc = Object.FindObjectOfType<RespawnController>(true);
            if (rc == null)
            {
                Debug.LogError("[KitchenMapV3] Setup Play: 생성기 실행 후에도 RespawnController를 찾지 못했다 — 리스폰 없이 플레이하게 된다.");
                ok = false;
            }
        }
        if (rc != null) V3.MarkOwned(rc.gameObject, "V3_Play");

        // 5) 조명(블록아웃 가시성)
        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject sun = new GameObject("Directional Light");
            Light l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0);
            V3.MarkOwned(sun, "V3_Play");
        }

        if (ok) V3.Log("Setup Play 완료 — ▶ 실행. WASD/Space 이동·점프, Tab 전환.");
        else Debug.LogError("[KitchenMapV3] Setup Play: 필수 생성 일부 실패 — 위 오류 참조. 이 상태의 플레이 결과를 성공으로 보고하지 마라.");
        return ok;
    }

    /// <summary>[K01·K04] 표식 없는 동명 오브젝트가 있으면 재사용·이동·복제 없이 실패(false). 생성기
    /// 실패도 false + LogError. 이번 호출로 새로 생긴 루트 오브젝트(플레이어 계층·생성기 부수물인
    /// PlayerControlSwitcher)에 V3_Owned 표식을 붙인다 — 동기 호출이라 다른 주체가 끼어들 수 없다.</summary>
    static bool SpawnShape(string shape, Vector3 doc)
    {
        string goName = "Player_" + shape;
        GameObject existing = V3.FindInActiveScene(goName, includeInactive: true);
        if (existing != null && !V3.IsOwned(existing))
        {
            Debug.LogError($"[KitchenMapV3] {goName}: V3_Owned 표식이 없는 동명 오브젝트가 이미 있다 — 재사용·이동하지 않고 복제도 하지 않는다. 직접 정리 후 재실행.");
            return false;
        }
        if (existing == null)
        {
            var before = new HashSet<int>();
            foreach (GameObject g in V3.AllSceneObjects(true)) before.Add(g.GetInstanceID());

            if (!EditorApplication.ExecuteMenuItem("Tools/PlayerSystem/Create Player/" + shape))
            {
                Debug.LogError("[KitchenMapV3] 정식 생성기 메뉴 실행 실패: " + shape + " — PlayerSystem이 프로젝트에 있는지 확인.");
                return false;
            }
            existing = V3.FindInActiveScene(goName, true);
            if (existing == null)
            {
                Debug.LogError($"[KitchenMapV3] {goName} 를 생성기 실행 후에도 찾지 못했다.");
                return false;
            }
            foreach (GameObject g in V3.AllSceneObjects(true))
                if (g.transform.parent == null && !before.Contains(g.GetInstanceID()))
                    V3.MarkOwned(g, "V3_Play.SpawnShape(" + shape + ")");
        }
        existing.transform.position = V3.Doc(doc.x, doc.y, doc.z);
        return true;
    }

    /// <summary>[K10, 2026-09-12] 근거: Codex 검수 사본 씬 YAML의 세모 재질 `m_Name: Standard` ·
    /// `m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000}`(내장 Standard) + 이 프로젝트의
    /// URP 14.0.12(Packages/manifest.json) + Windows 실행 화면 마젠타(실행화면_세모마젠타.jpg). 팀 생성기
    /// PlayerObjectMenuItem.CreateDefaultMaterial()이 Standard→Diffuse 순으로 고르는 것이 원인 —
    /// URP는 그 셰이더를 오류(MaterialError, 마젠타)로 그린다. 구·네모는 CreatePrimitive 기본
    /// 재질(URP 기본 Lit)이라 정상. 팀 코드는 손대지 않고 이 씬의 렌더러 참조만 URP Lit 재질로
    /// 바꾼다(씬 내 재질, .mat 파일 없음, 원래 재질 오브젝트는 파괴하지 않음). Built-in RP
    /// (currentRenderPipeline==null, develop 62f4e29가 이 경우)면 아무것도 하지 않는다 — 거기선
    /// Standard가 정상이다.</summary>
    static void FixTetrahedronMaterialForPipeline()
    {
        GameObject tetra = V3.FindInActiveScene("Player_Tetrahedron", true);
        if (tetra == null) return;
        // [K13 일반화] 같은 원인(팀 생성기의 Standard 재질)이 DoorSystem 무게판에도 있어 공통 헬퍼로 옮겼다 —
        // V3.ReplaceBuiltinMaterialsForPipeline(Editor/V3_Core.cs). 세모 색은 흰색 기본값이라 회색(0.62)이 된다.
        V3.ReplaceBuiltinMaterialsForPipeline(tetra, "K10 Setup Play/세모");
    }
}
#endif
