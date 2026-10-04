#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 섹터6(CH6) 팀 기믹 배치·배선 — 계약 R3 K6-0·K6-1·K6-2(진행/지시서/R3/_계약.md — R2S68은 이력), 지시서 S6-W4(S6-W3 판을 이음) [C15]·[C16].
/// S6_Builder.Build가 지형·앵커를 만든 뒤 Wire → S6_Dress.Apply 순서로 부른다[계약 K0-2].
/// 근거 우선순위: 사용자 결정 > 컨트롤타워 판정(최신 우선, 진행/판정/ 전부) > 설계서 > 초안 최종본 > 계약 수치(이름·자리 확인용) [계약 R3-0].
/// [C15] P4 자리·높이는 B 앵커 ANCH_S6_Panel_4를 따른다(W 좌표 리터럴 없음) — (가) 현재 배치 유지(진행/판정/2026-09-29_C15_재판정.md).
/// [C16] static Build* 4종·EnergyBall 메뉴 배치 모드 생성 유지.
///
/// [만드는 것 — 전부 generated/S6_Gimmicks 그룹 1개 아래, 씬 루트 잔여 0]
///   S6_Gimmicks
///   ├ S6_Panel_1..N        팀 PortalSurface      — SpacePortalMenuItem.BuildPortalSurface (public static, SpacePortalMenuItem.cs:176)
///   ├ S6_MovPanel_1..M     팀 MovablePortalPanel — SpacePortalMenuItem.BuildMovablePortalPanel (public static, :201)
///   ├ S6_Lever_1..M        팀 PanelLever         — SpacePortalMenuItem.BuildPanelLever(pos, panel) (public static, :39)
///   ├ S6_Ball_Orange/Blue  팀 EnergyBall         — 메뉴 Tools/SpacePortalSystem/Create Energy Ball (Orange)/(Blue) (private 메뉴, :11-15)
///   ├ S6_Zone_Start        팀 RespawnZone        — RespawnMenuItem.CreateCheckpoint (public static, RespawnMenuItem.cs:27-28)
///   │                      루트 = refs.startAnchor, 크기 = refs.startZoneSize [판정 4 · 계약 K6-1]
///   └ S6_Zone_Check_1..2   팀 RespawnZone        — 같은 메뉴. 루트 = checkpointPads[i], 부피 = checkpointPadBounds[i] 그대로 [판정 4 · 계약 K6-1]
///   (Rail 모드 패널이 있을 때만) S6_MovPanel_k_RailStart/_RailEnd 빈 오브젝트 — 팀 startPose/endPose 대상(패널 밖에 둬야 함, S6_팀API §3-3)
///
/// [기믹 동작은 바꾸지 않는다 — HANDOFF §5-0, 공통 규칙 5]
/// - 팀 생성 함수가 만든 구조(콜라이더·시각 자식·머티리얼)를 그대로 쓰고, 루트 위치·회전(배치 변환)과 public 인스펙터 값만 넣는다.
/// - 팀 private 필드는 읽지도 쓰지도 않는다. 리플렉션 없음. 팀 렌더러·머티리얼 손대지 않음.
/// - 부트스트랩·어댑터 없음. 팀 기본값(볼 색·respawnDelaySeconds·트리거 반경, 레버 driveMagnitude, rotationSpeed 등)은 대입하지 않거나 같은 값만 넣는다.
/// - 영구 UnityEvent 배선 없음: SpacePortalSystem에는 UnityEvent가 하나도 없고(S6_팀API §1-6·§3-4·§4-3·§10),
///   레버→패널 연결은 직렬화 참조 PanelLever.panel(public)로 끝난다. RespawnZone은 밟으면 static
///   RespawnController.SetCheckpoint(RespawnController.cs:170)를 부르므로 Master 씬과의 씬 사이 연결도 필요 없다.
/// - 바닥 킬 라인·자동 복귀·장외 볼륨은 만들지 않는다(바닥은 킬 라인이 아님, R 복귀 [확정 S6_설계.md §3-3 · 계약 K6-1]).
/// - PortalTraversable은 쓰지 않는다(물체 운반 없음 [확정 §3-1]).
///
/// 크기·인스펙터 값은 아래 상수 표 한 곳에 모았다. 자리·방향·복귀 구역 부피는 B(S6_Builder)의 refs가 정본이고
/// (W는 초안 JSON zones를 상수로 복사하지 않는다 [판정 4 · 계약 K6-1]), W는 앵커를 옮기거나 지우지 않는다
/// (자기 인스턴스를 앵커 월드 포즈에 맞춘다 [계약 K0-3]). Pivot 값만 계약 필드가 없어 W 상수다 [계약 K6-1 · C16 추인].
/// 출처 태그: [확정]=설계서, [팀]=팀 코드(파일:행), [제안]=초안(진행/초안/S6_배치초안.md '2차 반영'·'2차 반영 수정 1' 절 — 좌표 정본),
/// [계약 Kx]=R3 계약(진행/지시서/R3/_계약.md) 조항, [판정 n]=2026-09-28_S6S8_판정.md 번호, [C n]=2026-09-29_S6S8_2차_판정.md 번호, [사용자 09-29 가]=사용자 결정, [해석]·[명령 결정]=지시서 S6-W3가 정한 세부,
/// [계산]=계산값, [추정]=실측 필요 [판정 23 · 계약 K0-2].
/// Unity 컴파일·실행 미검증(Unity 실행 금지 — 통합 후 대기열). Unity 동봉 csc로 형식 검사만 했다(보고/S6-W3.md · S6-W4.md '수정 스테이징2').
/// </summary>
public static class S6_Wiring
{
    // ═════════════════════════ 상수 표 (확인 뒤 여기만 고친다) ═════════════════════════

