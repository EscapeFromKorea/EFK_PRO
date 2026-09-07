using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/WindupAxleSystem 메뉴 — 씬에 태엽 축 테스트 리그를 생성하고 순수 함수를 점검한다.
///
/// [2026-09-04 1차 외형 재설계] 드럼이 작고(지름 0.3) 손잡이가 트리거 콜라이더뿐이라 시각 메쉬가
/// 아예 없었고, 회전판도 매끈한 원기둥이라 Y축 회전이 눈에 안 보였다. 드럼을 키우고 손잡이·회전판에
/// 시각 메쉬를 붙였다.
///
/// [2026-09-04 2차 재설계 — 캡스턴 손잡이, 이후 되돌림] 방전 곡선을 타는 출력세기로 도는
/// 인디케이터는 너무 느리고 미묘해서(테스트 실측: 한 번 밀면 chargeRatio 0.1 → 초당 9도, 1초
/// 미만 지속) "내가 지금 이걸 돌렸다"는 게 거의 안 보였다. 처음엔 패들을 손잡이 양 끝에 자식으로
/// 붙여 물리적으로 반대편으로 넘어가게 했는데, 손잡이가 도는 동안 반대색 끝이 플레이어를 스치며
/// 지나가 그 자리에서 반대 방향 입력이 다시 잡혀 스윙이 곧바로 되돌아가는 문제가 났다.
///
/// [2026-09-04 3차 — 패들은 고정, 중앙 인디케이터만 회전] 패들(정/역방향 트리거) 위치는 다시
/// 드럼 양 끝 고정 위치로 되돌렸다 — 패들 자체가 물리적으로 움직일 필요는 없고, 밀었을 때 "돈다"는
/// 느낌은 드럼 중심의 별도 인디케이터 막대(트리거 없음, 순수 시각)가 대신 보여준다. 손잡이 스윙에는
/// 쿨다운(`WindupAxle.crankSwingCooldown`, 기본 1초)을 둬서 짧은 겹침이 반대로 잘못 잡혀도 곧바로
/// 되돌아가지 않게 했다. **이후 5차에서 전면 폐기됨** — 아래 참고.
///
/// [2026-09-04 4차 — 인디케이터 좌우 색 분리, 5차에서 대체됨] 3차 인디케이터가 흰색 대칭 막대라
/// 180도 돌아도 정지 상태에서는 이전과 똑같이 보였다. 막대를 초록/빨강 절반으로 나눠 봤지만, 이후
/// 사용자가 원한 건 "인디케이터가 도는 척"이 아니라 **패들 자체가 실제로 자리를 바꾸는 것**이었다.
///
/// [2026-09-04 5차 — 패들이 실제로 Y축 기준 180도 도는 아암에 붙음, 2차 캡스턴 재도입+보강]
/// 2차에서 겹었던 "손잡이가 도는 동안 반대색 끝이 스쳐 재발화" 문제를, 이번엔
/// `WindupAxle.crankSwingCooldown` 유예시간이 회전뿐 아니라 저장량 반영까지 통째로 잠그도록
/// 고쳐서(`WindupAxle.ApplyRotation` 참고) 막았다. 추가로 `WindupPaddleInput`이 접촉 순간 플레이어
/// 속도의 수평/수직 성분을 비교해 "옆에서 미는" 접촉만 인정하고 위/아래 접근은 무시한다. 회전축도
/// 드럼 길이축(X, 눕혀 돌리기)에서 **월드 Y(회전문처럼 수평으로 도는 팔)** 로 바꿨다 — 요청대로
/// "Y축으로 180도 회전".
///
/// [2026-09-04 6차 — 패들 2개 → 막대 1개, 회전 방향은 미는 방향으로 동적 판정] 5차는 여전히
/// "정방향 패들(초록)"·"역방향 패들(빨강)"이 방향을 하드코딩해 갖고 있었다. 요청은 "패들 두 개"가
/// 아니라 **기둥 위에 얹힌 막대 하나**를 어느 방향으로 밀든 그 방향으로 돌게 하는 것 — 색으로
/// 방향을 구분하던 방식 자체를 버렸다. 막대는 이제 콜라이더 하나(어디를 밀어도 인식), 색 절반은
/// 순수 장식(회전이 눈에 보이게)이고, 회전 부호는 `WindupPaddleInput`이 접촉 지점·속도로 매번 계산한다.
/// 회전각도 180 → `WindupAxle.crankSwingDegrees`(기본 90)로 노브화했다. 막대 높이도 기둥 꼭대기에
/// 바로 얹히도록 낮췄다(1.0 → 0.5, 플레이어 콜라이더 중심 높이에 맞춤).
/// </summary>
public static class WindupAxleMenuItem
{
    private const float StickHeight = 0.5f;      // 막대가 위치하는 지면으로부터의 높이(기둥 꼭대기)
    private const float StickHalfLength = 1.3f;  // 피벗 ~ 막대 끝 거리(5차보다 조금 길게)
    private const float StickThickness = 0.3f;
    private const float PoleRadius = 0.15f;

