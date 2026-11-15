#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 섹터4 "반중력·보안" 빌더 — 지형(R2 과제 S4-B → R3 과제 S4-B3). 진행/초안/S4_배치초안.md(최종본 — §13 → §14 → §15, 지형 좌표는 §14·§15에서
/// 바뀌지 않음) §3-1 좌표표를 섹터 씬 Generated/ 아래에 짓는다. Map4SceneBuilder.BuildSectorScene이 SectorBuilderRegistry를 거쳐
/// S4일 때 빈 틀 대신 이것을 부르고, 연결 통로(z 124~136, 윗면 18)·마커는 공용 코드(BuildConnectorAndMarkers)가 이어서 만든다
/// (1차 계약 C3). S4 빌더는 TEMP_Ramp를 만들지 않는다 — 출구 높이 길은 나선 계단(0→8) + 버블(8→18) + 출구 다리(18)다 [계약 C2-4].
///
/// [짓는 것] 바닥 3(입구 통로·원통 바닥·출구 다리) · 상자 벽 7(입구 W/E·버블 북쪽 벽·난간 W/E·출구 W/E) · 기둥·받침대 5 ·
///   원통 벽(곡면 — (나) 볼록 조각 48개, 코드 생성 메시) · 큰 돌(48각 기둥, 볼록 MeshCollider 1) · 나선 계단 6구간(3.75° 볼록 쐐기 88) ·
///   S4_Gimmicks 그룹 + S4_Bubble 트리거 자리(우리 BoxCollider isTrigger, 팀 컴포넌트 없음). 천장 없음(초안 §2 :17, §3-1에 천장 행 없음).
///   고정 장애물(GEO_S4_Obst_)은 없다 — 계약 C2-1 S4 "장애물 규칙 없음", C2-4에 Obst 행 없음. 기둥·받침대는 GEO_S4_Mast_, 돌은 GEO_S4_Rock.
///
/// [이름 — R3 계약 C2-0·C2-4] GEO_S4_Floor_Entry/Cyl · GEO_S4_Bridge_Exit · GEO_S4_Wall_EntryW/E·BubbleN·RailW/E·ExitW/E ·
///   GEO_S4_Wall_Cyl_{nn}(nn = 00~47, (나) 볼록 조각 — (가) 비볼록 1개 GEO_S4_Wall_Cyl는 폐기 [2차판정 15·16]) · GEO_S4_Rock · GEO_S4_Mast_SW/SE/NW/NE/StairAim ·
///   계단 그룹 GEO_S4_Stair_R1…L3 + 쐐기 &lt;그룹&gt;_W{nn}(그룹 안 번호). 콜라이더는 GEO_ 오브젝트, 렌더러는 콜라이더 없는 자식
///   VIS_&lt;GEO_ 뗀 이름&gt;(Map4Build 상자는 자식 Visual을 이 이름으로 바꾼다 — S5_Builder.Solid 방식).
///
/// [곡면 고체 — 초안 §3-1-1] 메시는 이 파일 안 private 함수로 코드 생성한다(Map4Build.Ramp 방식, 메시 에셋 파일 없음). 오브젝트는
///   localPosition 0·회전 0이라 메시 정점 = 섹터 로컬 좌표. 면마다 정점을 따로 둬 플랫 셰이딩(Ramp 3차 반려 A와 같음).
///   · 돌: 외접원 r 10 48각형, 꼭짓점 φ = k·7.5° [계산 — 초안에 위상 명시 없음, 계단 구간 경계가 모두 7.5 배수라 이렇게 둔다].
///   · 계단 쐐기: φ 270→600을 3.75°마다 88개. 안쪽 꼭짓점 = 돌 꼭짓점(r 10)과 변 가운데점(r 10·cos3.75°)을 번갈아 → 돌 옆면과 같은 평면,
///     바깥 r 15. 윗면 높이는 구간 안에서 φ에 비례(반지름 무관), 아래는 y 0까지 채움. 윗면 두 삼각형은 볼록 껍질의 윗면과 같은 대각선.
///   · 원통 벽: 환형 r 34~34.5, y 0~26, 7.5° 볼록 조각 48개 GEO_S4_Wall_Cyl_00~_47 [2차판정 15·16 · R3 계약 C2-4]. 정규 꼭짓점
///     φ = k·7.5°(k = 0~47, 돌 48각과 같은 위상 [계산 — 초안 §3-1-1에 원통 위상 명시 없음, 보고서에 보고]). 조각 k = 꼭짓점 k → k+1,
///     이웃 조각과 꼭짓점을 공유한다(이음매 틈 0·겹침 0 — 곡면 조건 틈 0·겹침 ≤0.02 [2차판정 15]). F1-3 '4m 조각 금지'는 곧은 벽 규칙이다.
///     개구부 경계(x = ±4.5 평면)에 가장 가까운 정규 꼭짓점 4개(k 11·13·35·37, 각 약 0.1° 이동 [계산])를 그 평면 위 점으로 바꿔
///     개구부 옆면이 x = ±4.5에 정확히 놓이게 한다. 남 개구부 위(y 6~26) 조각 35·36, 북 개구부 아래(y 0~17.7) 조각 11·12.
///
/// [이 파일이 하지 않는 것] 팀 기믹(ZeroGravityBubble 부착·인스펙터 값, 낙석 3, 레이저 10) 배치, S4_Refs 기믹 목록(bubble·rockSpawners·
///   fixedLasers·aimLasers) 채우기와 그 개수 검증 — [2차판정 4·R3 계약 C1-1] 기믹 목록 검증 = Wire 몫, 빌더는 트리거 자리·floors·
///   지형만 검증한다(ValidateTerrainRefs). 복귀 장치는 S4_Wiring, 머티리얼·조명·VIS_S4_BubbleShell은 S4_Dress 몫(계약 C1-5·C1-6).
///
/// [예외 — R2 계약 C5(판정 8)] 전제 불일치면 아무것도 만들지 않고 false. 지형 단계 예외면 Generated를 비우고 Build 중 새로 생긴
///   씬 루트를 지운 뒤 false. Wire·Dress 예외는 LogError만 하고 지형을 남긴다. 정적 필드 g는 모든 경로에서 null로 돌린다.
///
/// 출처 표기: [확정] 설계서 사용자 확정 · [판정] 판정 문서 · [계약] R2/1차 계약 · [제안] 초안 제안값 · [계산] 계산값 · [추정] 확인 못 함.
/// </summary>
public static class S4_Builder
{
    // ── 전제 (R2 계약 C5 · Map4Layout.asset · 판정 15) ──
    private const float SectorWidth = 72f;     // [계약 C5] S4 폭 72
    private const float SectorLength = 124f;   // [계약 C5]·[판정 15 유지] 길이 124
    private const float FloorHeight = 0f;      // [계약 C5] 바닥 높이 0
    private const float ExitHeight = 18f;      // [계약 C5]·[확정] 출구 높이 18(= S5 바닥)
    private const float PreconditionTolerance = 0.01f; // [계약 C5] ±0.01
    private const float SeamTolerance = 0.02f;          // [계약 C2-4]·[00_기반 결정11] 턱 0·틈 0(±0.02)

