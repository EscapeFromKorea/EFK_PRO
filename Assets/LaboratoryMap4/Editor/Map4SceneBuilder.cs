#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F1-2 씬 구조 생성기 — Map4Layout 데이터 에셋, Master 씬, 섹터 씬 8개를 만들고 Build Settings에
/// 등록한다. Generated/ 아래만 매번 통째로 교체하고 Manual/은 있으면 절대 건드리지 않는다(Map3
/// 관례 — F1-2 명세).
///
/// [F1 재작업 판정 R1] EnsureLayout은 이제 섹터 항목이 "이미 있으면" 어떤 필드도 덮어쓰지 않는다
/// — 09-22 기준값은 그 섹터가 **처음 만들어질 때만** 시드로 쓰인다(에셋이 곧 정답지가 된다).
/// [결정2] 연결 통로는 이 섹터(출발지)의 Generated 안에 짓는다(한 곳으로 정함, 문서화 여기)
/// — 세계 높이는 항상 이 섹터의 exitHeight = 다음 섹터의 floorHeight(평평한 통로). 높이가
/// 바뀌는 섹터(S4·S6)는 그 차이를 TEMP_Ramp(각도 ≤25°)로 메워 0단계에서도 S1→S8을 걸어서
/// 통과할 수 있게 한다 — 섹터 구체화(반중력·포탈 콘텐츠) 때 제거 대상.
/// [결정3] 입구·출구·체크포인트·스폰 슬롯 3개는 Manual/Markers 아래에 **없을 때만** 생성한다
/// (사람이 옮긴 위치가 재생성에도 유지된다).
/// [R3] 인터랙티브 실행 중에는 Map4SceneGuard로 저장 확인 없는 교체를 막는다.
/// </summary>
public static class Map4SceneBuilder
{
    private const string LayoutPath = "Assets/LaboratoryMap4/Data/Map4Layout.asset";
    private const string ScenesDir = "Assets/LaboratoryMap4/Scenes";
    private const string SectorScenesDir = ScenesDir + "/Sectors";
    private const string MasterScenePath = ScenesDir + "/Map4_Master.unity";

    // [F1 재작업 판정 A5 — 인용 오류 정정] widths[]는 TeamLabBuilder.cs가 아니라 ExpandedLayout.cs:9
    // 에만 있다(TeamLabBuilder.cs:9는 titles[]다). starts/lengths/heights는 TeamLabBuilder.cs:24-26.
    // exitHeight는 [결정2] — TeamLabBuilder.cs:64의 연결 윗면 18(S4 뒤)·16(S6 뒤, 절대 로컬 34-18=16
    // 이동이 아니라 그 다음 구간 시작이 절대 34) 근거로 S4=18(=S5.floorHeight)·S6=34(=S7.floorHeight),
    // 나머지는 floorHeight와 동일(평지 통과). 이름은 기획작업/맵4_완성/설계의 근거 계획
    // (cheerful-herding-shell.md "섹터별 팀 컴포넌트 적용 예정" 표)에서 가져온 한글 테마명.
    // [실좌표 09-22]
    // [INF1 — S1_설계.md §11-1] S1 길이 132 → 188(+56, "기다란 복도" 재설계). 설계서 §11-1 절 표기는
    // [제안](구현 전 실측 조정 여지), 지시서 INF1 수치표 표기는 [확정 09-28 재설계] — 표기 차이는 보고서 참고.
    // Map4Layout.GetOrigin이 누적 계산이라 S2~S8 원점은 자동으로 +56 밀린다 — 이 값은 EnsureLayout이
    // 섹터를 "처음" 만들 때만 시드로 쓰이므로, 이미 Data/Map4Layout.asset에 S1이 있으면(R1) 이 줄은
    // 영향이 없다(Map4Layout.asset의 S1 length 갱신은 과제 S1-B 소유).
    private static readonly (string name, float width, float length, float floorHeight, float exitHeight)[] SectorSource =
    {
        ("S1 추격",        60f, 188f, 0f,  0f),
        ("S2 격리 구출",   76f,  84f, 0f,  0f),
        ("S3 점프",        56f, 184f, 0f,  0f),
        ("S4 반중력.보안", 72f, 124f, 0f,  18f),
        ("S5 외다리 함정", 60f, 136f, 18f, 18f),
        ("S6 포탈",        72f,  98f, 18f, 34f),
        ("S7 서버실",      84f, 110f, 34f, 34f),
        ("S8 약물 조합",   88f, 144f, 34f, 34f),
    };

    [MenuItem("Tools/Laboratory Map4/Generate All (Scenes + Layout)")]
    public static void GenerateAll()
    {
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            Debug.LogWarning("[Map4SceneBuilder] 사용자가 저장을 취소해 Generate를 중단한다.");
            return;
        }

        Lab_PlayerBuilder.RegenerateAll();
        Map4Layout layout = EnsureLayout();
        s_exitMarkerChecks.Clear(); // [INF1 R1] 이번 Generate의 Exit 마커 대조 결과만 표에 싣는다.
        EnsureFolder(SectorScenesDir);