    [MenuItem("Tools/WindupAxleSystem/Create Windup Axle")]
    private static void CreateWindupAxle()
    {
        Vector3 spawnPos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        GameObject axleObj = new GameObject("WindupAxle", typeof(WindupAxle));
        axleObj.transform.position = spawnPos; // 피벗은 바닥.
        WindupAxle axle = axleObj.GetComponent<WindupAxle>();

        // 중앙 기둥 — 장식 + 충전 발광(bodyRenderer, 선택 기능). 회전하지 않는다.
        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "WindupAxle_Pole";
        Object.DestroyImmediate(pole.GetComponent<Collider>());
        pole.transform.SetParent(axleObj.transform, false);
        pole.transform.localPosition = new Vector3(0f, StickHeight * 0.5f, 0f);
        pole.transform.localScale = new Vector3(PoleRadius * 2f, StickHeight * 0.5f, PoleRadius * 2f);
        Renderer bodyRenderer = pole.GetComponent<Renderer>();
        bodyRenderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.4f, 0.1f) };
        axle.bodyRenderer = bodyRenderer;

        // 회전 막대 — 기둥 꼭대기에 얹혀 월드 Y축을 기준으로 도는 실제 물리 막대(WindupAxle.crank).
        // 트리거 콜라이더 하나가 막대 전체 길이를 덮는다 — 어느 쪽을, 어느 방향으로 밀든
        // WindupPaddleInput이 그 자리에서 회전 부호를 계산해 넘긴다("패들 두 개, 색으로 방향 고정"
        // 방식은 6차에서 폐기).
        GameObject stick = new GameObject("WindupAxle_Stick", typeof(BoxCollider), typeof(WindupPaddleInput));
        stick.transform.SetParent(axleObj.transform, false);
        stick.transform.localPosition = new Vector3(0f, StickHeight, 0f);
        axle.crank = stick.transform;

        BoxCollider col = stick.GetComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = new Vector3(StickHalfLength * 2f, StickThickness, StickThickness);

        WindupPaddleInput input = stick.GetComponent<WindupPaddleInput>();
        input.axle = axle;
        input.deltaPerHit = 1f;

        // 좌우 절반을 색으로 갈라 순수 장식(회전이 눈에 보이게) — 방향 판정과는 무관하다.
        CreateStickHalf(stick.transform, "Stick_Half_A", StickHalfLength * 0.5f, new Color(0.2f, 0.8f, 0.3f));
        CreateStickHalf(stick.transform, "Stick_Half_B", -StickHalfLength * 0.5f, new Color(0.85f, 0.25f, 0.2f));

        // 발동까지 남은 시간 카운트다운(2026-09-05, 사용자 요청) — 축 자신 위로 띄운다.
        CreateReleaseTimer(axleObj.transform, axle);

        Undo.RegisterCreatedObjectUndo(axleObj, "Create Windup Axle");
        Selection.activeGameObject = axleObj;
    }

    private static void CreateStickHalf(Transform parent, string name, float localX, Color color)
    {
        GameObject half = GameObject.CreatePrimitive(PrimitiveType.Cube);
        half.name = name;
        Object.DestroyImmediate(half.GetComponent<Collider>());
        half.transform.SetParent(parent, false);
        half.transform.localPosition = new Vector3(localX, 0f, 0f);
        half.transform.localScale = new Vector3(StickHalfLength, StickThickness * 0.9f, StickThickness * 0.9f);
        half.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
    }

    private const float ReleaseTimerHeight = 1.6f; // 축 위로 띄우는 높이 — 손잡이 막대보다 위
    private const float ReleaseTimerCharacterSize = 0.3f;

    // 카운트다운은 처음엔 숨겨져 있어야 하므로(WindupReleaseTimer.Update가 매 프레임 알파를
    // 계산하지만 lastSwingTime 초기값이 NegativeInfinity라 자연히 alpha=0으로 시작한다) 별도
    // 초기화가 필요 없다.
    private static void CreateReleaseTimer(Transform parent, WindupAxle axle)
    {
        GameObject timerObj = new GameObject("WindupAxle_ReleaseTimer", typeof(TextMesh));
        timerObj.transform.SetParent(parent, false);
        timerObj.transform.localPosition = new Vector3(0f, ReleaseTimerHeight, 0f);

        TextMesh label = timerObj.GetComponent<TextMesh>();
        label.characterSize = ReleaseTimerCharacterSize;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = Color.black;

        WindupReleaseTimer timer = timerObj.AddComponent<WindupReleaseTimer>();
        timer.axle = axle;
        timer.releaseDelay = 3f; // RotatingPlatform/RailCart 기본값과 일치
    }

    [MenuItem("Tools/WindupAxleSystem/Create Rotating Platform")]
    private static void CreateRotatingPlatform()
    {
        Vector3 spawnPos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        platform.name = "WindupRotatingPlatform";
        platform.transform.position = spawnPos;
        platform.transform.localScale = new Vector3(3f, 0.2f, 3f);
        platform.AddComponent<RotatingPlatform>();
        platform.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.55f, 0.6f) };

        // 매끈한 원판은 Y축으로 돌아도 시각적으로 정지와 구분이 안 된다 — 대비되는 십자 스포크를
        // 윗면에 얹는다. 부모(platform)가 비균일 스케일(3,0.2,3)이라 자식 로컬 스케일에도 그대로
        // 곱해지므로, 원하는 월드 크기를 부모 스케일로 나눠서 넣는다.
        CreateSpoke(platform.transform, new Vector3(2.7f / 3f, 0.15f / 0.2f, 0.2f / 3f));
        CreateSpoke(platform.transform, new Vector3(0.2f / 3f, 0.15f / 0.2f, 2.7f / 3f));

        Undo.RegisterCreatedObjectUndo(platform, "Create Rotating Platform");
        Selection.activeGameObject = platform;
    }

    private static void CreateSpoke(Transform parent, Vector3 localScale)
    {
        GameObject spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spoke.name = "Spoke";
        Object.DestroyImmediate(spoke.GetComponent<Collider>());
        spoke.transform.SetParent(parent, false);
        spoke.transform.localPosition = new Vector3(0f, 1.05f, 0f); // 디스크 윗면(로컬 y=1.0) 바로 위
        spoke.transform.localScale = localScale;
        spoke.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.15f, 0.1f) };
    }

    [MenuItem("Tools/WindupAxleSystem/Self-Check")]
    private static void SelfCheck()
    {
        string report = WindupAxle.SelfCheck();
        if (report == "OK") Debug.Log("[WindupAxle] Self-Check 통과");
        else Debug.LogError("[WindupAxle] Self-Check 실패:\n" + report);
    }
}