    // ── 그룹·이름 ──
    private const string GroupName = "S6_Gimmicks";            // [계약 K0-3] generated 바로 아래 1개
    private const string PanelNamePrefix = "S6_Panel_";        // [제안] k = fixedPanelAnchors 순서 + 1
    private const string MovPanelNamePrefix = "S6_MovPanel_";  // [제안]
    private const string LeverNamePrefix = "S6_Lever_";        // [제안]
    private const string BallOrangeName = "S6_Ball_Orange";    // [제안]
    private const string BallBlueName = "S6_Ball_Blue";        // [제안]
    private const string StartZoneName = "S6_Zone_Start";      // [제안]
    private const string CheckZoneNamePrefix = "S6_Zone_Check_"; // [제안] k = checkpointPads 순서 + 1(낮은 것부터 [계약 K6-1])

    // ── 팀 메뉴 경로 (EnergyBall은 public 생성 함수가 없다 — S6_팀API §5-1) ──
    private const string MenuBallOrange = "Tools/SpacePortalSystem/Create Energy Ball (Orange)"; // [팀 SpacePortalMenuItem.cs:11]
    private const string MenuBallBlue = "Tools/SpacePortalSystem/Create Energy Ball (Blue)";     // [팀 SpacePortalMenuItem.cs:14]
    private const string MenuCheckpoint = "Tools/Respawn/Create Checkpoint Pole";                // [팀 RespawnMenuItem.cs:27] 로그 표기용(함수는 public static으로 직접 부른다)

    // ── 고정 패널(PortalSurface) ──
    /// <summary>[판정 5 · 제안 초안 §4 :219] 3.0 × 3.6 × 0.2. 팀 메뉴 기본은 2.4 × 3.0 × 0.2(SpacePortalMenuItem.cs:155).
    /// 크기는 BoxCollider.size로만 낸다 — 루트 localScale 1 규약(PortalSurface.cs:10-12).
    /// 패널 크기는 결합 상수다 [계약 K6-0 · 검문 코드 S6 지적 1]. 크기를 바꾸려면(예: 팀 기본 (2.4f, 3.0f, 0.2f)) 이 상수 하나로 끝나지 않는다.
    /// 다음을 **함께** 바꾸고 초안 검산(S6_check.py·S6_검산.py·S6_flow.py)을 다시 돌려야 한다.
    ///  ① 여기 FixedPanelSize·MovablePanelSize
    ///  ② 아래 MovableSpecs[1](M2).pivotPointLocal.y(= −반높이, 지금 −1.8 — 아래 모서리 경첩). M1(MovableSpecs[0])은 가로·세로와 묶이지 않고
    ///     두께에만 묶인다: pivotPointLocal.x 6.5 = 경첩 z57.9 − 시작 루트 z51.4(루트 = 앵커 − 법선 × size.z/2) [계산 초안 §4 :230]
    ///  ③ B S6_Builder.cs의 PanelHalfW/H(구덩이·벽감·바닥 구멍·구덩이 디딤 3개 [판정 5])와 M2 앵커 좌표
    ///     (ANCH_S6_MovPanel_2 y = 경첩 6.0 + 반높이, ANCH_S6_MovPanelEnd_2 z = 83.5 + 반높이 — 리터럴)
    ///  ④ L S6_Dress.cs의 FallbackPanelSize ⑤ 초안 meta.panelSize·anchors/movables size.
    /// (K6-0 목록의 L ExitOpeningHeight(= B OpeningH)·FallbackCeilingY(= B CeilY)는 L↔B 결합이라 W 상수와 무관하다.)
    /// ②를 빼먹으면 경첩이 M2 아래 모서리에서 벗어난다. B의 M2 앵커를 새 반높이로 고쳤다면 Pivot 끝 포즈 대조(ApplySpec)에서 LogError가 나지만
    /// (2.4×3.0 예: 0.42 차이), ①만 바꾸고 B 앵커를 그대로 두면 대조는 통과하고 경첩만 모서리 밖 0.3에 남는다(로그 없음 [계산]). 그래서 검산 재실행이 필수다.</summary>
    private static readonly Vector3 FixedPanelSize = new Vector3(3.0f, 3.6f, 0.2f);
    /// <summary>[판정 5 · 제안 초안 JSON movables[*].size] 움직이는 패널도 같은 크기.</summary>
    private static readonly Vector3 MovablePanelSize = new Vector3(3.0f, 3.6f, 0.2f);

    // ── 움직이는 패널(MovablePortalPanel) 사양 — movablePanelAnchors 순서(k)와 1:1 ──
    // 계약 K6-1에 Pivot 값 필드가 없어 W 상수(= 초안 §4·JSON movables 값)로 둔다 [계약 K6-1 · C16 추인].
    // pivotPointLocal·pivotAxisLocal은 패널 **초기 회전 기준 로컬**(MovablePortalPanel.cs:35-38), 회전 중심 = 초기 루트 위치 + 초기회전×pivotPointLocal(:221).
    private static readonly MovableSpec[] MovableSpecs =
    {
        // k=1 (M1, ② 공중 패널): Pivot, 경첩 월드(섹터 로컬) (−4.5, 12.9, 57.9) = 시작 루트 (−4.5,12.9,51.4) + 로컬 (6.5,0,0)[로컬 +X = 월드 +Z],
        //   월드 +Y 둘레 +90° → 끝 루트 (−11,12.9,57.9)·면 (−11,12.9,58.0)·법선 +Z = ANCH_S6_MovPanelEnd_1 [판정 2 · 제안 초안 §4 :220·:230]
        new MovableSpec(MovablePortalPanel.PanelMode.Pivot,
            pivotPointLocal: new Vector3(6.5f, 0f, 0f),   // [판정 2] 동선분석 제안 D(경첩 z57.9·반지름 6.5) · [계산 초안 §4 :230 Pivot 값 풀이]
            pivotAxisLocal: new Vector3(0f, 1f, 0f),      // [제안 초안 §4] = 팀 기본(MovablePortalPanel.cs:38)
            minAngle: 0f, maxAngle: 90f,                  // [제안 초안 §4] = 팀 기본(:40-42)
            rotationSpeed: 45f,                           // [팀 기본 :44]
            travelSpeed: 1f,                              // [팀 기본 :32] (Pivot에선 안 쓰임)
            snapPoints: new float[0],                     // [판정 6 · 확정 설계 §3-4 "손을 떼면 즉시 멈춤"] 팀 기본 null과 같은 뜻(스냅 없음)
            stuckRecoveryDelay: 3f,                       // [팀 기본 :53]
            invertLever: false),                          // [팀 기본 PanelLever.cs:22 · 판정 3] 기둥 +X 쪽에서 밀면 진행도 +(시작→끝)
        // k=2 (M2, ③ 발사 홈 — 우물 낙하 → P4 → 홈 속 눕힌 M2에서 솟구치기 [사용자 09-29 가]): Pivot, 아래 모서리 경첩 (0, 6.0, 83.5)
        //   = 루트 + 로컬 (0,−1.8,0), 축 로컬 (−1,0,0)[= 월드 +X], +90° → 끝 면 (0,6.1,85.3)·법선 +Y = ANCH_S6_MovPanelEnd_2 [제안 초안 §4 :221]
        new MovableSpec(MovablePortalPanel.PanelMode.Pivot,
            pivotPointLocal: new Vector3(0f, -1.8f, 0f),  // [제안 초안 §4] y = −MovablePanelSize.y/2(아래 모서리 경첩) — 패널 크기와 묶임, 위 FixedPanelSize 주석 ①~⑤ 참고
            pivotAxisLocal: new Vector3(-1f, 0f, 0f),     // [제안 초안 §4]
            minAngle: 0f, maxAngle: 90f,                  // [제안 초안 §4]
            rotationSpeed: 45f,                           // [팀 기본]
            travelSpeed: 1f,                              // [팀 기본]
            snapPoints: new float[0],                     // [판정 6 · 확정 설계 §3-4]
            stuckRecoveryDelay: 3f,                       // [팀 기본]
            invertLever: false),                          // [팀 기본]
    };