        List<string> sceneNames = new List<string>();
        for (int i = 0; i < layout.sectors.Length; i++)
            sceneNames.Add(BuildSectorScene(layout, layout.sectors[i]));

        sceneNames.Insert(0, BuildMasterScene());
        RegisterBuildSettings();
        WriteOriginComparisonTable(layout);

        AssetDatabase.SaveAssets();
        Debug.Log($"[Map4SceneBuilder] Master + 섹터 {layout.sectors.Length}개 생성/갱신 완료.");
    }

    /// <summary>[F1 재작업 판정 R1] 섹터 항목이 이미 있으면 어떤 필드도 건드리지 않는다 — 09-22
    /// 기준값은 새로 추가되는 섹터에만 시드로 쓰인다. 그래서 "에셋 값을 사람이 바꿔도 다음
    /// Generate가 되돌리지 않는다"가 성립한다.</summary>
    public static Map4Layout EnsureLayout()
    {
        EnsureFolder("Assets/LaboratoryMap4/Data");
        Map4Layout layout = AssetDatabase.LoadAssetAtPath<Map4Layout>(LayoutPath);
        if (layout == null)
        {
            layout = ScriptableObject.CreateInstance<Map4Layout>();
            AssetDatabase.CreateAsset(layout, LayoutPath);
        }

        List<Map4Layout.SectorDef> defs = new List<Map4Layout.SectorDef>(layout.sectors);
        bool changed = false;
        for (int i = 0; i < SectorSource.Length; i++)
        {
            int id = i + 1;
            if (defs.Exists(d => d.id == id)) continue; // 이미 있음 — 전부 그대로 둔다(R1).

            (string name, float width, float length, float floorHeight, float exitHeight) src = SectorSource[i];
            defs.Add(new Map4Layout.SectorDef
            {
                id = id,
                sectorName = src.name,
                width = src.width,
                length = src.length,
                floorHeight = src.floorHeight,
                exitHeight = src.exitHeight,
                connectorLengthToNext = 12f,
                connectorWidthToNext = 8f
            });
            changed = true;
        }

        if (changed)
        {
            defs.Sort((a, b) => a.id.CompareTo(b.id));
            layout.sectors = defs.ToArray();
            EditorUtility.SetDirty(layout);
        }
        return layout;
    }

    private static string BuildSectorScene(Map4Layout layout, Map4Layout.SectorDef def)
    {
        string sceneName = $"Map4_S{def.id}";
        string path = $"{SectorScenesDir}/{sceneName}.unity";
        Vector3 origin = layout.GetOrigin(def.id);

        // [실측 09-28] 배치모드에서는 활성 씬이 "제목 없음(경로 없음)" 상태로 시작해 Additive
        // 모드가 "Cannot create a new scene additively with an untitled scene unsaved."로 실패한다
        // (run_generate.log 07:08, Lab_PlayerBuilder.cs와 동일 원인). 섹터 씬은 서로 동시에 열려
        // 있을 필요가 없으므로(한 번에 하나씩 짓고 저장하고 넘어간다) Single로 바꿔 이 제약을
        // 피한다 — Master+전체 섹터를 동시에 열어야 하는 OpenAllSectors()는 이미 저장된 Master를
        // 먼저 Single로 연 뒤 그 위에 Additive로 쌓으므로 이 문제가 없다.
        // [R3] Single로 교체하기 직전에 가드를 통과해야 한다(호출부인 GenerateAll이 이미 한 번
        // 확인했지만, 이 메서드가 독립적으로 불릴 가능성에 대비해 한 번 더 확인한다).
        // [3차 반려 C2] 이전엔 여기서 취소하면 경고만 찍고 sceneName을 그대로 반환했다 — 그러면
        // GenerateAll의 호출 루프가 이 섹터만 "건너뛴 채" 나머지를 계속 진행하고, 끝에 가서
        // (아래 73행 상당) "Master + 섹터 N개 생성/갱신 완료."를 그대로 찍어 부분 실패를 완료로
        // 오보했다. 이제 예외를 던져 GenerateAll 전체를 즉시 중단시킨다(그 "완료" 로그에 절대
        // 도달하지 못한다) — Map4Batch.RunGuarded의 try/catch가 [실패]로 정확히 집계한다.
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            throw new System.Exception($"[Map4SceneBuilder] 사용자가 저장을 취소해 섹터 {def.id} " +
                                        "생성을 중단한다 — GenerateAll 전체를 중단한다(부분 완료 금지).");
        }

        Scene scene = File.Exists(path)
            ? EditorSceneManager.OpenScene(path, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        {
            GameObject rootGo = GameObject.Find($"S{def.id}_Root");
            if (rootGo == null)
            {
                rootGo = new GameObject($"S{def.id}_Root");
                SceneManager.MoveGameObjectToScene(rootGo, scene);
            }
            rootGo.transform.position = origin;

            // [F1 재작업 판정 A7] `GetComponent<T>() ?? AddComponent<T>()`는 C# `??`가 Unity의
            // 오버로드된 `==`(fake-null 처리)가 아니라 CLR 원시 참조 null만 검사하므로, 파괴됐지만
            // 아직 GC되지 않은 컴포넌트(fake-null)를 "존재함"으로 오판할 수 있다 — 명시적 `== null`
            // 검사로 바꾼다.
            SectorController controller = rootGo.GetComponent<SectorController>();
            if (controller == null) controller = rootGo.AddComponent<SectorController>();
            controller.sectorId = def.id;
            controller.sectorName = def.sectorName;
            controller.width = def.width;
            controller.length = def.length;

            Transform generated = FindOrCreateChild(rootGo.transform, "Generated");
            FindOrCreateChild(rootGo.transform, "Manual"); // 있으면 그대로, 없으면만 생성.

            // Generated/는 매번 통째로 비우고 다시 짓는다 — 사람 편집은 Manual/에만(F1-2 명세).
            for (int i = generated.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(generated.GetChild(i).gameObject);

            // 구체화된 섹터는 전용 빌더가 짓고, 연결 통로·마커만 공용 코드로 잇는다. 전용 빌더가 전제
            // (섹터 크기 등)와 맞지 않아 false를 돌려주면 0단계 빈 틀로 되돌아간다.
            // [INF1] 섹터별 빌더를 직접 이름으로 부르던 것을 SectorBuilderRegistry 스위치로 바꿨다 —
            // 새 섹터 빌더가 생겨도 이 파일을 고치지 않아도 된다.
            if (SectorBuilderRegistry.TryBuild(def.id, generated, def))
                BuildConnectorAndMarkers(generated, controller, def, layout, def.exitHeight - def.floorHeight);
            else
                BuildEmptyShell(generated, controller, def, layout);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
        }
        // Single 모드라 다음 반복(다른 섹터)이나 이후 BuildMasterScene()의 NewScene(Single) 호출이
        // 이 씬을 그대로 교체한다 — 별도 Close가 필요 없다(위 주석 참고).

        return sceneName;
    }

    /// <summary>F1-2 마지막 문장 — "섹터 로직은 이번 단계에서 만들지 않는다. 빈 섹터 + 바닥·벽
    /// 틀까지만." Map4Build.Floor/Wall/Ramp만 쓴다(Door/Stairs는 섹터 로직 단계의 몫).
    /// [결정2] floorHeight != exitHeight인 섹터(S4·S6)는 평지 구간을 줄이고 그 자리에 TEMP_Ramp를
    /// 넣어 exitHeight까지 올린 뒤, 연결 통로를 그 높이에서 잇는다. 경사로 구간은 벽을 세우지
    /// 않는다(0단계 임시 — 측벽 없이도 통행 가능하다는 것만 증명하면 되고, 섹터 구체화 때
    /// 통째로 교체된다).</summary>
    private static void BuildEmptyShell(Transform generated, SectorController controller, Map4Layout.SectorDef def, Map4Layout layout)
    {
        Map4Build.BeginSection();

        float halfW = def.width / 2f;
        float rise = def.exitHeight - def.floorHeight;
        float flatEnd = def.length;
        float rampRun = 0f;

        if (Mathf.Abs(rise) > 0.001f)
        {
            float minRun = Mathf.Abs(rise) / Mathf.Tan(Map4Build.RampMaxAngleDeg * Mathf.Deg2Rad);
            rampRun = Mathf.Ceil(minRun * 1.1f / 2f) * 2f; // 10% 여유, 보기 좋게 짝수로 올림.
            flatEnd = def.length - rampRun;
            if (flatEnd < 0f)
            {
                Debug.LogError($"[Map4SceneBuilder] 섹터 {def.id} 길이({def.length}U)가 TEMP 경사로 " +
                                $"필요 수평거리({rampRun}U, 상승 {rise}U/{Map4Build.RampMaxAngleDeg}도) " +
                                "보다 짧다 — 경사로를 섹터 전체 길이로 강제한다(임시 조치).");
                flatEnd = 0f;
                rampRun = def.length;
            }
        }

        // [F1 재작업 판정 2차 반려 N1 — 이음매 재설계] 1차 수정은 겹침을 "여유 0.2U"로 피했으나
        // Ramp()의 윗면이 중심선 기준이라 실제로는 양 끝에서 0.3·cosθ(S4 0.2777U·S6 0.2765U)
        // 더 높이 떠 있어 턱+틈이 함께 생겼다(실측, 도형이 걸어서 못 올라감) — "통행 지장 없음"
        // 판단은 근거 없어 철회한다(N1 지적). Map4Build.Ramp가 이제 윗면을 (bottom,top) 선분에
        // 정확히 맞춰 만들므로(N1 기하 수정), 바닥·통로는 더 이상 인위적인 여유를 둘 필요가 없다
        // — flatEnd·def.length 그대로 붙이고, Ramp가 돌려주는 startSurfaceY/endSurfaceY로 턱이
        // 실제로 0(오차 0.02U 이내)인지 즉시 검증한다(결정11).
        // [N1 재재수정] 위 "윗면만 선분에 맞춘 회전 박스"는 그래도 두께 때문에 아랫면이 seam을
        // 넘어 튀어나와(두께×sinθ) 실제 감사에서 GEO_Floor(통로)와 0.114~0.116U 겹쳤다 — 근본
        // 원인은 회전 박스 자체였다. Map4Build.Ramp를 오버행 0인 쐐기 MeshCollider로 다시 바꿨으니
        // (Map4Build.cs 주석 참고) 여기 flatEnd·def.length 이음매 로직은 그대로 유지해도 된다.
        Map4Build.Floor(generated, new Vector3(-halfW, -0.3f, 0f), new Vector3(halfW, 0f, flatEnd));

        const float wallThickness = 0.5f;
        const float wallHeight = 6f;
        if (flatEnd > 0f)
        {
            Map4Build.Wall(generated, new Vector3(-halfW - wallThickness, 0f, 0f), new Vector3(-halfW, wallHeight, flatEnd));
            Map4Build.Wall(generated, new Vector3(halfW, 0f, 0f), new Vector3(halfW + wallThickness, wallHeight, flatEnd));
        }

        if (rampRun > 0f)
        {
            Transform ramp = Map4Build.Ramp(generated, new Vector3(-halfW, 0f, flatEnd), def.width, rise, rampRun,
                out float startY, out float endY);
            if (ramp != null)
            {
                ramp.name = "TEMP_Ramp";
                const float seamTolerance = 0.02f; // 결정11 — 허용 오차 0.02U.

                // [3차 반려 A] 이전 검사는 startY/endY(Ramp()가 스스로 되돌려주는 out 파라미터)를
                // 그 값을 만든 바로 그 입력(baseMin.y=0·baseMin.y+rise)과 비교했다 — Ramp() 내부를
                // 보면 그 두 값은 실제 메시 정점이 아니라 입력을 그대로 대입한 것이라(Map4Build.cs
                // "startSurfaceY = y0; endSurfaceY = y1;") 이 검사는 구조적으로 실패할 수 없었다
                // (입력=입력 비교, 반려 지적대로 "자체검사가 실패 불가"). 실제로 만들어진
                // MeshCollider 정점을 직접 읽어 이웃 바닥·통로 윗면과 비교하도록 바꾼다 — 이러면
                // Ramp() 내부 정점 계산이 out 파라미터와 어긋나는 미래의 회귀도 잡을 수 있다.
                MeshFilter mf = ramp.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 3)
                {
                    Debug.LogError($"[Map4SceneBuilder] 섹터 {def.id} TEMP_Ramp에 MeshFilter/메시가 " +
                                    "없다 — 결정11 검사를 할 수 없다.");
                }
                else
                {
                    // Map4Build.Ramp 정점 순서: 0=윗면-시작-좌, 2=윗면-끝-우. ramp의 로컬 좌표계가
                    // generated(=parent)와 동일(localPosition=0·localRotation=identity)하므로 이
                    // "정점 자체"(메시에 저장된 원시 좌표)가 이미 generated 로컬 좌표(=이 섹터
                    // 기준 상대 높이)다. [실측 버그·수정] 처음엔 여기서 ramp.TransformPoint(verts[i])
                    // .y를 썼는데, TransformPoint는 항상 "월드" 좌표를 반환한다 — S6처럼 generated
                    // 자체가 월드 Y=18(floorHeight)만큼 떠 있는 섹터에서는 이 값이 딱 18U만큼
                    // 어긋나 "섹터6 시작 턱 18.0000U 초과" 오탐을 냈다(실제로 Generate 배치 재실행
                    // 때 이 오탐으로 드러남). 로컬 좌표가 필요하므로 TransformPoint를 아예 쓰지
                    // 않고 정점 값을 그대로 읽는다.
                    Vector3[] verts = mf.sharedMesh.vertices;
                    float actualStartY = verts[0].y;
                    float actualEndY = verts[2].y;
                    const float floorTopY = 0f; // 위 Map4Build.Floor(..., new Vector3(halfW,0f,flatEnd)) 윗면.
                    float corridorTopY = rise;  // 아래 통로 블록의 corridorLocalY와 동일한 값.

                    if (Mathf.Abs(actualStartY - floorTopY) > seamTolerance)
                        Debug.LogError($"[Map4SceneBuilder] 섹터 {def.id} TEMP_Ramp 실측 시작 턱 " +
                                        $"{actualStartY - floorTopY:F4}U(메시 정점 기준) — 허용 오차 " +
                                        $"{seamTolerance}U 초과(결정11 위반).");
                    if (Mathf.Abs(actualEndY - corridorTopY) > seamTolerance)
                        Debug.LogError($"[Map4SceneBuilder] 섹터 {def.id} TEMP_Ramp 실측 끝 턱 " +
                                        $"{actualEndY - corridorTopY:F4}U(메시 정점 기준) — 허용 오차 " +
                                        $"{seamTolerance}U 초과(결정11 위반).");
                }

                // out 파라미터도 참고용으로 남긴다 — 위 실측(메시 정점)과 어긋나면 Ramp() 내부에서
                // out 파라미터 대입과 정점 계산이 따로 논다는 신호다.
                if (!Mathf.Approximately(startY, 0f) || !Mathf.Approximately(endY, rise))
                    Debug.LogWarning($"[Map4SceneBuilder] 섹터 {def.id} TEMP_Ramp out파라미터(startY=" +
                                      $"{startY:F4}·endY={endY:F4})가 기대값(0·{rise:F4})과 다르다 — " +
                                      "Ramp() 내부 점검 필요.");
            }
        }

        BuildConnectorAndMarkers(generated, controller, def, layout, rise);
    }

    /// <summary>연결 통로 + 마커 — 빈 틀(BuildEmptyShell)과 구체화된 섹터 빌더(S1_Builder 등)가 함께 쓴다.</summary>
    private static void BuildConnectorAndMarkers(Transform generated, SectorController controller, Map4Layout.SectorDef def, Map4Layout layout, float rise)
    {
        // [결정2] 연결 통로 — 이 섹터의 Generated 안에 짓는다(한 곳으로 정함). 세계 높이는
        // exitHeight(로컬 rise)이고, 다음 섹터의 floorHeight와 반드시 일치해야 평평하다.
        Map4Layout.SectorDef next = layout.GetSector(def.id + 1);
        if (next != null)
        {
            if (!Mathf.Approximately(def.exitHeight, next.floorHeight))
                Debug.LogError($"[Map4SceneBuilder] 섹터 {def.id}.exitHeight({def.exitHeight}) != " +
                                $"섹터 {next.id}.floorHeight({next.floorHeight}) — 연결 통로가 평평하지 " +
                                "않다(결정2 위반). Map4Layout.asset을 확인해라.");

            float corridorLocalY = rise; // def.floorHeight 기준 로컬 상대 높이.
            float halfCW = def.connectorWidthToNext / 2f;
            Map4Build.Floor(generated,
                new Vector3(-halfCW, corridorLocalY - 0.3f, def.length),
                new Vector3(halfCW, corridorLocalY, def.length + def.connectorLengthToNext));
        }

        BuildOrFindMarkers(generated, controller, def, rise);
    }

    // [INF1 R1] 옛 기본 위치(09-22 길이)에 남은 Exit 마커를 자동으로 옮길지 — 컨트롤타워 판정 대기.
    // [INF1 R2 — 검문 지적(중)] 기본값을 false로 되돌린다. 00_기반_설계 F1-2 "Manual/은 빌더가 절대
    // 건드리지 않음"·[결정3]과 F1 목표2의 충돌을 실행 에이전트가 스스로 "자동 이동"으로 정하고 켜 둔
    // 것은 월권이었다. 컨트롤타워가 승인해 결정3에 "옛 기본 위치의 Exit 마커 한정 예외"를 적은 뒤에만
    // true로 바꾼다. false일 때는 옮기지 않고 경고 + 대조표 "불일치 — 옛 기본 위치(자동 이동 꺼짐)"만 남긴다.
    // const가 아니라 static readonly인 것은 CS0162(도달 불가 코드) 경고를 피하려는 것.
    private static readonly bool AutoRelocateStaleDefaultExit = false;
    // [INF1 R1] 섹터별 Exit 마커 대조 결과 — GenerateAll 시작에 비우고 WriteOriginComparisonTable이 적는다.
    private static readonly List<string> s_exitMarkerChecks = new List<string>();

    /// <summary>[결정3] 입구·출구·체크포인트·스폰 슬롯 3개를 Manual/Markers 아래에 없을 때만
    /// 생성한다 — 있으면 그 Transform을 그대로 재사용해 사람이 옮긴 위치를 보존한다.
    /// [2차 반려 C-f] Exit 마커 높이는 이 섹터의 exitHeight(로컬 rise)여야 한다 — 예전엔 항상
    /// 로컬 y=0.1로 고정돼 있어 S4·S6처럼 출구가 위쪽(18U·34U 위)에 있는 섹터에서는 마커가
    /// 실제 출구와 전혀 다른(바닥) 높이에 찍혔다.</summary>
    private static void BuildOrFindMarkers(Transform generated, SectorController controller, Map4Layout.SectorDef def, float rise)
    {
        Transform manual = generated.parent.Find("Manual");
        Transform markers = FindOrCreateChild(manual, "Markers");

        controller.entrance = FindOrCreateMarker(markers, "Entrance", new Vector3(0f, 0.1f, 0f));
        Transform exitMarker = FindOrCreateMarker(markers, "Exit", new Vector3(0f, rise + 0.1f, def.length));
        ReconcileExitMarker(exitMarker, def, rise); // [INF1 R1] 길이가 바뀐 뒤 옛 기본 위치에 남은 Exit 처리.
        controller.exit = exitMarker;
        controller.checkpoint = FindOrCreateMarker(markers, "Checkpoint", new Vector3(0f, 0.1f, 3f));

        controller.spawnSlots = new Transform[3];
        for (int i = 0; i < 3; i++)
            controller.spawnSlots[i] = FindOrCreateMarker(markers, $"Spawn_{i}", new Vector3((i - 1) * 2f, 0.6f, 3f));
    }

    /// <summary>[INF1 R1 — 검문 지적 "S1 Exit 마커가 옛 자리 z=132에 남음"] 00_기반 F1 목표2(섹터 크기가
    /// 바뀌면 배치가 따라감)와 [결정3](Manual 마커는 없을 때만 생성)이 부딪치는 곳이다. S1 길이가
    /// 132→188로 바뀌어도 기존 Exit 마커는 재생성되지 않아 SectorController.exit가 카트 승차장 한가운데
    /// (z=132)를 가리킨 채 남았다(씬 YAML 실측, 2026-09-28).
    /// 규칙: 로컬 z가 현재 def.length와 ±0.01 안이면 일치. 아니면
    ///  (a) "옛 기본 위치"(x=0, y=rise+0.1 또는 [2차 반려 C-f] 이전 기본 y=0.1, z=09-22 기준 길이
    ///      ReferenceLengths_20260922[id-1])이면 사람이 옮긴 적 없는 자동 생성값으로 보고, 스위치가 켜져
    ///      있으면 현재 기본 위치로 옮기고 경고를 남긴다.
    ///  (b) 그 밖의 위치는 사람이 옮긴 것일 수 있으므로 [결정3]대로 건드리지 않고 경고만 남긴다.
    /// 결과는 s_exitMarkerChecks에 모아 원점 대조표 파일에 "Exit 마커 z = length" 대조로 적는다.
    /// 자동 이동 여부는 컨트롤타워 판정 대기 — [INF1 R2] 기본값 false(꺼짐). 승인 후에만 true로.
    /// [INF1 R2 — 알려진 한계] "옛 기본 위치"는 09-22 길이(ReferenceLengths_20260922)로만 판정한다.
    /// 직전 Generate 때의 length는 어디에도 기록하지 않으므로, 자동 이동으로 한 번 옮겨진 마커(예: S1
    /// z=188, 2026-09-28 21:14 Generate)는 그 뒤 길이가 다시 바뀌면 "사람이 옮긴 위치"로 분류돼
    /// 자동 이동되지 않는다(경고 + 대조표 "불일치 — 수동 확인"으로만 드러난다). 직전 length를 기록하려면
    /// SectorController(내 소유 아님)에 필드를 추가해야 하므로 이번에는 하지 않았다.</summary>
    private static void ReconcileExitMarker(Transform exit, Map4Layout.SectorDef def, float rise)
    {
        const float tol = 0.01f;
        Vector3 before = exit.localPosition;
        Vector3 expected = new Vector3(0f, rise + 0.1f, def.length);
        string verdict;

        if (Mathf.Abs(before.z - def.length) <= tol)
        {
            verdict = "일치";
        }
        else
        {
            int idx = def.id - 1;
            bool hasRef = idx >= 0 && idx < Map4Layout.ReferenceLengths_20260922.Length;
            bool isOldDefault = hasRef
                && Mathf.Abs(before.x) <= tol
                && (Mathf.Abs(before.y - (rise + 0.1f)) <= tol || Mathf.Abs(before.y - 0.1f) <= tol)
                && Mathf.Abs(before.z - Map4Layout.ReferenceLengths_20260922[idx]) <= tol;

            if (isOldDefault && AutoRelocateStaleDefaultExit)
            {
                exit.localPosition = expected;
                verdict = "일치(자동 이동 — 옛 기본 위치였음)";
                Debug.LogWarning($"[Map4SceneBuilder] 섹터 {def.id} Exit 마커가 옛 기본 위치 {before}(09-22 길이 " +
                                  $"{Map4Layout.ReferenceLengths_20260922[idx]})에 남아 있어 현재 출구 {expected}로 옮겼다 " +
                                  "(INF1 R1 — 사람이 옮긴 적 없는 자동 생성값만 옮긴다).");
            }
            else
            {
                verdict = isOldDefault
                    ? "불일치 — 옛 기본 위치(자동 이동 꺼짐), 수동으로 옮길 것"
                    : "불일치 — 사람이 옮긴 위치일 수 있어 그대로 둠(결정3), 수동 확인";
                Debug.LogWarning($"[Map4SceneBuilder] 섹터 {def.id} Exit 마커 로컬 z={before.z}가 섹터 길이 " +
                                  $"{def.length}와 다르다 — {verdict}. SectorController.exit가 실제 출구를 가리키지 않는다.");
            }
        }

        Vector3 after = exit.localPosition;
        s_exitMarkerChecks.Add($"S{def.id} | ({before.x}, {before.y}, {before.z}) | ({after.x}, {after.y}, {after.z}) | " +
                               $"{def.length} | {verdict}");
    }

    private static Transform FindOrCreateMarker(Transform parent, string name, Vector3 defaultLocalPos)
    {
        Transform t = parent.Find(name);
        if (t != null) return t; // 사람이 옮긴 기존 위치를 그대로 존중한다(결정3).
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = defaultLocalPos;
        return go.transform;
    }

    private static string BuildMasterScene()
    {
        EnsureFolder(ScenesDir);

        // [3차 반려 C2] 섹터와 동일한 이유로 경고+정상 반환 대신 예외를 던져 GenerateAll 전체를
        // 중단한다("완료" 로그 금지) — 취소 시 부분 완료를 완료로 오보하지 않는다.
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            throw new System.Exception("[Map4SceneBuilder] 사용자가 저장을 취소해 Master 씬 생성을 " +
                                        "중단한다 — GenerateAll 전체를 중단한다(부분 완료 금지).");
        }

        Scene scene = File.Exists(MasterScenePath)
            ? EditorSceneManager.OpenScene(MasterScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 플레이어 3개 — 정식 Variant 프리팹 인스턴스만 놓는다(직접 생성 금지, F1-1).
        EnsurePlayerInstance(scene, "Sphere", new Vector3(-2f, 1.5f, 3f));
        EnsurePlayerInstance(scene, "Cube", new Vector3(0f, 1.5f, 3f));
        EnsurePlayerInstance(scene, "Tetrahedron", new Vector3(2f, 1.5f, 3f));

        if (Object.FindObjectOfType<PlayerControlSwitcher>() == null)
            new GameObject("PlayerControlSwitcher").AddComponent<PlayerControlSwitcher>();

        Camera cam = Object.FindObjectOfType<Camera>();
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        if (cam.GetComponent<PlayerFollowCamera>() == null)
            cam.gameObject.AddComponent<PlayerFollowCamera>();

        if (Object.FindObjectOfType<RespawnController>() == null)
            new GameObject("RespawnController").AddComponent<RespawnController>();

        if (Object.FindObjectOfType<Light>() == null)
        {
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        // [판정 A1] Master 방향광 그림자를 소프트로 켠다 — 천장 있는 방(S1 복도·S2 격리 공간·S7·S8)을
        // 그림자로 어둡게 한다. 기존 조명이 있어 위 생성 블록을 건너뛰어도 매번 넣는다(재생성 멱등).
        // 대상은 Directional만. 세기·bias·해상도·환경광·QualitySettings는 건드리지 않는다(Light 값 하나만).
        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            if (l.type == LightType.Directional && l.shadows != LightShadows.Soft)
                l.shadows = LightShadows.Soft;
        }

        if (Object.FindObjectOfType<Map4Director>() == null)
            new GameObject("Map4Director").AddComponent<Map4Director>();

        // [F1 재작업 판정 결정6] 실입력 하네스 텔레메트리 — MAP4_TELE_PATH 환경변수가 없으면
        // Awake에서 스스로 꺼진다(일반 실행에 영향 0). 준비만 해 두고 이번 회차에서는 실행하지
        // 않는다(tools/map4_drive.ps1 참고, 사용자 허락 필요).
        if (Object.FindObjectOfType<Lab_InputHarnessTelemetry>() == null)
            new GameObject("Lab_InputHarness").AddComponent<Lab_InputHarnessTelemetry>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MasterScenePath);
        return "Map4_Master";
    }

    private static void EnsurePlayerInstance(Scene scene, string shape, Vector3 pos)
    {
        if (GameObject.Find($"Player_{shape}") != null) return;

        string path = $"Assets/LaboratoryMap4/Players/Player_{shape}.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[Map4SceneBuilder] Variant 프리팹이 없다: {path} — " +
                            "Lab_PlayerBuilder.RegenerateAll()이 먼저 성공했는지 확인해라.");
            return;
        }
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.position = pos;
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void RegisterBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MasterScenePath, true)
        };
        for (int i = 1; i <= 8; i++)
            scenes.Add(new EditorBuildSettingsScene($"{SectorScenesDir}/Map4_S{i}.unity", true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>[F1 재작업 판정 결정9/A6] 원점 대조표를 검증/에 파일로 남긴다(이전엔 채팅 로그의
    /// grep 결과로만 존재해 "검증/에 없음"으로 반려됐다 — R4/A6).
    /// [INF1] S1이 132→188(+56)로 재설계된 뒤에도 "09-22 그대로면 여기"라는 단순 대조는 항상
    /// 불일치가 나 의미가 없어진다 — 기대 원점을 "09-22 기준 원점 + 앞 섹터들의 (현재 길이 − 09-22
    /// 길이) 누적(연결 통로 길이 변화 포함)"으로 다시 계산해 비교한다. 출력 파일명도 Generate마다
    /// 기존 파일을 덮어쓰지 않도록 타임스탬프를 붙인다(공통 규칙 8 — 검증/ 기존 파일 보존).</summary>
    private static void WriteOriginComparisonTable(Map4Layout layout)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("F1-6 항목3 — 섹터 원점 대조표 (Map4Layout.GetOrigin() 계산값 vs 09-22 기준 + 길이 변경 누적 기대값)");
        sb.AppendLine("생성: " + System.DateTime.Now);
        // [INF1] 길이 변경 내역은 하드코딩하지 않고 현재 레이아웃에서 읽는다 — Data/Map4Layout.asset의
        // S1 length가 아직 132인지(S1-B 반영 전) 188인지(S1_설계 §11-1 반영 후)에 따라 달라지기 때문.
        sb.AppendLine("[INF1] 09-22 대비 길이 변경(현재 레이아웃 기준, 0이 아닌 것만):");
        bool anyLengthChange = false;
        for (int id = 1; id <= 8; id++)
        {
            Map4Layout.SectorDef d = layout.GetSector(id);
            if (d == null || id - 1 >= Map4Layout.ReferenceLengths_20260922.Length) continue;
            float delta = d.length - Map4Layout.ReferenceLengths_20260922[id - 1];
            if (Mathf.Approximately(delta, 0f)) continue;
            anyLengthChange = true;
            sb.AppendLine($"  S{id}: {Map4Layout.ReferenceLengths_20260922[id - 1]} → {d.length} ({(delta > 0 ? "+" : "")}{delta}) — " +
                          "뒤 섹터 기대 원점이 이만큼 밀린다");
        }
        if (!anyLengthChange) sb.AppendLine("  (없음 — 기대 원점 = 09-22 원점 그대로)");
        sb.AppendLine();
        sb.AppendLine("id | name | 09-22 기준 원점 | 누적 길이 변경(Σ) | 기대 원점 | 실제 원점(GetOrigin) | 판정");

        bool allMatch = true;
        float cumulativeDelta = 0f; // Σ_(j<i) (현재 length[j] - ReferenceLengths[j]) — 연결 통로 길이 변화 포함.
        const float referenceConnectorLength = 12f; // [실좌표 09-22] 모든 구간 동일(Map4Layout.cs 주석 근거).

        for (int id = 1; id <= 8; id++)
        {
            Map4Layout.SectorDef def = layout.GetSector(id);
            if (def == null) { sb.AppendLine($"{id} | (없음) | | | | | 불일치"); allMatch = false; continue; }

            int idx = id - 1;
            float referenceOrigin = idx < Map4Layout.ReferenceStarts_20260922.Length
                ? Map4Layout.ReferenceStarts_20260922[idx] : float.NaN;
            float expectedOrigin = referenceOrigin + cumulativeDelta;
            Vector3 actualOrigin = layout.GetOrigin(id);
            bool match = Mathf.Approximately(actualOrigin.z, expectedOrigin);
            allMatch &= match;

            sb.AppendLine($"{id} | {def.sectorName} | {referenceOrigin} | {cumulativeDelta} | " +
                          $"{expectedOrigin} | {actualOrigin.z} | {(match ? "일치" : "불일치")}");

            // 다음 섹터를 위해 이번 섹터의 길이·연결 통로 길이 변화를 누적한다.
            float refLength = idx < Map4Layout.ReferenceLengths_20260922.Length
                ? Map4Layout.ReferenceLengths_20260922[idx] : def.length; // 기준값 없는 섹터는 변화 0 취급.
            cumulativeDelta += (def.length - refLength) + (def.connectorLengthToNext - referenceConnectorLength);
        }
        sb.AppendLine();
        sb.AppendLine("전체 판정: " + (allMatch ? "8/8 일치" : "불일치 있음 — 위 표 확인"));

        // [INF1 R1] Exit 마커 z = length 대조 — 원점이 맞아도 Manual 마커가 옛 길이에 남으면
        // SectorController.exit가 틀린 자리를 가리킨다(결정3 때문에 재생성되지 않음).
        sb.AppendLine();
        sb.AppendLine("[INF1 R1] Exit 마커 로컬 z = 섹터 length 대조 (허용 ±0.01)");
        sb.AppendLine("섹터 | Generate 전 로컬 위치 | Generate 후 로컬 위치 | length | 판정");
        int exitOk = 0;
        foreach (string line in s_exitMarkerChecks)
        {
            sb.AppendLine(line);
            if (line.EndsWith("| 일치", System.StringComparison.Ordinal) || line.Contains("| 일치(")) exitOk++;
        }
        sb.AppendLine($"Exit 마커 판정: {exitOk}/{s_exitMarkerChecks.Count} 일치" +
                      (s_exitMarkerChecks.Count == layout.sectors.Length ? "" : $" (기록 수가 섹터 수 {layout.sectors.Length}와 다름)"));

        string outDir = ResolveMapRoot() + "/검증";
        Directory.CreateDirectory(outDir);
        // [INF1] 고정 파일명(F1_origin_comparison.txt)이면 Generate마다 기존 파일을 덮어써 공통 규칙 8을
        // 어긴다 — 타임스탬프가 붙은 새 파일로 남기고 기존 파일은 그대로 둔다.
        string fileName = $"F1_origin_comparison_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt";
        File.WriteAllText(Path.Combine(outDir, fileName), sb.ToString(), new UTF8Encoding(true));
    }

    private static string ResolveMapRoot()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName; // .../UnityProject
        return Directory.GetParent(projectRoot).FullName;                        // .../맵4_완성
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    // [10-04 사용자 요청] 순위 −100 — Tools 메뉴 맨 위(팀 KitchenMapV3 등 최저 30보다 앞)에 Laboratory Map4가, 그 안에서 이 항목이 첫 줄에 오게 한다.
    [MenuItem("Tools/Laboratory Map4/Open All Sectors", false, -100)]
    public static void OpenAllSectors()
    {
        if (!Map4SceneGuard.CanReplaceCurrentScenes())
        {
            Debug.LogWarning("[Map4SceneBuilder] 사용자가 저장을 취소해 Open All Sectors를 중단한다.");
            return;
        }
        EditorSceneManager.OpenScene(MasterScenePath, OpenSceneMode.Single);
        for (int i = 1; i <= 8; i++)
            EditorSceneManager.OpenScene($"{SectorScenesDir}/Map4_S{i}.unity", OpenSceneMode.Additive);
    }
}
#endif