    // ── 원통 (초안 §2 :18, §3-1 :38 · check.py :80-84·:124-127) ──
    private const float CylAxisX = 0f, CylAxisZ = 62f;  // [제안 초안 §2] 원통 축 = 섹터 가운데
    private const float CylInnerR = 34f;                // [제안] 안쪽 반지름
    private const float CylWallT = 0.5f;                // [팀 관례] 벽 두께(Map4SceneBuilder wallThickness)
    private const float CylWallTop = 26f;               // [제안] 벽 높이
    private const float OpeningHalfX = 4.5f;            // [제안 초안 :38] 개구부 x ±4.5(남·북)
    private const float SouthOpeningTop = 6f;           // [제안 초안 :38] 남 개구부 y 0~6
    private const float NorthOpeningBottom = 17.7f;     // [제안 초안 :38] 북 개구부 y ≥ 17.7 = 다리 아랫면
    /// <summary>원통 벽 볼록 조각 수 — 48각(한 조각 7.5°, 안쪽 현 약 4.45U) [2차판정 16]·[R3 계약 C2-4]·[제안 초안 §3-1-1 (나) "48~96개(약 4.45U
    /// 또는 2.2U)"]. 구성은 (나) 볼록 조각만 둔다 — (가) 비볼록 1개는 폐기 [2차판정 15].</summary>
    private const int CylSegments = 48;
    private const int ExpectedOpeningPieces = 2;        // [계산] 개구부마다 조각 2개(남 35·36, 북 11·12) — 절단각 90°/270° ± 7.60°

    // ── 큰 돌 (초안 :39·:61) ──
    private const float RockR = 10f;                    // [제안] 외접원 반지름
    private const float RockTop = 8f;                   // [제안] 높이(설계서 "예: 높이 8")
    private const int RockSides = 48;                   // [제안 check.py N_POLY] 48각형
    private const float PolyStepDeg = 360f / RockSides; // [계산] 7.5°

    // ── 나선 계단 (초안 :40-45·:55·:62 · check.py :89-96) ──
    private const float StairInR = RockR;               // [제안] 계단 안쪽 = 돌 옆면(r 10)
    private const float StairOutR = 15f;                // [제안] 폭 5
    private const float WedgeStepDeg = 3.75f;           // [제안 초안 :62] 쐐기 분할 3.75°
    private const int ExpectedWedges = 88;              // [계산] 330 / 3.75

    private struct Seg
    {
        public readonly string id; public readonly float phi0, phi1, h0, h1;
        public Seg(string id, float phi0, float phi1, float h0, float h1) { this.id = id; this.phi0 = phi0; this.phi1 = phi1; this.h0 = h0; this.h1 = h1; }
    }

    /// <summary>나선 6구간(φ는 270→600으로 풀어 쓴 값, 윗면 높이 시작→끝) [제안 초안 §3-1 :40-45 · check.py STAIR_SEGS].</summary>
    private static readonly Seg[] StairSegs =
    {
        new Seg("R1", 270f, 345f, 0f, 2.5f),
        new Seg("L1", 345f, 375f, 2.5f, 2.5f),
        new Seg("R2", 375f, 465f, 2.5f, 5.5f),
        new Seg("L2", 465f, 495f, 5.5f, 5.5f),
        new Seg("R3", 495f, 570f, 5.5f, 8f),
        new Seg("L3", 570f, 600f, 8f, 8f),
    };

    // ── 상자 지형 (초안 §3-1 :35-52 · check.py BOXES) ──
    private enum Kind { Floor, Wall }

    private struct Box
    {
        public readonly string name; public readonly Kind kind; public readonly Vector3 min, max;
        public Box(string name, Kind kind, Vector3 min, Vector3 max) { this.name = name; this.kind = kind; this.min = min; this.max = max; }
    }