    // ── 레버(PanelLever) ──
    /// <summary>[팀 SpacePortalMenuItem.cs:41-44] 기둥 = Cube 스케일 (0.4, 1, 0.4), 피벗 = 중심 → 밑동(앵커)에서 0.5 위.
    /// L1 자리는 앵커를 따른다(ANCH_S6_Lever_1 (−24,5,38) [판정 3]) — 코드 값 없음.</summary>
    private const float LeverBodyHalfHeight = 0.5f;
    // driveMagnitude 10·maxArmAngle 30·armSpeed 360은 팀 기본 그대로 — 대입하지 않는다(PanelLever.cs:19, :27-28).

    // ── 에너지볼(EnergyBall) — 색·respawnDelaySeconds(5)·트리거 반경 1.5는 팀 기본 그대로(EnergyBall.cs:25, :33; SpacePortalMenuItem.cs:117) ──
    private const float BallTriggerRadius = 1.5f;          // [팀 SpacePortalMenuItem.cs:117] 검사용 — 대입하지 않는다
    private const float PanelOverlapCheckDepth = 0.5f;     // [팀 PlayerEnergyReceiver.overlapCheckDepth :26] 검사용

    // ── 복귀(팀 RespawnZone) ──
    // 시작 구역 크기는 refs.startZoneSize, 체크 구역 부피는 refs.checkpointPadBounds에서 읽는다 — 여기 상수를 두지 않는다
    // (초안 JSON zones 값 복사 금지, 1차 선택지 (나) 기각 [판정 4 · 계약 K6-1]). 팀 기본 구역은 6×18×6(RespawnMenuItem.cs:22-23, :38-39).
    /// <summary>[명령 결정 — 지시서 S6-W3 반영 수치표] 이 높이 이하의 checkpointPadBounds는 옛 '윗면 영역' 뜻(판정 4로 폐기)으로 보고
    /// 계약 위반 LogError 후 그 체크포인트만 건너뛴다. 늘리거나 다시 들이지 않는다.</summary>
    private const float MinZoneExtent = 0.02f;

    // ── 검사 허용치 ──
    private const float PoseTolerance = 0.02f;             // [계약 K0-2·K6-1 ±0.02]
    private const float AngleToleranceDeg = 0.5f;          // [제안]

    // ═════════════════════════ 진입점 ═════════════════════════