    private static Box B(string name, Kind kind, float x0, float y0, float z0, float x1, float y1, float z1) =>
        new Box(name, kind, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

    /// <summary>Floor → Map4Build.Floor(겹침이면 null → throw), Wall → Map4Build.Wall. 순서 = 초안 §3-1 행 순서.</summary>
    private static readonly Box[] Boxes =
    {
        B("GEO_S4_Floor_Entry", Kind.Floor, -4f, -0.3f, 0f, 4f, 0f, 27.5f),         // [제안 초안 :35] S3 연결 통로와 z 0에서 맞닿음
        B("GEO_S4_Floor_Cyl", Kind.Floor, -34.5f, -0.3f, 27.5f, 34.5f, 0f, 96.5f),  // [제안 초안 :36]
        B("GEO_S4_Wall_EntryW", Kind.Wall, -4.5f, 0f, 0f, -4f, 6f, 28.8f),          // [제안 초안 :37] 원통 벽과 겹쳐 틈 0
        B("GEO_S4_Wall_EntryE", Kind.Wall, 4f, 0f, 0f, 4.5f, 6f, 28.8f),            // [제안 초안 :37]
        B("GEO_S4_Mast_SW", Kind.Wall, -6f, 8f, 58f, -5f, 16f, 59f),                // [제안 초안 :46] 레이저 기둥(고정 레이저 발사)
        B("GEO_S4_Mast_SE", Kind.Wall, 5f, 8f, 58f, 6f, 16f, 59f),                  // [제안 초안 :46]
        B("GEO_S4_Mast_NW", Kind.Wall, -6f, 8f, 65f, -5f, 16f, 66f),                // [제안 초안 :47] 레이저 기둥(받이)
        B("GEO_S4_Mast_NE", Kind.Wall, 5f, 8f, 65f, 6f, 16f, 66f),                  // [제안 초안 :47]
        B("GEO_S4_Wall_BubbleN", Kind.Wall, -4f, 8f, 66f, 4f, 17.7f, 66.5f),        // [판정 14]·[초안 :48] 버블 북쪽 벽 8→17.7
        B("GEO_S4_Mast_StairAim", Kind.Wall, -0.5f, 0f, 79f, 0.5f, 7.7f, 80f),      // [판정 16]·[초안 :49] 받침대 윗면 7.7
        B("GEO_S4_Bridge_Exit", Kind.Floor, -4f, 17.7f, 66f, 4f, 18f, 124f),        // [제안 초안 :50]·[계약 C2-4] 출구 윗면 18, z 124
        B("GEO_S4_Wall_RailW", Kind.Wall, -4.5f, 18f, 66f, -4f, 19.2f, 95.7f),      // [제안 초안 §3-1 :51 — M2R1 개정 95.7] 난간 1.2 · z 끝 96→95.7 = 출구 옆벽 z 시작과 맞닿음(겹침 0·틈 0)
        B("GEO_S4_Wall_RailE", Kind.Wall, 4f, 18f, 66f, 4.5f, 19.2f, 95.7f),        // [제안 초안 §3-1 :51 — M2R1 개정 95.7] · z 끝 95.7 같음
        B("GEO_S4_Wall_ExitW", Kind.Wall, -4.5f, 17.7f, 95.7f, -4f, 24f, 124f),     // [제안 초안 :52] 원통 밖 다리 옆벽
        B("GEO_S4_Wall_ExitE", Kind.Wall, 4f, 17.7f, 95.7f, 4.5f, 24f, 124f),       // [제안 초안 :52]
    };

    private const string ExitBridgeName = "GEO_S4_Bridge_Exit";
    private const string CylPiecePrefix = "GEO_S4_Wall_Cyl_";   // + 두 자리 nn(00~47) [R3 계약 C2-4]
    private const string FloorPrefix = "GEO_S4_Floor_";         // Refs.floors [2차판정 16]·[R3 계약 C1-1]
    private static readonly string[] ExpectedFloorNames = { "GEO_S4_Floor_Entry", "GEO_S4_Floor_Cyl" }; // [R3 계약 C1-1] 순서
    private const string RockName = "GEO_S4_Rock";
    private const string StairPrefix = "GEO_S4_Stair_";
    private const string WallPrefix = "GEO_S4_Wall_";
    private const string MastPrefix = "GEO_S4_Mast_";

    // ── 버블 트리거 자리 (초안 §3-2 :68 · §4-1 ①~③ · 판정 18 ②) ──
    private const string GimmickRootName = "S4_Gimmicks";   // [계약 C1-4]
    private const string BubbleName = "S4_Bubble";          // [계약 C2-4]
    private static readonly Vector3 BubbleMin = new Vector3(-4f, 8f, 58f);   // [판정 18 ②] 바닥 8 [제안](돌 높이)
    private static readonly Vector3 BubbleMax = new Vector3(4f, 18f, 66f);   // [판정 18 ②] 윗면 18 [확정](출구 높이)

    // ── 복귀 기준점 (초안 §3-2 :70-71 → 계약 C1-4 Refs 값, 초안 표 값 그대로) ──
    // 값 = 바닥 윗면(계단아래 = 원통 바닥 0, 버블입구 = 돌 윗면 8). 안전점 = 바닥 윗면 +0.1 [2차판정 15]의 +0.1은 S4_Wiring이 배치할 때
    // 더한다 — 여기서 더하지 않는다(두 번 더하기 방지) [R3 계약 C1-4 주석·R3-S4]. ValidateSafePoints가 y = 바닥 윗면(±0.02)을 확인한다.
    private static readonly Vector3 SpStairs = new Vector3(-3.24f, 0f, 49.93f);      // [제안 초안 :70] φ255 r12.5
    private static readonly Vector3 SpStairsB0 = new Vector3(-4.01f, 0f, 47.03f);    // [제안 초안 :70] r15.5
    private static readonly Vector3 SpStairsB1 = new Vector3(-4.79f, 0f, 44.13f);    // [제안 초안 :70] r18.5
    private static readonly Vector3 SpBubble = new Vector3(0f, 8f, 54.5f);           // [제안 초안 :71]
    private static readonly Vector3 SpBubbleB0 = new Vector3(-3f, 8f, 54.5f);        // [제안 초안 :71]
    private static readonly Vector3 SpBubbleB1 = new Vector3(3f, 8f, 54.5f);         // [제안 초안 :71]

    // ── 개수 (Refs 검증 — 계약 C1-4 주석) ──
    private const int ExpectedMasts = 5;
    private const int ExpectedBoxWalls = 7;

    private static Transform g; // Build 동안만 유효 — Generated 루트

    /// <summary>S4를 짓는다(R2 계약 C1-0 호출 순서 · C5 예외 표준). 전제(폭 72·길이 124·바닥 0·출구 18, ±0.01)와 다르면 아무것도
    /// 짓지 않고 false — 호출자가 빈 틀로 되돌아간다.</summary>
    public static bool Build(Transform generated, Map4Layout.SectorDef def)
    {
        // (1) 전제 검사 — 맨 앞. 아무것도 만들기 전에.
        if (!PreconditionsOk(def))
        {
            Debug.LogError(def == null
                ? "[S4_Builder] 전제 불일치 — SectorDef가 null이다. S4 구체화를 건너뛰고 빈 틀로 짓는다."
                : $"[S4_Builder] 전제 불일치 — 폭 {def.width}·길이 {def.length}·바닥 {def.floorHeight}·출구 {def.exitHeight}" +
                  $"(전제 {SectorWidth}·{SectorLength}·{FloorHeight}·{ExitHeight}, ±{PreconditionTolerance}). S4 구체화를 건너뛰고 빈 틀로 짓는다.");
            return false;
        }

        g = generated;
        var before = new HashSet<GameObject>(generated.gameObject.scene.GetRootGameObjects());
        S4_Refs refs = new S4_Refs { generated = generated };
        try
        {
            Map4Build.BeginSection();
            // 지형 → (팀 기믹: [2차판정 4·R3 계약 C1-1] 기믹 목록 검증 = Wire 몫) → Refs 채우기 → Refs 검증. Map4Build가 null을 돌려주면 즉시 throw.
            BuildBoxes(refs, def);
            BuildCylinderWall(refs);
            BuildRock(refs);
            BuildSpiralStairs(refs);
            BuildBubbleTriggerSlot(refs);
            FillRefPoints(refs);
            ValidateTerrainRefs(refs);
            Debug.Log($"[S4_Builder] 섹터4 지형 완료 — 상자 {Boxes.Length} · 원통 벽 (나) 볼록 조각 {CylSegments}(7.5°) · 바닥 Refs {refs.floors.Count} · " +
                      $"돌 1 · 계단 {StairSegs.Length}구간/쐐기 {refs.stairs.Count - StairSegs.Length} · 버블 트리거 자리 1(팀 컴포넌트 없음).");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[S4_Builder] 섹터4 구체화 실패 — Generated를 비우고 빈 틀로 되돌린다: {e.Message}\n{e.StackTrace}");
            for (int i = generated.childCount - 1; i >= 0; i--) Object.DestroyImmediate(generated.GetChild(i).gameObject);
            foreach (GameObject r in generated.gameObject.scene.GetRootGameObjects())
                if (!before.Contains(r)) Object.DestroyImmediate(r);   // 이번 Build가 팀 메뉴로 만든 루트 잔류물
            g = null; return false;
        }
        try { S4_Wiring.Wire(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S4_Builder] Wire 예외 — 지형 유지: {e}"); }
        try { S4_Dress.Apply(generated, refs); } catch (System.Exception e) { Debug.LogError($"[S4_Builder] Dress 예외 — 지형 유지: {e}"); }
        ReportStrayRoots();
        g = null; return true;
    }

    private static bool PreconditionsOk(Map4Layout.SectorDef def) =>
        def != null
        && Mathf.Abs(def.width - SectorWidth) <= PreconditionTolerance
        && Mathf.Abs(def.length - SectorLength) <= PreconditionTolerance
        && Mathf.Abs(def.floorHeight - FloorHeight) <= PreconditionTolerance
        && Mathf.Abs(def.exitHeight - ExitHeight) <= PreconditionTolerance;

    // ───────────────────────── 상자 지형 ─────────────────────────

    /// <summary>상자 15개. Floor는 z ∈ [0, def.length] 안(계약 C5), 출구 다리 윗면 = exitHeight − floorHeight이고 z 끝 = def.length(±0.02) —
    /// 공용 연결 통로(z 124~136, 윗면 18)와 턱 0·틈 0 [계약 C2-4]. 어긋나면 throw.</summary>
    private static void BuildBoxes(S4_Refs refs, Map4Layout.SectorDef def)
    {
        CheckBoxPairOverlaps();   // 아무것도 짓기 전에 — 상자 쌍 부피 겹침(관통) 0 [M2R1]
        float exitTop = def.exitHeight - def.floorHeight;
        foreach (Box b in Boxes)
        {
            if (b.kind == Kind.Floor && (b.min.z < -SeamTolerance || b.max.z > def.length + SeamTolerance))
                throw new System.Exception($"[S4_Builder] Floor '{b.name}' z {b.min.z}~{b.max.z}가 섹터 [0, {def.length}] 밖이다(계약 C5).");

            Transform t = Solid(b.kind == Kind.Floor ? Map4Build.Floor(g, b.min, b.max) : Map4Build.Wall(g, b.min, b.max), b.name);

            if (b.name == ExitBridgeName)
            {
                if (Mathf.Abs(b.max.y - exitTop) > SeamTolerance || Mathf.Abs(b.max.z - def.length) > SeamTolerance
                    || b.min.x > -def.connectorWidthToNext / 2f + SeamTolerance || b.max.x < def.connectorWidthToNext / 2f - SeamTolerance)
                    throw new System.Exception($"[S4_Builder] 출구 다리 {b.min}~{b.max}가 출구(윗면 {exitTop}, z {def.length}, 폭 {def.connectorWidthToNext})와 " +
                                               $"±{SeamTolerance} 밖이다(계약 C2-4 턱 0·틈 0).");
                refs.exitBridge = t;
            }
            else if (b.kind == Kind.Floor && b.name.StartsWith(FloorPrefix, System.StringComparison.Ordinal)) refs.floors.Add(t); // 표 순서 = Entry → Cyl [2차판정 16]
            else if (b.name.StartsWith(WallPrefix, System.StringComparison.Ordinal)) refs.walls.Add(t);
            else if (b.name.StartsWith(MastPrefix, System.StringComparison.Ordinal)) refs.masts.Add(t);
        }
    }

    /// <summary>Boxes 표의 모든 쌍(Floor·Wall 구분 없이 105쌍)에 대해 AABB 관통 깊이 = 세 축 겹침 길이 중 최솟값을 구해, 세 축 모두
    /// 겹치고 그 값이 SeamTolerance(0.02)를 넘으면 throw한다 — 맞닿음(깊이 0 ±0.02)만 허용 [M2R1 지적: F1-3 겹침 0 · 공통규칙 §3 ① 관통
    /// ≥0.10 = 0 · 합격 기준 Audit 관통 0]. Map4Audit의 관통 기준(0.10)보다 엄격하게 0.02로 둔다. 원통 벽·돌·계단(메시)과 상자의
    /// 겹침은 여기서 보지 않는다(Audit 몫 — 메시는 AABB로 판정할 수 없다).</summary>
    private static void CheckBoxPairOverlaps()
    {
        for (int i = 0; i < Boxes.Length; i++)
        {
            for (int j = i + 1; j < Boxes.Length; j++)
            {
                Box a = Boxes[i], b = Boxes[j];
                float dx = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                float dy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
                float dz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                float depth = Mathf.Min(dx, Mathf.Min(dy, dz));
                if (depth > SeamTolerance)
                    throw new System.Exception($"[S4_Builder] 상자 '{a.name}'↔'{b.name}' 부피 겹침 {depth:F3}U(x {dx:F3}·y {dy:F3}·z {dz:F3}) — " +
                                               $"맞닿음(±{SeamTolerance})만 허용한다(F1-3 겹침 0·Audit 관통 0).");
            }
        }
    }

    // ───────────────────────── 원통 벽 ─────────────────────────

    /// <summary>환형 벽 r 34~34.5, y 0~26, 축 (0, 62) — 7.5° 볼록 조각 48개 GEO_S4_Wall_Cyl_00~_47 [2차판정 15·16]. 정규 꼭짓점 φ = k·7.5°.
    /// 개구부 경계 x = ±4.5 평면에 가장 가까운 정규 꼭짓점 4개를 그 평면 위 점(안쪽 r 34·바깥 r 34.5 원 위)으로 바꾼다 → 개구부 옆면이
    /// x = ±4.5에 정확히 놓인다(check.py WALL_OPENINGS와 같은 절단면). 조각 높이: 남 개구부(두 남쪽 절단점 사이, 조각 35·36) 6~26,
    /// 북 개구부(두 북쪽 절단점 사이, 조각 11·12) 0~17.7, 나머지 0~26. 조각마다 닫힌 6면 볼록 MeshCollider — 이웃 조각과 같은 꼭짓점
    /// 배열 원소를 쓰므로 이음매 틈 0·겹침 0(비트 단위 공유). 짓기 전에 조각 평면 사각형이 볼록인지(겹침 0의 전제) 검사해 어긋나면 throw.</summary>
    private static void BuildCylinderWall(S4_Refs refs)
    {
        float rOut = CylInnerR + CylWallT;
        float step = 360f / CylSegments;
        Vector2[] inner = new Vector2[CylSegments], outer = new Vector2[CylSegments];
        for (int k = 0; k < CylSegments; k++)
        {
            inner[k] = PolarXZ(CylInnerR, k * step);
            outer[k] = PolarXZ(rOut, k * step);
        }

        // 개구부 절단점 — (x, 남/북). 안쪽 원 위 절단점의 각도에 가장 가까운 정규 꼭짓점을 바꾼다.
        int kSW = CutVertex(inner, outer, -OpeningHalfX, false, rOut, step);
        int kSE = CutVertex(inner, outer, OpeningHalfX, false, rOut, step);
        int kNE = CutVertex(inner, outer, OpeningHalfX, true, rOut, step);
        int kNW = CutVertex(inner, outer, -OpeningHalfX, true, rOut, step);
        if (!(kNE < kNW && kNW < kSW && kSW < kSE) || kNW - kNE != ExpectedOpeningPieces || kSE - kSW != ExpectedOpeningPieces)
            throw new System.Exception($"[S4_Builder] 원통 벽 개구부 절단 꼭짓점이 어긋났다 — NE {kNE}·NW {kNW}·SW {kSW}·SE {kSE}" +
                                       $"(순서 NE < NW < SW < SE, 개구부마다 조각 {ExpectedOpeningPieces}개 기대).");

        float[] y0 = new float[CylSegments], y1 = new float[CylSegments];
        for (int k = 0; k < CylSegments; k++)
        {
            if (k >= kSW && k < kSE) { y0[k] = SouthOpeningTop; y1[k] = CylWallTop; }        // 남 개구부 위(인방)
            else if (k >= kNE && k < kNW) { y0[k] = 0f; y1[k] = NorthOpeningBottom; }       // 북 개구부 아래(다리 받침)
            else { y0[k] = 0f; y1[k] = CylWallTop; }
        }

        for (int k = 0; k < CylSegments; k++) CheckPieceConvex(inner, outer, k);

        for (int k = 0; k < CylSegments; k++)
        {
            MeshAcc m = new MeshAcc();
            AddWallPiece(m, inner, outer, k, y0, y1);
            refs.walls.Add(MeshSolid(g, $"{CylPiecePrefix}{k:00}", m.ToMesh($"S4_WallCyl_{k:00}"), true, "Wall"));
        }
    }

    /// <summary>조각 k의 평면 사각형(안쪽 k → 안쪽 k+1 → 바깥 k+1 → 바깥 k)이 볼록이고 넓이가 0이 아닌지 — 네 모서리 외적 부호가 모두 같아야 한다.
    /// 볼록이면 볼록 MeshCollider 껍질 = 조각 그대로라 이웃과 공유 끝면에서 겹침 0이다. 어긋나면 throw.</summary>
    private static void CheckPieceConvex(Vector2[] inner, Vector2[] outer, int k)
    {
        int b = (k + 1) % inner.Length;
        Vector2[] q = { inner[k], inner[b], outer[b], outer[k] };
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector2 e0 = q[(i + 1) % 4] - q[i], e1 = q[(i + 2) % 4] - q[(i + 1) % 4];
            float cross = e0.x * e1.y - e0.y * e1.x;
            int s = cross > 1e-6f ? 1 : (cross < -1e-6f ? -1 : 0);
            if (s == 0 || (sign != 0 && s != sign))
                throw new System.Exception($"[S4_Builder] 원통 벽 조각 {CylPiecePrefix}{k:00} 평면 사각형이 볼록이 아니다(모서리 {i} 외적 {cross:G4}).");
            sign = s;
        }
    }

    /// <summary>개구부 경계 평면 x = xc와 원이 만나는 점(북 = z &gt; 축, 남 = z &lt; 축)으로 가장 가까운 정규 꼭짓점을 바꾸고 그 번호를 돌려준다.</summary>
    private static int CutVertex(Vector2[] inner, Vector2[] outer, float xc, bool north, float rOut, float step)
    {
        float sign = north ? 1f : -1f;
        Vector2 pin = new Vector2(xc, CylAxisZ + sign * Mathf.Sqrt(CylInnerR * CylInnerR - xc * xc));
        Vector2 pout = new Vector2(xc, CylAxisZ + sign * Mathf.Sqrt(rOut * rOut - xc * xc));
        float phi = Mathf.Atan2(pin.y - CylAxisZ, pin.x - CylAxisX) * Mathf.Rad2Deg;
        if (phi < 0f) phi += 360f;
        int k = Mathf.RoundToInt(phi / step) % CylSegments;
        inner[k] = pin;
        outer[k] = pout;
        return k;
    }

    /// <summary>원통 벽 조각 k(꼭짓점 k → k+1) 한 개를 m에 더한다. 안쪽·바깥·윗면·아랫면은 늘 내고, 끝면 a(꼭짓점 k)·b(꼭짓점 k+1)는
    /// 자기 높이에서 이웃 조각(k−1·k+1)이 덮는 부분을 뺀 드러난 부분만 낸다(렌더링용) — 개구부 옆면(남 y 0~6, 북 y 17.7~26)만 남는다.
    /// 안쪽·바깥 면이 8모서리를 모두 가지므로 볼록 껍질(콜라이더)은 닫힌 6면이고, 가려진 끝면은 보이지 않는다.</summary>
    private static void AddWallPiece(MeshAcc m, Vector2[] inner, Vector2[] outer, int k, float[] y0, float[] y1)
    {
        int n = inner.Length, b = (k + 1) % n, prev = (k + n - 1) % n;
        Vector2 ia = inner[k], ib = inner[b], ob = outer[b], oa = outer[k];
        Vector2 c = (ia + ib + ob + oa) * 0.25f;
        m.Side(ia, ib, y0[k], y1[k], y0[k], y1[k], c);   // 안쪽 면(축 쪽)
        m.Side(ob, oa, y0[k], y1[k], y0[k], y1[k], c);   // 바깥 면
        m.FlatCap(new[] { ia, ib, ob, oa }, y1[k], true);
        m.FlatCap(new[] { ia, ib, ob, oa }, y0[k], false);
        EndFace(m, oa, ia, y0[k], y1[k], y0[prev], y1[prev], c);   // 끝면 a — 이전 조각과 맞닿는 면
        EndFace(m, ib, ob, y0[k], y1[k], y0[b], y1[b], c);         // 끝면 b — 다음 조각과 맞닿는 면
    }

    /// <summary>끝면 p→q, 자기 높이 [y0, y1]에서 이웃 높이 [n0, n1]이 덮는 부분을 뺀 나머지(아래·위 최대 2조각)만 낸다.</summary>
    private static void EndFace(MeshAcc m, Vector2 p, Vector2 q, float y0, float y1, float n0, float n1, Vector2 c)
    {
        if (n1 <= y0 || n0 >= y1) { m.Side(p, q, y0, y1, y0, y1, c); return; }     // 겹침 없음 → 전체
        if (n0 > y0) m.Side(p, q, y0, n0, y0, n0, c);                                // 아래 드러난 부분
        if (n1 < y1) m.Side(p, q, n1, y1, n1, y1, c);                                // 위 드러난 부분
    }

    // ───────────────────────── 큰 돌 ─────────────────────────

    /// <summary>48각 기둥 y 0~8, 외접원 r 10, 꼭짓점 φ = k·7.5°. 볼록 MeshCollider 1개 [제안 초안 §3-1-1].</summary>
    private static void BuildRock(S4_Refs refs)
    {
        Vector2[] ring = new Vector2[RockSides];
        for (int k = 0; k < RockSides; k++) ring[k] = PolarXZ(RockR, k * PolyStepDeg);
        MeshAcc m = new MeshAcc();
        Vector2 c = new Vector2(CylAxisX, CylAxisZ);
        for (int k = 0; k < RockSides; k++)
            m.Side(ring[k], ring[(k + 1) % RockSides], 0f, RockTop, 0f, RockTop, c);
        m.FlatCap(ring, RockTop, true);
        m.FlatCap(ring, 0f, false);
        refs.rock = MeshSolid(g, RockName, m.ToMesh("S4_Rock"), true, "Floor");
    }

    // ───────────────────────── 나선 계단 ─────────────────────────

    /// <summary>6구간 그룹 + 3.75° 볼록 쐐기 88개. 쐐기 j: φa → φb = φa + 3.75. 안쪽 꼭짓점은 φ가 7.5 배수면 돌 꼭짓점(r 10), 아니면 그 변의
    /// 가운데점(r 10·cos3.75°) → 돌 옆면과 같은 평면(틈 0·겹침 0). 윗면은 구간 안에서 φ에 비례, 아래 y 0. 짓기 전에 구간 이음(윗면 연속)과
    /// 시작 0(= 원통 바닥)·끝 8(= 돌 윗면), 쐐기마다 안쪽·가운데·바깥 경사 ≤ Map4Build.RampMaxAngleDeg를 검사해 어긋나면 throw.</summary>
    private static void BuildSpiralStairs(S4_Refs refs)
    {
        if (Mathf.Abs(StairSegs[0].h0 - FloorHeight) > SeamTolerance || Mathf.Abs(StairSegs[StairSegs.Length - 1].h1 - RockTop) > SeamTolerance)
            throw new System.Exception($"[S4_Builder] 계단 시작 윗면 {StairSegs[0].h0}(원통 바닥 0)·끝 윗면 {StairSegs[StairSegs.Length - 1].h1}(돌 윗면 {RockTop})이 어긋난다.");
        for (int i = 1; i < StairSegs.Length; i++)
            if (Mathf.Abs(StairSegs[i].phi0 - StairSegs[i - 1].phi1) > 1e-4f || Mathf.Abs(StairSegs[i].h0 - StairSegs[i - 1].h1) > SeamTolerance)
                throw new System.Exception($"[S4_Builder] 계단 구간 {StairSegs[i - 1].id}→{StairSegs[i].id} 이음이 어긋난다(φ {StairSegs[i - 1].phi1}/{StairSegs[i].phi0}, 윗면 {StairSegs[i - 1].h1}/{StairSegs[i].h0}).");

        float innerMid = StairInR * Mathf.Cos(0.5f * PolyStepDeg * Mathf.Deg2Rad);
        int total = 0;
        foreach (Seg s in StairSegs)
        {
            float spanCount = (s.phi1 - s.phi0) / WedgeStepDeg;
            int count = Mathf.RoundToInt(spanCount);
            if (count <= 0 || Mathf.Abs(spanCount - count) > 1e-4f)
                throw new System.Exception($"[S4_Builder] 계단 구간 {s.id} φ {s.phi0}~{s.phi1}가 {WedgeStepDeg}° 배수가 아니다.");

            GameObject grp = new GameObject(StairPrefix + s.id);
            grp.transform.SetParent(g, false);
            refs.stairs.Add(grp.transform);

            for (int j = 0; j < count; j++)
            {
                float pa = s.phi0 + j * WedgeStepDeg, pb = pa + WedgeStepDeg;
                float ha = Tread(s, pa), hb = Tread(s, pb);
                float ra = IsPolyVertex(pa) ? StairInR : innerMid, rb = IsPolyVertex(pb) ? StairInR : innerMid;
                Vector2 ia = PolarXZ(ra, pa), ib = PolarXZ(rb, pb), ob = PolarXZ(StairOutR, pb), oa = PolarXZ(StairOutR, pa);

                string name = $"{StairPrefix}{s.id}_W{j:00}";
                CheckWedgeSlope(name, ia, ib, ha, hb, "안쪽");
                CheckWedgeSlope(name, PolarXZ(0.5f * (StairInR + StairOutR), pa), PolarXZ(0.5f * (StairInR + StairOutR), pb), ha, hb, "가운데");
                CheckWedgeSlope(name, oa, ob, ha, hb, "바깥");

                MeshAcc m = new MeshAcc();
                Vector2 c = (ia + ib + ob + oa) * 0.25f;
                Vector3 IA = V(ia, ha), IB = V(ib, hb), OB = V(ob, hb), OA = V(oa, ha);
                m.TopQuadUpperHull(IA, IB, OB, OA);
                m.FlatCap(new[] { ia, ib, ob, oa }, 0f, false);
                m.Side(ia, ib, 0f, ha, 0f, hb, c);   // 안쪽 면(돌 옆면 위)
                m.Side(ib, ob, 0f, hb, 0f, hb, c);   // 끝면 b(φb 반지름 방향 — 다음 쐐기와 같은 면)
                m.Side(ob, oa, 0f, hb, 0f, ha, c);   // 바깥 면
                m.Side(oa, ia, 0f, ha, 0f, ha, c);   // 끝면 a(φa 반지름 방향)
                refs.stairs.Add(MeshSolid(grp.transform, name, m.ToMesh($"S4_Stair_{s.id}_W{j:00}"), true, "Floor"));
                total++;
            }
        }
        if (total != ExpectedWedges)
            throw new System.Exception($"[S4_Builder] 계단 쐐기 {total}개 — 기대 {ExpectedWedges}개(330° / {WedgeStepDeg}°).");
    }

    private static float Tread(Seg s, float phi) => s.h0 + (s.h1 - s.h0) * (phi - s.phi0) / (s.phi1 - s.phi0);

    private static bool IsPolyVertex(float phi)
    {
        float q = phi / PolyStepDeg;
        return Mathf.Abs(q - Mathf.Round(q)) < 1e-4f;
    }

    private static void CheckWedgeSlope(string name, Vector2 a, Vector2 b, float ha, float hb, string where)
    {
        float run = Vector2.Distance(a, b);
        float deg = Mathf.Atan2(Mathf.Abs(hb - ha), run) * Mathf.Rad2Deg;
        if (run <= 0f || deg > Map4Build.RampMaxAngleDeg)
            throw new System.Exception($"[S4_Builder] 쐐기 {name} {where} 경사 {deg:F2}°가 한도 {Map4Build.RampMaxAngleDeg}°를 넘는다(00_기반 결정5).");
    }

    // ───────────────────────── 버블 트리거 자리 ─────────────────────────

    /// <summary>S4_Gimmicks 빈 그룹 + S4_Bubble(우리 GameObject) — 원점 (0, 13, 62)·회전 0·스케일 1, BoxCollider center 0·size (8, 10, 8),
    /// isTrigger = true 직접 [판정 18 ②, 초안 §4-1 ①~③]. Rigidbody 없음. 팀 ZeroGravityBubble AddComponent(④⑤)는 이 과제 범위 밖이다.
    /// 붙인 뒤 콜라이더가 BoxCollider 1개뿐이고 트리거인지 검사 — 아니면 throw(계약 C5 부분 생성 금지).</summary>
    private static void BuildBubbleTriggerSlot(S4_Refs refs)
    {
        GameObject root = new GameObject(GimmickRootName);
        root.transform.SetParent(g, false);
        refs.gimmickRoot = root.transform;

        GameObject bubble = new GameObject(BubbleName);
        bubble.transform.SetParent(root.transform, false);
        bubble.transform.localPosition = (BubbleMin + BubbleMax) * 0.5f;
        bubble.transform.localRotation = Quaternion.identity;
        bubble.transform.localScale = Vector3.one;
        BoxCollider box = bubble.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = BubbleMax - BubbleMin;
        box.isTrigger = true;

        Collider[] cols = bubble.GetComponents<Collider>();
        if (cols.Length != 1 || !(cols[0] is BoxCollider) || !cols[0].isTrigger || bubble.GetComponent<Rigidbody>() != null)
            throw new System.Exception($"[S4_Builder] {BubbleName} 콜라이더 구성이 어긋났다 — 콜라이더 {cols.Length}개(기대 BoxCollider 1개, isTrigger, Rigidbody 없음).");
        refs.bubbleTrigger = MinMax(BubbleMin, BubbleMax);
    }

    // ───────────────────────── Refs ─────────────────────────

    /// <summary>복귀 기준점(초안 §3-2 표 값). 나머지 치수(cylAxisXZ·cylInnerR·cylWallT·cylWallTop·rockR·rockTop·bridgeTop·
    /// checkpointStartLocal)는 계약 C1-4 기본값 그대로 두고 ValidateTerrainRefs에서 이 빌더의 기하와 같은지만 본다.</summary>
    private static void FillRefPoints(S4_Refs refs)
    {
        refs.spStairsLocal = SpStairs;
        refs.spStairsBackups = new[] { SpStairsB0, SpStairsB1 };
        refs.spBubbleLocal = SpBubble;
        refs.spBubbleBackups = new[] { SpBubbleB0, SpBubbleB1 };
    }

    /// <summary>지형 Refs 검증(R3 계약 C1-1의 빌더 몫 — 트리거 자리·floors·지형) — 개수·순서·null·치수·안전점. 기믹 목록(bubble·rockSpawners·
    /// fixedLasers·aimLasers)은 Wire가 만들고 검증한다(Build 중 비어도 됨 [2차판정 4]). 실패면 throw → C5 표준으로 false.
    /// walls = 상자 벽 7 + 원통 조각 48 = 55이고 원통 조각은 walls 안에서 이름 GEO_S4_Wall_Cyl_00~_47이 연속이어야 한다.</summary>
    private static void ValidateTerrainRefs(S4_Refs refs)
    {
        int expectedWalls = ExpectedBoxWalls + CylSegments;   // [계산] 7 + 48 = 55
        int expectedStairs = StairSegs.Length + ExpectedWedges;

        // floors [2차판정 16 · R3 계약 C1-1] — 개수 2·순서 Entry → Cyl·null 없음(출구 다리는 exitBridge 몫).
        if (refs.floors == null || refs.floors.Count != ExpectedFloorNames.Length || refs.floors.Contains(null))
            throw new System.Exception($"[S4_Builder] Refs.floors가 계약과 다르다 — {(refs.floors == null ? "null" : refs.floors.Count.ToString())}개" +
                                       $"(기대 {ExpectedFloorNames.Length}개: {string.Join(", ", ExpectedFloorNames)}), 또는 null 포함.");
        for (int i = 0; i < ExpectedFloorNames.Length; i++)
            if (refs.floors[i].name != ExpectedFloorNames[i])
                throw new System.Exception($"[S4_Builder] Refs.floors[{i}] '{refs.floors[i].name}' — 기대 '{ExpectedFloorNames[i]}'(순서 Entry → Cyl, R3 계약 C1-1).");

        // 원통 조각 이름 00~47 연속(walls 안 원통 조각만, 만든 순서 그대로).
        int cylIdx = 0;
        foreach (Transform w in refs.walls)
        {
            if (w == null || !w.name.StartsWith(CylPiecePrefix, System.StringComparison.Ordinal)) continue;
            string expect = $"{CylPiecePrefix}{cylIdx:00}";
            if (w.name != expect)
                throw new System.Exception($"[S4_Builder] 원통 조각 이름 '{w.name}' — 기대 '{expect}'(00~{CylSegments - 1:00} 연속, R3 계약 C2-4).");
            cylIdx++;
        }
        if (cylIdx != CylSegments)
            throw new System.Exception($"[S4_Builder] 원통 조각 {cylIdx}개 — 기대 {CylSegments}개(48각 7.5° [2차판정 16]).");

        if (refs.walls.Count != expectedWalls || refs.masts.Count != ExpectedMasts || refs.stairs.Count != expectedStairs
            || refs.walls.Contains(null) || refs.masts.Contains(null) || refs.stairs.Contains(null)
            || refs.rock == null || refs.exitBridge == null || refs.gimmickRoot == null)
            throw new System.Exception($"[S4_Builder] 지형 Refs가 계약과 다르다 — walls {refs.walls.Count}/{expectedWalls}, masts {refs.masts.Count}/{ExpectedMasts}, " +
                                       $"stairs {refs.stairs.Count}/{expectedStairs}, rock {(refs.rock != null)}, exitBridge {(refs.exitBridge != null)}, " +
                                       $"gimmickRoot {(refs.gimmickRoot != null)}, 또는 null 포함.");

        bool dimsOk = Vector2.Distance(refs.cylAxisXZ, new Vector2(CylAxisX, CylAxisZ)) <= SeamTolerance
            && Mathf.Abs(refs.cylInnerR - CylInnerR) <= SeamTolerance && Mathf.Abs(refs.cylWallT - CylWallT) <= SeamTolerance
            && Mathf.Abs(refs.cylWallTop - CylWallTop) <= SeamTolerance && Mathf.Abs(refs.rockR - RockR) <= SeamTolerance
            && Mathf.Abs(refs.rockTop - RockTop) <= SeamTolerance && Mathf.Abs(refs.bridgeTop - (ExitHeight - FloorHeight)) <= SeamTolerance;
        if (!dimsOk)
            throw new System.Exception("[S4_Builder] S4_Refs 치수 기본값(계약 C1-4)이 이 빌더의 기하와 다르다 — 축·반지름·벽·돌·다리 값을 확인.");
        ValidateSafePoints(refs);
    }

    /// <summary>안전점 Refs 값 = 바닥 윗면(+0.1 없음 — W가 더한다 [2차판정 15 · R3 계약 C1-4 주석]). 계단아래 3점은 y = 원통 바닥 윗면
    /// (FloorHeight)이고 평면 위치가 원통 안(r &lt; 안쪽 r)·돌 밖(r &gt; 돌 r)·계단 쐐기 밖이어야 한다. 버블입구 3점은 y = 돌 윗면이고
    /// 돌 48각 안(r ≤ 돌 r·cos3.75° = 변심거리)이어야 한다. 허용 ±0.02(SeamTolerance). 어긋나면 throw.</summary>
    private static void ValidateSafePoints(S4_Refs refs)
    {
        if (refs.spStairsBackups == null || refs.spStairsBackups.Length != 2 || refs.spBubbleBackups == null || refs.spBubbleBackups.Length != 2)
            throw new System.Exception("[S4_Builder] 안전점 예비점이 2개씩이 아니다(계약 C1-4).");

        Vector3[] stairsPts = { refs.spStairsLocal, refs.spStairsBackups[0], refs.spStairsBackups[1] };
        Vector3[] bubblePts = { refs.spBubbleLocal, refs.spBubbleBackups[0], refs.spBubbleBackups[1] };
        float rockApothem = RockR * Mathf.Cos(0.5f * PolyStepDeg * Mathf.Deg2Rad);
        foreach (Vector3 p in stairsPts)
        {
            float r = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(CylAxisX, CylAxisZ));
            if (Mathf.Abs(p.y - FloorHeight) > SeamTolerance || r <= RockR || r >= CylInnerR || OverStairWedge(p))
                throw new System.Exception($"[S4_Builder] 계단아래 안전점 {p}(r {r:F2})가 원통 바닥 윗면({FloorHeight}) 위가 아니다 — " +
                                           $"값 = 바닥 윗면, +0.1은 W 몫 [2차판정 15].");
        }
        foreach (Vector3 p in bubblePts)
        {
            float r = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(CylAxisX, CylAxisZ));
            if (Mathf.Abs(p.y - RockTop) > SeamTolerance || r > rockApothem)
                throw new System.Exception($"[S4_Builder] 버블입구 안전점 {p}(r {r:F2})가 돌 윗면({RockTop}, r ≤ {rockApothem:F2}) 위가 아니다 — " +
                                           $"값 = 바닥 윗면, +0.1은 W 몫 [2차판정 15].");
        }
    }

    /// <summary>평면 위치가 나선 계단 쐐기(r 돌~15, φ 270→600 = 270~360·0~240) 위인지.</summary>
    private static bool OverStairWedge(Vector3 p)
    {
        float dx = p.x - CylAxisX, dz = p.z - CylAxisZ;
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        if (r < StairInR - SeamTolerance || r > StairOutR + SeamTolerance) return false;
        float phi = Mathf.Atan2(dz, dx) * Mathf.Rad2Deg;
        if (phi < 0f) phi += 360f;
        float first = StairSegs[0].phi0, last = StairSegs[StairSegs.Length - 1].phi1;   // 270, 600
        return (phi >= first && phi <= last) || (phi + 360f >= first && phi + 360f <= last);
    }

    // ───────────────────────── 점검 ─────────────────────────

    /// <summary>씬 루트에 Generated 밖 오브젝트가 남았는지 확인(S5_Builder와 같은 점검) — 남으면 재생성 때 중복된다.</summary>
    private static void ReportStrayRoots()
    {
        GameObject sectorRoot = g.root.gameObject;
        foreach (GameObject root in g.gameObject.scene.GetRootGameObjects())
            if (root != sectorRoot)
                Debug.LogError($"[S4_Builder] 씬 루트에 남은 오브젝트 '{root.name}' — Generated 아래로 옮기지 못했다(재생성 시 중복).");
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>원통 축 기준 극좌표 → 섹터 로컬 (x, z). φ는 360으로 나눈 나머지로 계산해 풀어 쓴 각(375° 등)과 돌 꼭짓점 각(15°)이
    /// 같은 값을 내게 한다(계단 안쪽 꼭짓점 = 돌 꼭짓점, 비트 단위 공유).</summary>
    private static Vector2 PolarXZ(float r, float phiDeg)
    {
        double a = (phiDeg % 360f) * System.Math.PI / 180.0;
        return new Vector2((float)(CylAxisX + r * System.Math.Cos(a)), (float)(CylAxisZ + r * System.Math.Sin(a)));
    }

    private static Vector3 V(Vector2 xz, float y) => new Vector3(xz.x, y, xz.y);

    private static Bounds MinMax(Vector3 min, Vector3 max)
    {
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    /// <summary>Map4Build.Floor/Wall이 돌려준 상자에 S4 이름을 붙이고 시각물 자식 "Visual"을 VIS_ 접두사로 바꾼다(Map4Build 수정 없음).
    /// 생성이 거부돼 null이면 즉시 중단(부분 지형 방지 — Build가 Generated를 비운다).</summary>
    private static Transform Solid(Transform t, string name)
    {
        if (t == null) throw new System.Exception($"[S4_Builder] 지형 생성 실패: {name} — 위 Map4Build 로그 확인.");
        t.name = name;
        Transform vis = t.Find("Visual");
        if (vis != null) vis.name = "VIS_" + name.Substring("GEO_".Length);
        return t;
    }

    /// <summary>코드 생성 메시 고체 — GEO_ 오브젝트(localPosition 0·회전 0)에 MeshCollider, 자식 VIS_&lt;GEO_ 뗀 이름&gt;에 MeshFilter·MeshRenderer
    /// (콜라이더 없음, Map4Build.GetMaterial 팔레트 — 톤은 S4_Dress 몫). 메시가 비었거나 콜라이더에 메시가 안 붙으면 throw.</summary>
    private static Transform MeshSolid(Transform parent, string name, Mesh mesh, bool convex, string paletteName)
    {
        if (mesh == null || mesh.vertexCount < 4)
            throw new System.Exception($"[S4_Builder] 메시 생성 실패: {name}(정점 {(mesh == null ? 0 : mesh.vertexCount)}).");
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        MeshCollider col = go.AddComponent<MeshCollider>();
        col.convex = convex;
        col.sharedMesh = mesh;
        if (col.sharedMesh == null) throw new System.Exception($"[S4_Builder] MeshCollider에 메시를 붙이지 못했다: {name}.");

        GameObject vis = new GameObject("VIS_" + name.Substring("GEO_".Length));
        vis.transform.SetParent(go.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = mesh;
        vis.AddComponent<MeshRenderer>().sharedMaterial = Map4Build.GetMaterial(paletteName);
        return go.transform;
    }

    /// <summary>면마다 정점을 따로 두는 메시 누적기(플랫 셰이딩, Map4Build.Ramp 3차 반려 A와 같음). 삼각형 감김은 바깥 방향 기준으로
    /// 스스로 맞춘다(Unity 앞면 = cross(b−a, c−a) 방향). 넓이 0 삼각형은 버린다(계단 첫 쐐기의 높이 0 끝면 등).</summary>
    private sealed class MeshAcc
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<int> tris = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>수직 옆면 p→q: p쪽 높이 [pY0, pY1], q쪽 높이 [qY0, qY1]. 바깥 = 평면 중심 c에서 변 가운데로 향하는 수평 방향.</summary>
        public void Side(Vector2 p, Vector2 q, float pY0, float pY1, float qY0, float qY1, Vector2 c)
        {
            Vector2 mid = (p + q) * 0.5f;
            Vector2 d = mid - c;
            Vector3 outward = new Vector3(d.x, 0f, d.y);
            Vector3 p0 = new Vector3(p.x, pY0, p.y), p1 = new Vector3(p.x, pY1, p.y);
            Vector3 q0 = new Vector3(q.x, qY0, q.y), q1 = new Vector3(q.x, qY1, q.y);
            Tri(p0, q0, q1, outward);
            Tri(p0, q1, p1, outward);
        }

        /// <summary>볼록 다각형 뚜껑(높이 y 평면) — 첫 꼭짓점 부채꼴. up = true면 위를, false면 아래를 바깥으로.</summary>
        public void FlatCap(Vector2[] ring, float y, bool up)
        {
            Vector3 outward = up ? Vector3.up : Vector3.down;
            for (int i = 1; i + 1 < ring.Length; i++)
                Tri(new Vector3(ring[0].x, y, ring[0].y), new Vector3(ring[i].x, y, ring[i].y), new Vector3(ring[i + 1].x, y, ring[i + 1].y), outward);
        }

        /// <summary>네 점 윗면(A-B-C-D 둘레 순서)을 볼록 껍질의 윗면과 같은 대각선으로 나눈다 — D가 평면 ABC 아래(또는 위)면 대각선 AC,
        /// 아니면 BD. 렌더링 면 = 볼록 MeshCollider 윗면.</summary>
        public void TopQuadUpperHull(Vector3 A, Vector3 B, Vector3 C, Vector3 D)
        {
            Vector3 n = Vector3.Cross(B - A, C - A);
            if (n.y < 0f) n = -n;
            if (Vector3.Dot(n, D - A) <= 0f) { Tri(A, B, C, Vector3.up); Tri(A, C, D, Vector3.up); }
            else { Tri(A, B, D, Vector3.up); Tri(B, C, D, Vector3.up); }
        }

        public Mesh ToMesh(string meshName)
        {
            Mesh mesh = new Mesh { name = meshName };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

// ───────────────────────── S4_Refs (R3 계약 C1-4 — 글자 그대로) ─────────────────────────
// R3 계약 R3-2는 S4_Refs를 이 파일 끝에 두고 소유 S4-B로 적었다. 아래 클래스 선언은 진행/지시서/R3/_계약.md C1-4 코드 블록의 using·#if·#endif
// 줄을 뺀 나머지를 파이썬으로 바이트 그대로 옮긴 것이다(재타이핑 없음 — S4-B3, floors 필드 [2차판정 16] 포함). 별도 S4_Refs.cs를 만들면 이
// 블록을 지워야 한다(CS0101 중복 정의).

/// <summary>S4 빌더 → S4_Wiring/S4_Dress 전달 묶음(R2 계약 C1-4). 좌표 섹터 로컬.</summary>
public sealed class S4_Refs
{
    public Transform generated;
    public Transform gimmickRoot;                 // generated/S4_Gimmicks
    public Vector2 cylAxisXZ = new Vector2(0f, 62f);  // 원통 축 [초안 §2]
    public float cylInnerR = 34f, cylWallT = 0.5f, cylWallTop = 26f;
    public float rockR = 10f, rockTop = 8f;
    public float bridgeTop = 18f;                 // = exitHeight - floorHeight
    public Bounds bubbleTrigger;                  // min(-4,8,58) max(4,18,66) [판정 18 ②]
    // 복귀 기준점(초안 최종본 §3-2·§4-4 값을 빌더가 넣는다). 값 = 바닥 윗면, 안전점 배치 때 W가 +0.1 [2차판정 15]
    public Vector3 spStairsLocal;                 // SP 계단아래 주점
    public Vector3[] spStairsBackups = new Vector3[2];
    public Vector3 spBubbleLocal;                 // SP 버블입구 주점
    public Vector3[] spBubbleBackups = new Vector3[2];
    public Vector3 checkpointStartLocal = new Vector3(0f, 0f, 3f); // CP_S4_Start 원점
    // 지형 분류
    public List<Transform> stairs = new List<Transform>();       // GEO_S4_Stair_* (쐐기 포함)
    public List<Transform> walls = new List<Transform>();        // GEO_S4_Wall_* (원통 볼록 조각 GEO_S4_Wall_Cyl_{nn} 48개 포함 [2차판정 15])
    public List<Transform> floors = new List<Transform>();       // GEO_S4_Floor_* — 바닥 [2차판정 16](이름 탐색 대체)
    public List<Transform> masts = new List<Transform>();        // GEO_S4_Mast_*
    public Transform rock;                        // GEO_S4_Rock
    public Transform exitBridge;                  // GEO_S4_Bridge_Exit
    // 팀 기믹
    public ZeroGravityBubble bubble;              // S4_Bubble(우리 BoxCollider 트리거 + 팀 컴포넌트, 판정 18 ②). 컴포넌트는 Wire가 붙인다 — Build 중 null 허용 [2차판정 4]
    public List<FallingRockSpawner> rockSpawners = new List<FallingRockSpawner>();   // 3 — Wire 생성, Build 중 0개 허용 [2차판정 4]
    public List<FixedPeriodicLaser> fixedLasers = new List<FixedPeriodicLaser>();    // 4
    public List<CharacterLockedLaser> aimLasers = new List<CharacterLockedLaser>();  // 6
}
#endif