    /// <summary>[계약 K6-2] 에러가 나도 예외를 던지지 않는다(로그만) — S6_Builder는 지형을 지우지 않는다[계약 K0-2].
    /// 앵커가 null이면 LogError 후 그 항목만 건너뛴다.</summary>
    public static void Wire(Transform generated, S6_Refs refs)
    {
        if (generated == null) { Debug.LogError("[S6_Wiring] generated가 null이다 — 팀 기믹을 만들지 않는다."); return; }
        if (refs == null) { Debug.LogError("[S6_Wiring] refs가 null이다 — 팀 기믹을 만들지 않는다."); return; }

        HashSet<GameObject> rootsBefore = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        try
        {
            CheckCounts(refs);
            Transform group = NewGroup(generated);
            if (!IsUnitScale(group))
                Debug.LogError($"[S6_Wiring] 그룹 lossyScale {group.lossyScale}가 1이 아니다 — 팀 PortalSurface 규약(부모 체인 스케일 1, " +
                               "PortalSurface.cs:10-12, MovablePortalPanel.cs:266-267) 위반. 섹터 루트 스케일을 확인해라.", group);

            List<PortalSurface> fixedPanels = BuildFixedPanels(group, refs);
            int movableMade, leversMade;
            List<PortalSurface> movablePanels = BuildMovablePanelsAndLevers(group, refs, out movableMade, out leversMade);
            int ballsMade = BuildBalls(group, refs, fixedPanels, movablePanels);
            int zonesMade = BuildZones(generated, group, refs);

            ReportStrayRoots(generated, rootsBefore);

            Debug.Log($"[S6_Wiring] 완료 — 고정 패널 {fixedPanels.Count}/{Count(refs.fixedPanelAnchors)}, 움직이는 패널 {movableMade}/{Count(refs.movablePanelAnchors)}, " +
                      $"레버 {leversMade}/{Count(refs.leverAnchors)}, 에너지볼 {ballsMade}/2, 복귀 구역 {zonesMade}/{1 + Count(refs.checkpointPads)}" +
                      $"(시작 구역 = refs.startZoneSize {refs.startZoneSize} [판정 4]). 영구 UnityEvent 배선 0(SpacePortalSystem에 UnityEvent 없음).");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S6_Wiring] 배선 중 예외 — 지형은 그대로 둔다: {e}");
            ReportStrayRoots(generated, rootsBefore);
        }
    }

    // ═════════════════════════ 그룹·개수 ═════════════════════════

    private static Transform NewGroup(Transform generated)
    {
        // Generated는 Generate마다 비워지지만, 같은 빌드 안에서 두 번 불려도 겹치지 않게 기존 그룹을 지운다(우리 생성물만 있는 그룹).
        Transform old = generated.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject go = new GameObject(GroupName);
        go.transform.SetParent(generated, false); // 로컬 원점·무회전·스케일 1 = 섹터 로컬 좌표계 그대로
        return go.transform;
    }

    private static int Count<T>(List<T> list) => list != null ? list.Count : 0;

    /// <summary>[계약 K6-1] 개수 규칙(리스트 길이는 하드코딩하지 않고 Count로 돈다). 어긋나도 만들 수 있는 것은 만든다(k별로 짝이 없으면 그 k만 건너뜀).</summary>
    private static void CheckCounts(S6_Refs refs)
    {
        int mov = Count(refs.movablePanelAnchors), end = Count(refs.movablePanelTravelEnd), lev = Count(refs.leverAnchors);
        if (mov != end || mov != lev)
            Debug.LogError($"[S6_Wiring] 계약 K6-1 위반: movablePanelAnchors {mov} · movablePanelTravelEnd {end} · leverAnchors {lev} — 세 개수가 같아야 한다.");
        if (mov != MovableSpecs.Length)
            Debug.LogError($"[S6_Wiring] 움직이는 패널 {mov}개인데 상수 표 MovableSpecs는 {MovableSpecs.Length}개다 — 사양이 없는 k는 만들지 않는다. 초안이 바뀌었으면 상수 표를 고쳐라.");
        int cp = Count(refs.checkpointPads), cb = Count(refs.checkpointPadBounds);
        if (cp != 2 || cb != 2)
            Debug.LogError($"[S6_Wiring] 계약 K6-1 위반: checkpointPads {cp} · checkpointPadBounds {cb} — 둘 다 2여야 한다.");
        if (Count(refs.fixedPanelAnchors) == 0)
            Debug.LogError("[S6_Wiring] fixedPanelAnchors가 비었다 — 고정 패널 0장.");
        if (refs.startAnchor == null) Debug.LogError("[S6_Wiring] startAnchor가 null이다 — 시작 복귀 구역을 만들지 않는다.");
        if (refs.orangeBallAnchor == null) Debug.LogError("[S6_Wiring] orangeBallAnchor가 null이다 — 주황 에너지볼을 만들지 않는다.");
        if (refs.blueBallAnchor == null) Debug.LogError("[S6_Wiring] blueBallAnchor가 null이다 — 파랑 에너지볼을 만들지 않는다.");
    }

    // ═════════════════════════ 고정 패널 ═════════════════════════

    /// <summary>fixedPanelAnchors[k]마다 팀 BuildPortalSurface. 앵커 = 면 중심·forward = 바깥 법선(계약 K6-1) →
    /// 팀 루트(부피 중심) = 앵커 − forward × size.z/2(S6_팀API §2-1).</summary>
    private static List<PortalSurface> BuildFixedPanels(Transform group, S6_Refs refs)
    {
        List<PortalSurface> made = new List<PortalSurface>();
        if (refs.fixedPanelAnchors == null) return made;
        for (int i = 0; i < refs.fixedPanelAnchors.Count; i++)
        {
            Transform a = refs.fixedPanelAnchors[i];
            if (a == null) { Debug.LogError($"[S6_Wiring] fixedPanelAnchors[{i}]가 null이다 — 이 패널만 건너뛴다."); continue; }

            Quaternion rot = PanelRotation(a);
            Vector3 root = a.position - (rot * Vector3.forward) * (FixedPanelSize.z * 0.5f);
            GameObject go = SpacePortalMenuItem.BuildPortalSurface(root, rot, FixedPanelSize); // [팀 :176-195]
            if (go == null) { Debug.LogError($"[S6_Wiring] 팀 BuildPortalSurface가 null을 돌려줬다(fixedPanelAnchors[{i}])."); continue; }
            Adopt(go, group);
            go.name = PanelNamePrefix + (i + 1);

            PortalSurface s = go.GetComponent<PortalSurface>();
            if (s == null) { Debug.LogError($"[S6_Wiring] '{go.name}'에 PortalSurface가 없다.", go); continue; }
            CheckFaceMatchesAnchor(go.transform, FixedPanelSize, a, go.name);
            made.Add(s);
        }
        return made;
    }

    // ═════════════════════════ 움직이는 패널 + 레버 ═════════════════════════

    private static List<PortalSurface> BuildMovablePanelsAndLevers(Transform group, S6_Refs refs, out int panelsMade, out int leversMade)
    {
        panelsMade = 0; leversMade = 0;
        List<PortalSurface> made = new List<PortalSurface>();
        if (refs.movablePanelAnchors == null) return made;

        for (int k = 0; k < refs.movablePanelAnchors.Count; k++)
        {
            Transform a = refs.movablePanelAnchors[k];
            Transform e = refs.movablePanelTravelEnd != null && k < refs.movablePanelTravelEnd.Count ? refs.movablePanelTravelEnd[k] : null;
            Transform l = refs.leverAnchors != null && k < refs.leverAnchors.Count ? refs.leverAnchors[k] : null;
            string tag = (k + 1).ToString();

            if (a == null) { Debug.LogError($"[S6_Wiring] movablePanelAnchors[{k}]가 null이다 — 패널 {tag}과 레버 {tag}를 건너뛴다."); continue; }
            if (k >= MovableSpecs.Length) { Debug.LogError($"[S6_Wiring] 패널 {tag}의 사양이 상수 표에 없다 — 패널 {tag}과 레버 {tag}를 건너뛴다."); continue; }
            MovableSpec spec = MovableSpecs[k];

            Quaternion rot = PanelRotation(a);
            Vector3 root = a.position - (rot * Vector3.forward) * (MovablePanelSize.z * 0.5f);
            MovablePortalPanel p = SpacePortalMenuItem.BuildMovablePortalPanel(root, rot, MovablePanelSize); // [팀 :201-217] Rigidbody·라이더 센서 포함
            if (p == null) { Debug.LogError($"[S6_Wiring] 팀 BuildMovablePortalPanel이 null을 돌려줬다(패널 {tag})."); continue; }
            GameObject go = p.gameObject;
            Adopt(go, group);
            go.name = MovPanelNamePrefix + tag;
            CheckFaceMatchesAnchor(go.transform, MovablePanelSize, a, go.name);

            ApplySpec(p, spec, group, root, rot, e, tag);
            EditorUtility.SetDirty(p);
            panelsMade++;
            PortalSurface surf = go.GetComponent<PortalSurface>();
            if (surf != null) made.Add(surf);

            if (l == null) { Debug.LogError($"[S6_Wiring] leverAnchors[{k}]가 null이다 — 레버 {tag}를 건너뛴다(패널 {tag}는 움직일 수 없다)."); continue; }
            GameObject body = SpacePortalMenuItem.BuildPanelLever(l.position + Vector3.up * LeverBodyHalfHeight, p); // [팀 :39-61] panel 직접 대입
            if (body == null) { Debug.LogError($"[S6_Wiring] 팀 BuildPanelLever가 null을 돌려줬다(레버 {tag})."); continue; }
            Adopt(body, group);
            body.name = LeverNamePrefix + tag;
            // 팀 레버는 identity 회전으로 만든다(:41-45). 방향 판정이 기둥 로컬 X 기준이므로(PanelLever.cs:50-51) 앵커의 수평 방향에 맞춘다.
            body.transform.rotation = FlatRotation(l.forward);
            PanelLever lever = body.GetComponent<PanelLever>();
            if (lever == null) { Debug.LogError($"[S6_Wiring] '{body.name}'에 PanelLever가 없다.", body); continue; }
            if (lever.panel != p) { Debug.LogError($"[S6_Wiring] '{body.name}'.panel이 '{go.name}'이 아니다 — 팀 BuildPanelLever 대입 확인.", body); lever.panel = p; }
            lever.invertDirection = spec.invertLever; // [팀 기본 false]
            EditorUtility.SetDirty(lever);
            leversMade++;
        }
        return made;
    }

    /// <summary>public 인스펙터 값만 넣는다(MovablePortalPanel.cs:21-61). Rail이면 startPose/endPose용 빈 오브젝트를 패널 밖(그룹 아래)에
    /// 만들고(패널 자식이면 Lerp 기준이 같이 움직임 — S6_팀API §3-3), Pivot이면 팀 식으로 끝 포즈를 계산해 TravelEnd 앵커와 대조만 한다.</summary>
    private static void ApplySpec(MovablePortalPanel p, MovableSpec spec, Transform group, Vector3 root, Quaternion rot, Transform end, string tag)
    {
        p.mode = spec.mode;
        p.travelSpeed = spec.travelSpeed;
        p.pivotPointLocal = spec.pivotPointLocal;
        p.pivotAxisLocal = spec.pivotAxisLocal;
        p.minAngle = spec.minAngle;
        p.maxAngle = spec.maxAngle;
        p.rotationSpeed = spec.rotationSpeed;
        p.snapPoints = spec.snapPoints != null ? (float[])spec.snapPoints.Clone() : new float[0];
        p.stuckRecoveryDelay = spec.stuckRecoveryDelay;
        p.debugKeyboardDrive = false; // [팀 기본 :61] 맵에서는 끈다(Q/E 시험 구동)

        if (spec.mode == MovablePortalPanel.PanelMode.Rail)
        {
            // 진행도 0 = startPose 위치 → 첫 FixedUpdate에 패널이 startPose로 순간 이동하므로 초기 루트 위치와 같게 둔다(:219, S6_팀API §3-3).
            GameObject s = NewChild(group, $"{MovPanelNamePrefix}{tag}_RailStart", root, rot);
            p.startPose = s.transform;
            if (end == null)
            {
                Debug.LogError($"[S6_Wiring] movablePanelTravelEnd[{tag}]가 null이다 — Rail 패널 {tag}의 endPose가 없어 움직이지 않는다(:209-210).");
                return;
            }
            Quaternion endRot = PanelRotation(end);
            Vector3 endRoot = end.position - (endRot * Vector3.forward) * (MovablePanelSize.z * 0.5f);
            GameObject eGo = NewChild(group, $"{MovPanelNamePrefix}{tag}_RailEnd", endRoot, endRot);
            p.endPose = eGo.transform;
            if (Quaternion.Angle(endRot, rot) > AngleToleranceDeg)
                Debug.LogWarning($"[S6_Wiring] Rail 패널 {tag}: 끝 앵커 회전이 시작과 {Quaternion.Angle(endRot, rot):0.##}° 다르다 — Rail은 초기 회전을 유지한다(:227). 끝에서 앵커 방향과 어긋난다.");
            return;
        }

        // Pivot: startPose/endPose는 쓰이지 않는다(:205-219) — 팀 기본(null) 그대로 둔다.
        if (end == null) { Debug.LogWarning($"[S6_Wiring] movablePanelTravelEnd[{tag}]가 null이다 — Pivot 끝 포즈 대조를 건너뛴다."); return; }
        Vector3 axisWorld = rot * spec.pivotAxisLocal;
        if (axisWorld.sqrMagnitude < 1e-8f) { Debug.LogError($"[S6_Wiring] 패널 {tag}: pivotAxisLocal이 0이다 — 회전하지 않는다."); return; }

        // 팀 식 그대로(MovablePortalPanel.cs:221-233): 중심 = 초기위치 + 초기회전×pivotPointLocal, 회전 = AngleAxis(max−min, 초기회전×축).
        Vector3 worldPivot = root + rot * spec.pivotPointLocal;
        Quaternion turn = Quaternion.AngleAxis(spec.maxAngle - spec.minAngle, axisWorld.normalized);
        Vector3 endRootCalc = worldPivot + turn * (root - worldPivot);
        Quaternion endRotCalc = turn * rot;
        Vector3 endFaceCalc = endRootCalc + (endRotCalc * Vector3.forward) * (MovablePanelSize.z * 0.5f);
        float dPos = Vector3.Distance(endFaceCalc, end.position);
        float dAng = Vector3.Angle(endRotCalc * Vector3.forward, end.forward);
        if (dPos > PoseTolerance || dAng > AngleToleranceDeg)
            Debug.LogError($"[S6_Wiring] 패널 {tag} Pivot 끝 포즈(팀 식 계산) 면 {endFaceCalc} · 법선 {endRotCalc * Vector3.forward}가 " +
                           $"앵커 '{end.name}' {end.position} · {end.forward}와 다르다(위치 {dPos:0.###}, 각 {dAng:0.##}°). " +
                           "초안 좌표와 상수 표(pivotPointLocal·pivotAxisLocal·각도)를 대조해라.", p);
    }

    // ═════════════════════════ 에너지볼 ═════════════════════════

    private static int BuildBalls(Transform group, S6_Refs refs, List<PortalSurface> fixedPanels, List<PortalSurface> movablePanels)
    {
        int made = 0;
        if (BuildBall(group, refs.orangeBallAnchor, MenuBallOrange, EnergyColor.Orange, BallOrangeName, fixedPanels, movablePanels)) made++;
        if (BuildBall(group, refs.blueBallAnchor, MenuBallBlue, EnergyColor.Blue, BallBlueName, fixedPanels, movablePanels)) made++;
        return made;
    }

    /// <summary>팀 private 메뉴를 경로로 실행한다(S1_Builder.MenuCreate 선례). 메뉴는 씬 뷰 피벗(없으면 원점)에 만들고 선택한다(:106-132) →
    /// 앵커 위치로 옮긴다. 원위치는 플레이 시 Awake의 transform.position이다(EnergyBall.cs:56-60).</summary>
    private static bool BuildBall(Transform group, Transform anchor, string menu, EnergyColor color, string name,
                                  List<PortalSurface> fixedPanels, List<PortalSurface> movablePanels)
    {
        if (anchor == null) return false; // CheckCounts가 이미 LogError
        EnergyBall ball = CreateByMenuPath<EnergyBall>(menu);
        if (ball == null) return false;
        GameObject go = ball.gameObject;
        Adopt(go, group);
        go.name = name;
        go.transform.SetPositionAndRotation(anchor.position, Quaternion.identity);
        if (ball.color != color)
            Debug.LogError($"[S6_Wiring] '{name}' 색이 {ball.color}다(기대 {color}) — 팀 메뉴 '{menu}' 확인. 값을 바꾸지 않는다.", go);

        // [계산 S6_팀API §2-3·§5-4] 볼 트리거(반경 1.5)가 패널 면 앞 0.5 영역에 닿으면 그 패널에 설치 불가 → 면 영역에서 2.0 이상.
        CheckBallClearance(go.transform.position, name, fixedPanels);
        CheckBallClearance(go.transform.position, name, movablePanels);
        return true;
    }

    private static void CheckBallClearance(Vector3 ballPos, string ballName, List<PortalSurface> panels)
    {
        foreach (PortalSurface s in panels)
        {
            if (s == null) continue;
            Vector3 local = s.transform.InverseTransformPoint(ballPos); // 루트 스케일 1 → 로컬 = 월드 단위
            Vector2 half = s.HalfExtents();                             // [팀 PortalSurface.cs:29-33]
            float faceZ = s.Box.size.z * 0.5f;
            // 면(로컬 z = faceZ)부터 바깥으로 overlapCheckDepth까지의 판 영역과 볼 중심 사이 거리
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - half.x);
            float dy = Mathf.Max(0f, Mathf.Abs(local.y) - half.y);
            float dz = local.z < faceZ ? faceZ - local.z : Mathf.Max(0f, local.z - (faceZ + PanelOverlapCheckDepth));
            float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < BallTriggerRadius)
                Debug.LogWarning($"[S6_Wiring] '{ballName}' 트리거(반경 {BallTriggerRadius})가 '{s.name}' 면 앞 {PanelOverlapCheckDepth} 영역에 닿는다(거리 {d:0.##}) — 그 패널에 설치 불가(PlayerEnergyReceiver.cs:308-327).", s);
        }
    }

    // ═════════════════════════ 복귀 구역 ═════════════════════════

    /// <summary>팀 RespawnZone 3개: 시작(startAnchor + startZoneSize) + 체크포인트 2(checkpointPads + checkpointPadBounds, 순서 = 낮은 것 → 높은 것)
    /// [판정 4 · 계약 K6-1]. 두 필드와 startAnchor·startZoneSize는 **RespawnZone 루트 자리와 부피 그 자체**다 — 앵커 = 구역 AABB 아랫면 중심,
    /// Bounds = 가장자리 들임·높이 3이 반영된 부피(W는 그대로 쓰고 다시 들이거나 늘리지 않는다). 1차 판의 '윗면 영역' 해석은 폐기됐다.
    /// 밟으면 저장·세 도형 공유·앞으로만은 팀 기본 동작(RespawnZone.cs:132-139, RespawnController.cs:113-122, :193).
    /// 구역은 축 정렬로만(월드 AABB 사용, RespawnZone.cs:185-195) — 루트 회전 identity. R 복귀 지점 = 구역 중앙 아래 레이가 찾은 바닥(:153-184).</summary>
    private static int BuildZones(Transform generated, Transform group, S6_Refs refs)
    {
        int made = 0;

        // ── 시작 구역: 루트 = startAnchor(아랫면 중심), BoxCollider size = refs.startZoneSize, center = (0, size.y/2, 0) [판정 4 · 계약 K6-1]
        if (refs.startAnchor != null)
        {
            Vector3 size = refs.startZoneSize;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                Debug.LogError($"[S6_Wiring] refs.startZoneSize {size}의 성분이 0 이하다 — 계약 K6-1 위반(B가 채워야 한다, 0 벡터 금지). 시작 복귀 구역만 건너뛴다.", refs.startAnchor);
            }
            else
            {
                Vector3 aLocal = generated.InverseTransformPoint(refs.startAnchor.position);
                Bounds local = new Bounds(aLocal + new Vector3(0f, size.y * 0.5f, 0f), size); // 초안 값이면 섹터 로컬 (−6,0,5)~(6,3,11) [제안 초안 §3-4]
                if (BuildZone(group, StartZoneName, refs.startAnchor.position, LocalToWorldAabb(generated, local))) made++;
            }
        }

        // ── 체크 구역 2개: 루트 = checkpointPads[i], 부피 = checkpointPadBounds[i] 그대로 [판정 4 · 계약 K6-1]
        if (refs.checkpointPads != null)
        {
            float prevY = float.NegativeInfinity;
            for (int i = 0; i < refs.checkpointPads.Count; i++)
            {
                Transform a = refs.checkpointPads[i];
                if (a == null) { Debug.LogError($"[S6_Wiring] checkpointPads[{i}]가 null이다 — 이 체크포인트만 건너뛴다."); continue; }
                if (refs.checkpointPadBounds == null || i >= refs.checkpointPadBounds.Count)
                {
                    Debug.LogError($"[S6_Wiring] checkpointPadBounds[{i}]가 없다 — 체크포인트 {i + 1}을 건너뛴다.");
                    continue;
                }

                Bounds b = refs.checkpointPadBounds[i];
                // [명령 결정] 높이 ≤0.02(옛 '윗면 영역' 뜻)는 계약 위반 → 건너뛴다. [해석] 가로·깊이 ≤0.02도 부피가 아니므로 같게 처리한다.
                if (b.size.y <= MinZoneExtent || b.size.x <= MinZoneExtent || b.size.z <= MinZoneExtent)
                {
                    Debug.LogError($"[S6_Wiring] checkpointPadBounds[{i}] 크기 {b.size}가 부피가 아니다(성분 ≤ {MinZoneExtent}) — 계약 K6-1 위반" +
                                   "(판정 4로 '윗면 영역' 뜻은 폐기, Bounds = RespawnZone 부피 그대로). 늘리지 않고 체크포인트 " + (i + 1) + "만 건너뛴다.", a);
                    continue;
                }

                // 앵커 = AABB 아랫면 중심(±0.02) [계약 K6-1]. 어긋나도 구역은 Bounds대로 만든다(루트 = 앵커 → center는 차로 맞춘다).
                Vector3 aLocal = generated.InverseTransformPoint(a.position);
                Vector3 bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
                Vector3 d = aLocal - bottomCenter;
                if (Mathf.Abs(d.x) > PoseTolerance || Mathf.Abs(d.y) > PoseTolerance || Mathf.Abs(d.z) > PoseTolerance)
                    Debug.LogError($"[S6_Wiring] checkpointPads[{i}] {aLocal}가 checkpointPadBounds[{i}] {b.min}~{b.max}의 아랫면 중심 {bottomCenter}와 " +
                                   $"다르다(차 {d}, 허용 ±{PoseTolerance}) — 계약 K6-1 위반. 구역은 Bounds대로 만든다.", a);
                if (aLocal.y <= prevY)
                    Debug.LogError($"[S6_Wiring] checkpointPads 순서 위반: [{i}] 높이 {aLocal.y:0.##} ≤ 앞 {prevY:0.##} — 계약 K6-1은 낮은 것부터.", a);
                prevY = aLocal.y;

                if (BuildZone(group, CheckZoneNamePrefix + (i + 1), a.position, LocalToWorldAabb(generated, b))) made++;
            }
        }
        return made;
    }

    /// <summary>팀 CreateCheckpoint(public static)로 만들고 루트 = rootWorld(앵커 = 구역 아랫면 중심), 무회전, BoxCollider center/size로 worldBox를 덮는다.
    /// 막대·깃발 자식은 팀 그대로(막대는 구역 원점 기준 x −2.6, 콜라이더 없음 — RespawnMenuItem.cs:45-65).</summary>
    private static bool BuildZone(Transform group, string name, Vector3 rootWorld, Bounds worldBox)
    {
        RespawnZone zone = CreateByTeamCall<RespawnZone>(RespawnMenuItem.CreateCheckpoint, MenuCheckpoint);
        if (zone == null) return false;
        GameObject go = zone.gameObject;
        Adopt(go, group);
        go.name = name;
        go.transform.SetPositionAndRotation(rootWorld, Quaternion.identity); // 팀 전제: 축 정렬(RespawnZone.cs:185-186)
        go.transform.localScale = Vector3.one;

        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box == null) { Debug.LogError($"[S6_Wiring] '{name}'에 BoxCollider가 없다.", go); return false; }
        box.isTrigger = true;                     // [팀 RespawnMenuItem.cs:37] 그대로
        box.center = worldBox.center - rootWorld; // 무회전·스케일 1 → 월드 차 = 로컬. 앵커가 아랫면 중심이면 (0, size.y/2, 0)
        box.size = worldBox.size;
        EditorUtility.SetDirty(box);
        return true;
    }

    private static Bounds LocalToWorldAabb(Transform generated, Bounds local)
    {
        Vector3 mn = local.min, mx = local.max;
        Bounds w = new Bounds(generated.TransformPoint(mn), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 c = new Vector3((i & 1) != 0 ? mx.x : mn.x, (i & 2) != 0 ? mx.y : mn.y, (i & 4) != 0 ? mx.z : mn.z);
            w.Encapsulate(generated.TransformPoint(c));
        }
        return w;
    }

    // ═════════════════════════ 팀 생성물 받기 ═════════════════════════

    /// <summary>팀 private 메뉴를 경로로 실행하고 생성물을 받는다. Selection 수신은 배치 모드 실측 기록이 없어(S6_팀API §12 "모르겠다")
    /// 호출 전후 목록 비교로도 찾는다(S5_Wiring.CreateByTeamMenu 선례).</summary>
    private static T CreateByMenuPath<T>(string path) where T : Component
    {
        HashSet<T> before = new HashSet<T>(Object.FindObjectsOfType<T>(true));
        Selection.activeGameObject = null;
        bool ok = EditorApplication.ExecuteMenuItem(path);
        T made = PickNew(before);
        if (made == null) Debug.LogError($"[S6_Wiring] 팀 메뉴 '{path}' 실행 {(ok ? "성공" : "실패")} — 생성물({typeof(T).Name})을 찾지 못했다.");
        return made;
    }

    /// <summary>팀 public static 생성 함수(반환값 없음, 끝에 Selection 설정)를 부르고 생성물을 받는다.</summary>
    private static T CreateByTeamCall<T>(System.Action create, string menuLabel) where T : Component
    {
        HashSet<T> before = new HashSet<T>(Object.FindObjectsOfType<T>(true));
        Selection.activeGameObject = null;
        create();
        T made = PickNew(before);
        if (made == null) Debug.LogError($"[S6_Wiring] 팀 메뉴 '{menuLabel}' 생성물({typeof(T).Name})을 찾지 못했다.");
        return made;
    }

    private static T PickNew<T>(HashSet<T> before) where T : Component
    {
        GameObject sel = Selection.activeGameObject;
        if (sel != null)
        {
            T c = sel.GetComponent<T>();
            if (c != null && !before.Contains(c)) return c;
        }
        foreach (T c in Object.FindObjectsOfType<T>(true))
            if (!before.Contains(c)) return c;
        return null;
    }

    // ═════════════════════════ 검사·헬퍼 ═════════════════════════

    /// <summary>팀 생성 함수는 씬 루트에 만든다 — 그룹 아래로 옮기지 못한 것이 남으면 재생성 때 지워지지 않아 중복된다(계약 K0-3 "씬 루트 잔여 0").
    /// Wire가 불린 뒤 새로 생긴 루트만 본다(S1_Builder.ReportStrayRoots 방식을 이 함수 범위로 좁힘). 지우지 않고 보고만 한다.</summary>
    private static void ReportStrayRoots(Transform generated, HashSet<GameObject> rootsBefore)
    {
        foreach (GameObject root in generated.gameObject.scene.GetRootGameObjects())
            if (!rootsBefore.Contains(root))
                Debug.LogError($"[S6_Wiring] 씬 루트에 남은 오브젝트 '{root.name}' — {GroupName} 아래로 옮기지 못했다(재생성 시 중복).", root);
    }

    private static void Adopt(GameObject go, Transform group)
    {
        if (go != null) go.transform.SetParent(group, true); // 월드 포즈 유지(그룹 스케일 1)
    }

    private static GameObject NewChild(Transform parent, string name, Vector3 worldPos, Quaternion worldRot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        return go;
    }

    /// <summary>앵커 포즈 → 팀 패널 회전(로컬 +Z = 바깥 법선, +Y = 세로, PortalSurface.cs:10-14). 앵커 up이 법선과 평행하면
    /// 팀 SurfaceRotationFromNormal(:166-170, 바닥/천장이면 up 힌트 +Z)로 대신한다.</summary>
    private static Quaternion PanelRotation(Transform a)
    {
        Vector3 f = a.forward, u = a.up;
        if (Mathf.Abs(Vector3.Dot(f.normalized, u.normalized)) > 0.99f)
            return SpacePortalMenuItem.SurfaceRotationFromNormal(f);
        return Quaternion.LookRotation(f, u);
    }

    private static Quaternion FlatRotation(Vector3 forward)
    {
        Vector3 f = new Vector3(forward.x, 0f, forward.z);
        return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
    }

    private static bool IsUnitScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Abs(s.x - 1f) < 1e-4f && Mathf.Abs(s.y - 1f) < 1e-4f && Mathf.Abs(s.z - 1f) < 1e-4f;
    }

    /// <summary>만든 패널의 면 중심·법선이 앵커와 같은지(±0.02, 0.5°). 부모 스케일이 1이 아니면 어긋난다.</summary>
    private static void CheckFaceMatchesAnchor(Transform panel, Vector3 size, Transform anchor, string name)
    {
        Vector3 face = panel.position + panel.forward * (size.z * 0.5f);
        float dPos = Vector3.Distance(face, anchor.position);
        float dAng = Vector3.Angle(panel.forward, anchor.forward);
        if (dPos > PoseTolerance || dAng > AngleToleranceDeg)
            Debug.LogError($"[S6_Wiring] '{name}' 면 중심 {face}·법선 {panel.forward}가 앵커 '{anchor.name}' {anchor.position}·{anchor.forward}와 다르다(위치 {dPos:0.###}, 각 {dAng:0.##}°).", panel);
        if (Mathf.Abs(panel.localScale.x - 1f) > 1e-4f || Mathf.Abs(panel.localScale.y - 1f) > 1e-4f || Mathf.Abs(panel.localScale.z - 1f) > 1e-4f)
            Debug.LogError($"[S6_Wiring] '{name}' localScale {panel.localScale} ≠ 1 — 팀 규약 위반(PortalSurface.cs:10-12).", panel);
    }

    // ═════════════════════════ 사양 자료형 ═════════════════════════

    private sealed class MovableSpec
    {
        public readonly MovablePortalPanel.PanelMode mode;
        public readonly Vector3 pivotPointLocal;
        public readonly Vector3 pivotAxisLocal;
        public readonly float minAngle, maxAngle, rotationSpeed, travelSpeed, stuckRecoveryDelay;
        public readonly float[] snapPoints;
        public readonly bool invertLever;

        public MovableSpec(MovablePortalPanel.PanelMode mode, Vector3 pivotPointLocal, Vector3 pivotAxisLocal,
                           float minAngle, float maxAngle, float rotationSpeed, float travelSpeed,
                           float[] snapPoints, float stuckRecoveryDelay, bool invertLever)
        {
            this.mode = mode;
            this.pivotPointLocal = pivotPointLocal;
            this.pivotAxisLocal = pivotAxisLocal;
            this.minAngle = minAngle;
            this.maxAngle = maxAngle;
            this.rotationSpeed = rotationSpeed;
            this.travelSpeed = travelSpeed;
            this.snapPoints = snapPoints;
            this.stuckRecoveryDelay = stuckRecoveryDelay;
            this.invertLever = invertLever;
        }
    }
}
#endif
