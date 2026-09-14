#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// KitchenMapV3 — 저장소 DoorSystem·DreamThreadSystem 사본을 이용한 기믹 배선 (R2).
/// 근거: 맵2_V3_릴레이설계/기믹배선_계획_2026-09-06.md §1 · 사용자 결정 09-06 "기믹 가능한 것
/// 전부"(TO_코워크 #9). 씬 저장 없음.
/// [정정, map-reviewer 28차 R1] "둘 다 멱등"은 부정확했다 — "2b. Wire Gimmicks"(Wire(), Build All
/// 체인 끝)는 재실행마다 "V3_Gimmicks" 그룹 전체를 지우고 처음부터 다시 배선해 완전히 멱등이다
/// (Wire() 진입부의 그룹 DestroyImmediate — [정정 2026-09-09, 검문32차 A1] 이 앵커를 줄번호로
/// 적을 때마다("(:43~44"→":50~51") 다음 편집에서 다시 어긋나 재발했다. 이번부터 줄번호 대신
/// 함수명 앵커로 고정해 이 문제 자체를 없앤다). "2c. Wire
/// Catapult"(WireCatapultMenu, Build All 체인 밖 별도 실험, 기본 OFF)는
/// "V3_Gimmicks" 그룹 자체는 보존한 채 그 안의 "P3_Catapult_Group" 서브그룹만 골라서 지우고
/// 다시 만든다 — 이전엔 이 서브그룹을 안 지워 2c를 두 번 누르면 투석기 2대·질량 300·동명 그룹
/// 2개가 남는 비멱등이었다(WireCatapult() 진입부에서 DestroyImmediate로 해소, R1). 마커
/// (Catapult_건조대_자리)가 없으면(Build All 미실행) 2c는 root·그룹 어느 것도 새로 만들지 않고
/// 즉시 실패한다(WireCatapultMenu() 마커 선확인, R2).
///
/// 원칙:
///  · DoorSystem·DreamThreadSystem 컴포넌트(ExitWeightPlate·ThreadAnchor·ThreadBridge)는 전부
///    정식 생성기(`EditorApplication.ExecuteMenuItem`)로만 만든다 — 직접 `new
///    ExitWeightPlate()`류로 붙이지 않는다(저장소 코드 자체는 사본이라 AddComponent해도 동작은
///    하지만, 이 프로젝트의 관례(V3_Checkpoints.cs 등)가 "정식 생성기 경유"를 지킨다).
///  · 생성 검증은 Selection에 기대지 않는다 — 메뉴 실행 전후 컴포넌트 집합을 비교해 "새로 생긴
///    것 정확히 1개"만 성공으로 본다(V3_Checkpoints.cs R2 반려 해소와 동일 패턴).
///  · 발자국(트리거 크기)은 손으로 좌표를 다시 베끼지 않고, 대상 오브젝트의 콜라이더/렌더러
///    world bounds를 읽어 자동으로 맞춘다 — V3_Build.cs 좌표가 바뀌어도 여기 숫자를 안 건드려도
///    된다.
///  · V3_PlateSensor·V3_GoalGate는 이 프로젝트 전용 신규 컴포넌트라(DoorSystem·DreamThreadSystem
///    소속이 아니다) 정식 생성기가 없다 — T0RS_SequentialGate.cs의 게이트 자식 오브젝트와 같은
///    방식으로 직접 만든다.
/// </summary>
public static class V3Gimmicks
{
    private const string GroupName = "V3_Gimmicks";

    // 우선순위 4 — 기존 0(Clear)·1(BuildAll)·2(SetupPlay)·3(Audit) 핵심 파이프라인 바로 다음
    // 빈 슬롯(4~19 미사용, grep 확인: 20 Trolley·32 Place T0RS·37~40 토글류·50 PhysLab). "2b"라는
    // 표시 이름이 원하는 위치(Build All과 Setup Play 사이)를 숫자로 정확히 끼워 넣을 정수 우선순위가
    // 없어(1과 2 사이), 핵심 파이프라인 그룹의 연장으로 취급해 4를 썼다.
    // [확정, R7/R8] Unity MenuItem은 같은 메뉴 트리 안에서 priority 오름차순으로 정렬한다는 것이
    // API 계약이라(0<1<2<3<4<20<...), 실기 확인 없이도 이 항목이 "4. Audit + T0 Measure"
    // (priority 3) 바로 아래에 정렬됨은 확정이다 — 더 이상 (추측)이 아니다. 다만 "2. Build All"과
    // "3. Setup Play" 사이에 끼워 넣는 원래 의도는 정수 우선순위로는 여전히 불가능해 근사(4)를
    // 그대로 쓴다. 기능(BuildAll 체인 호출)에는 영향 없다.
    [MenuItem("Tools/KitchenMapV3/2b. Wire Gimmicks (DoorSystem+DreamThread)", false, 4)]
    public static void Wire()
    {
        GameObject root = V3.Root();
        Transform old = root.transform.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        GameObject group = new GameObject(GroupName);
        group.transform.SetParent(root.transform, false);

        WireStartMat(group);
        WireP5Goal(group);
        WireSwingRings(group);
        WireClothesline(group);
        WireP2Gate(group);
        // [컨트롤타워 결정 2026-09-09, TO_코워크 [#2] 5항] 투석기(WireCatapult)는 이 체인에서
        // 뺐다 — map-reviewer 26차 검문 반려(겹침 23쌍·질량150 MassAudit ❌·뒷마당 RB 41>40 등,
        // 상신 판정 전) 상태라 Build All을 돌릴 때마다 기본으로 깨지게 두지 않는다. 별도 실험
        // 메뉴 "2c. Wire Catapult"로만 켠다(기본 OFF, 아래 WireCatapultMenu 참고).

        V3.Log("Wire Gimmicks 완료 — B2 시작판 · P5 GOAL(판A/B/C+게이트) · P4 스윙고리 6 · " +
               "P4 빨랫줄 · P2 실게이트. P3 투석기는 Build All 체인에서 분리됨 — " +
               "'2c. Wire Catapult' 메뉴로 별도 실행(실험 전용, 기본 OFF, 상신 판정 전).");
    }

    // ── 공통 헬퍼 ────────────────────────────────────────────────────────────────────

    /// <summary>메뉴 실행 전후로 T 컴포넌트 집합을 비교해 "새로 생긴 것" 하나를 찾는다 —
    /// Selection에 의존하지 않는다(V3_Checkpoints.cs R2 반려 해소와 동일 패턴). 신규 개수가
    /// 정확히 1이 아니면(0=실패, 2+=중복) 실패로 본다.
    /// [R6 반려, map-reviewer 22차] 실패 로그를 V3.Warn(Debug.LogWarning)에서 Debug.LogError로
    /// 올린다 — V3_Batch.RunAll은 LogType.Error/Exception만 errorCount에 센다(V3_Batch.cs
    /// handler: `if (type == LogType.Error || type == LogType.Exception) errorCount++;`).
    /// LogWarning인 채로 두면 무인 검증(batchmode)에서 배선 실패가 있어도 exitCode가 계속 0으로
    /// 나와 "미배선이 그냥 새어 나간다". 아울러 실패 시 생성기가 판정 대상 T 말고도 만들어 버릴 수
    /// 있는 부수 오브젝트(예: ExitWeightPlate 생성기가 함께 만드는 door)까지 포함해, 이번
    /// 호출로 새로 생긴 GameObject 전부를 지운다(CleanupResidue) — 실패한 시도의 잔재가 씬에
    /// 남지 않게 한다.</summary>
    private static T SpawnViaMenu<T>(string menuPath) where T : Object
    {
        var beforeAll = new HashSet<GameObject>(Object.FindObjectsOfType<GameObject>());
        var before = new HashSet<T>(Object.FindObjectsOfType<T>());
        if (!EditorApplication.ExecuteMenuItem(menuPath))
        {
            Debug.LogError($"[KitchenMapV3] 정식 생성기 메뉴 실행 실패: {menuPath}");
            CleanupResidue(beforeAll, menuPath);
            return null;
        }
        T created = null;
        int newCount = 0;
        foreach (T t in Object.FindObjectsOfType<T>())
        {
            if (before.Contains(t)) continue;
            created = t;
            newCount++;
        }
        if (newCount != 1)
        {
            Debug.LogError($"[KitchenMapV3] {menuPath}: 생성 검증 실패(신규 {typeof(T).Name} {newCount}개, 기대 1개) — 이 항목 배선을 건너뛴다.");
            CleanupResidue(beforeAll, menuPath);
            return null;
        }
        return created;
    }

    /// <summary>실패한 SpawnViaMenu 호출 직전 GameObject 스냅샷(beforeAll) 대비 새로 생긴 것 전부를
    /// 지운다 — 생성기가 판정 대상 T 외에 부수 오브젝트(문·임시 앵커 등)까지 만들었어도 실패
    /// 시엔 잔재가 씬에 안 남게 한다(R6). 부모가 먼저 지워져 자식이 이미 파괴된 경우
    /// `go == null`이 Unity의 파괴된 오브젝트 비교 규칙으로 true가 되어 이중 파괴를 자동으로
    /// 건너뛴다.</summary>
    private static void CleanupResidue(HashSet<GameObject> beforeAll, string menuPath)
    {
        int cleaned = 0;
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
        {
            if (go == null || beforeAll.Contains(go)) continue;
            Object.DestroyImmediate(go);
            cleaned++;
        }
        if (cleaned > 0)
            V3.Log($"{menuPath}: 실패 후 잔여물 {cleaned}개 정리(문·앵커 등 생성기 부수 오브젝트 포함).");
    }

    private static GameObject SubGroup(GameObject parent, string name)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        return g;
    }

    /// <summary>대상(target)의 위치·스케일을 footprintSource 콜라이더의 world bounds "상면"에
    /// 맞춘다(target 자신은 시각적으로 보여야 하는 판 — ExitWeightPlate 등). thickness는 판
    /// 두께(Unity Y).</summary>
    private static void FitOnTop(GameObject target, GameObject footprintSource, float thickness)
    {
        if (footprintSource == null) { V3.Warn($"{target.name}: 발자국 대상 오브젝트가 없다(null)."); return; }
        Collider col = footprintSource.GetComponent<Collider>();
        if (col == null) { V3.Warn($"{target.name}: '{footprintSource.name}'에 콜라이더가 없어 발자국을 못 읽었다."); return; }
        Bounds b = col.bounds;
        target.transform.position = new Vector3(b.center.x, b.max.y + thickness * 0.5f, b.center.z);
        target.transform.localScale = new Vector3(b.size.x, thickness, b.size.z);
    }

    /// <summary>footprintSource 상면에 딱 맞는, 렌더러 없는 순수 트리거 볼륨 하나를 새로 만든다
    /// (대상이 이미 시각적으로 존재하는 판 위에 얹는 "감지만 하는" 센서용 — T0RS_nn_Gate 자식
    /// 패턴과 동일하게 빈 GameObject + BoxCollider만 붙인다).</summary>
    private static GameObject FootprintTrigger(GameObject parent, string name, GameObject footprintSource, float thickness)
    {
        if (footprintSource == null)
        {
            // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] PlateB/PlateC 발자국(존재-확인 실패)도
            // 무인 검증 exitCode에 반영되게 Debug.LogError로 올린다(V3_Batch.RunAll의 errorCount는
            // LogType.Error/Exception만 센다).
            Debug.LogError($"[KitchenMapV3] {name}: 발자국 대상 오브젝트가 없다(null).");
            return null;
        }
        Collider srcCol = footprintSource.GetComponent<Collider>();
        if (srcCol == null) { V3.Warn($"{name}: '{footprintSource.name}'에 콜라이더가 없어 발자국을 못 읽었다."); return null; }
        Bounds b = srcCol.bounds;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, true);
        go.transform.position = new Vector3(b.center.x, b.max.y + thickness * 0.5f, b.center.z);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(b.size.x, thickness, b.size.z);
        return go;
    }

    /// <summary>이름의 오브젝트를 찾아 렌더러 world bounds의 상단 중앙을 돌려준다(콜라이더가 없는
    /// V3.Marker류 — 렌더러는 남아 있다).</summary>
    private static bool TryMarkerTopCenter(string markerName, out Vector3 topCenter)
    {
        topCenter = default;
        GameObject marker = GameObject.Find(markerName);
        if (marker == null) { V3.Warn($"마커 '{markerName}'을 찾지 못했다."); return false; }
        Renderer rend = marker.GetComponent<Renderer>();
        if (rend == null) { V3.Warn($"마커 '{markerName}'에 렌더러가 없어 bounds를 못 읽었다."); return false; }
        Bounds b = rend.bounds;
        topCenter = new Vector3(b.center.x, b.max.y, b.center.z);
        return true;
    }

    // ── B2 시작 매트 = 무게판 Teach (기믹배선_계획 §1.2 첫 항목) ──────────────────────

    private static void WireStartMat(GameObject parent)
    {
        GameObject sub = SubGroup(parent, "B2_StartMat");
        GameObject mat = GameObject.Find("Start_Mat_무게판2.75");
        // Q6 반려: latchOpen=true(한 번 열리면 계속 유지, ExitWeightPlate.Update()
        // "if (isOpen && latchOpen) return;")였던 것을 false로 정정 — 스폰 지점의 도형 3종
        // 질량 합이 이미 1.5(구)+1(세모)+3(네모)=5.5(공통규칙 §3 09-06 정정 질량 열)로 임계
        // 2.75를 넘어, Build All 직후 셋이 매트 위에 있으면 시작하자마자 즉시 눌린다. latchOpen=
        // true인 채였다면 그 뒤 셋이 흩어져 매트를 완전히 떠나도 열림 상태가 영구 고정돼
        // "시작 즉시 눌림 → 떠나면 원복" 요구와 반대로 동작했다. false면 ExitWeightPlate.Update()가
        // 매 프레임 무게를 재판정해 떠나면 자동으로 닫힘 상태로 되돌아간다.
        WireExitPlateWithSensor(sub, mat, "B2_StartMat", 2.75f, latchOpen: false,
            addSensor: false, searchCabinetDoor: true, sensor: out _);
    }

    /// <summary>ExitWeightPlate를 정식 생성기로 만들어 footprintSource 상면에 배치한다.
    /// addSensor면 같은 GameObject에 V3_PlateSensor를 형제로 붙여 out sensor에 담는다
    /// (ExitWeightPlate.isOpen이 private이라 외부 판정용 별도 창구가 필요할 때만 true — B2 Teach는
    /// 필요 없다). searchCabinetDoor면 "찬장 문"(U1-c) 연동 대상을 찾아보고, 못 찾으면(또는 연결이
    /// 부적절하면) 판만 배선한 채 경고 로그로 남긴다(추측 연결 금지).
    /// [C2 반려, map-reviewer 24차] 반환형을 `V3_PlateSensor`(성공/센서 겸용) 단일값에서
    /// `bool`(성공 여부) + `out V3_PlateSensor sensor`로 분리했다 — 이전엔 addSensor:false로
    /// "정상 성공"한 호출(B2 StartMat)도 null을 반환해 호출부가 null만으로는 "실패"와 "센서를
    /// 만들지 말라고 시킨 정상 성공"을 구분할 수 없었다(거짓 실패로 오판될 여지).</summary>
    private static bool WireExitPlateWithSensor(GameObject parent, GameObject footprintSource,
        string name, float requiredWeight, bool latchOpen, bool addSensor, bool searchCabinetDoor,
        out V3_PlateSensor sensor)
    {
        sensor = null;
        if (footprintSource == null)
        {
            // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] Start_Mat·PlateA 발자국(존재-확인
            // 실패)도 무인 검증 exitCode에 반영되게 Debug.LogError로 올린다(V3_Batch.RunAll의
            // errorCount는 LogType.Error/Exception만 센다).
            Debug.LogError($"[KitchenMapV3] {name}: 발자국 대상 오브젝트를 찾지 못했다 — Build All을 먼저 실행했는지 확인해라.");
            return false;
        }

        ExitWeightPlate plate = SpawnViaMenu<ExitWeightPlate>("Tools/DoorSystem/Create Exit Weight Plate (+ Door)");
        if (plate == null) return false;

        GameObject go = plate.gameObject;
        go.name = name + "_ExitWeightPlate";
        go.transform.SetParent(parent.transform, true);
        FitOnTop(go, footprintSource, 0.3f);

        plate.requiredWeight = requiredWeight; // [검증] 🔒N2 임계 2.75 — 생성기 기본값과 동일, 명시 재확인.
        plate.latchOpen = latchOpen;

        // 생성기가 "판+전용 문" 세트로만 만들어 준다(DoorSystem에 문 없는 판 전용 메뉴가 없음).
        // 연동을 확정하지 못하면 이 문은 아무 데도 안 쓰이는 허공의 잔재라 곧바로 지운다.
        doorPhysics autoDoor = plate.targetDoor;
        plate.targetDoor = null;
        plate.exitBarriers = new GameObject[0];
        if (autoDoor != null) Object.DestroyImmediate(autoDoor.gameObject);

        if (searchCabinetDoor)
        {
            // U1-c "찬장 문"(RELAY_병합본_1of4.md:975·982 · 4of4:677 — "열린 현관 수납장 문 위
            // Z=3.5, B2 무게판으로 열린다") 연동 대상 탐색. V3_Build.cs 전수 grep 결과("장\b|
            // Cabinet|Cupboard|Sideboard|찬장") 러그 존(S1_Dining_V1)에 인접한 후보는
            // Sideboard_수납장(x18~46·y0.5~8.5·z0~8, [실좌표 top 8]) 하나뿐이다. 하지만 이
            // 오브젝트는 통짜 솔리드 박스라 "문짝"만 분리된 지오메트리가 없다 — doorPhysics를
            // 걸면 캐비닛 전체가 doorTargetYOffset(기본 3U)만큼 통째로 떠오르는 형상이 되어
            // "문이 삐걱 열린다"는 의도와 다른, 검증 안 된 신설 조형이 된다. 그래서 연결하지
            // 않는다(추측 금지 원칙).
            // Q6 신규 확인: U1-c 뒤에 오는 보상("서랍 1단", RELAY_병합본_1of4.md:982 표 마지막 칸)도
            // doorPhysics 연결 대상 후보로 검토했으나, 이 "서랍 1단"은 문이 열린 뒤 나타나는
            // 콘텐츠 설명일 뿐 V3_Build.cs·DATA_KitchenMap_v2.json 어디에도 그 자체의 좌표를 가진
            // 오브젝트로 존재하지 않는다(전수 grep 0건) — 즉 대체 후보가 아니라 애초에 이 블록아웃
            // 단계에서 지어진 적이 없는 개념이다(폐기 상태로 확인). Sideboard 상면(z8.0)도 별도
            // 대체 지점으로 검토했으나 통짜 솔리드라는 문제는 동일해 채택하지 않는다.
            GameObject cabinetCandidate = GameObject.Find("Sideboard_수납장");
            if (cabinetCandidate != null)
                V3.Warn($"{name} 연동 대상 미확인 — 후보 'Sideboard_수납장'(U1-c '찬장 문'과 이름이 " +
                        "가장 가깝고 러그 존에 인접, 상면 z8.0)을 찾았으나 문짝이 분리된 지오메트리가 " +
                        "아니라 doorPhysics 연결을 보류했다. 보상으로 언급된 '서랍 1단'도 이 지형에 " +
                        "실좌표 오브젝트로 존재하지 않는다(폐기 상태, 신규 확인). 판만 배선 — 검토 요청.");
            else
                V3.Warn($"{name} 연동 대상 미확인 — '찬장 문'(U1-c)에 해당하는 오브젝트를 V3_Build.cs에서 " +
                        "찾지 못했다. 판만 배선.");
        }

        if (addSensor)
        {
            sensor = go.AddComponent<V3_PlateSensor>();
            sensor.requiredWeight = requiredWeight;
        }
        return true;
    }

    // ── P5 GOAL 판정 (판 A·B·C + 실 게이트 앵커 2개 + V3_GoalGate) ───────────────────

    private static void WireP5Goal(GameObject parent)
    {
        GameObject sub = SubGroup(parent, "P5_Goal");

        GameObject goalGate = GameObject.Find("GOAL_Gate_닫힘(0/1·무변화)");
        if (goalGate == null)
        {
            // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] 존재-확인 실패도 무인 검증
            // exitCode에 반영되게 Debug.LogError로 올린다(V3_Batch.RunAll의 errorCount는
            // LogType.Error/Exception만 센다) — 이미 정상 배선된 것은 건드리지 않는다.
            Debug.LogError("[KitchenMapV3] GOAL_Gate_닫힘(0/1·무변화)를 찾지 못했다 — P5 배선 전체를 건너뛴다.");
            return;
        }

        // 판 A: 실좌표 위치에 ExitWeightPlate(2.75, latchOpen=false, targetDoor 없음) +
        // V3_PlateSensor(판정용 형제 컴포넌트 — ExitWeightPlate.isOpen이 private이라 GoalGate가
        // 직접 못 읽는다. 식 복제 금지 — PlayerWeight.Of를 그대로 호출하는 센서를 별도로 둔다).
        GameObject plateABox = GameObject.Find("PlateA_무게판2.75_네모(실좌표)");
        WireExitPlateWithSensor(sub, plateABox, "P5_PlateA", 2.75f,
            latchOpen: false, addSensor: true, searchCabinetDoor: false, sensor: out V3_PlateSensor sensorA);

        // 판 B·C: "실 게이트 너머 도달"이 조건이라 무게 임계가 아니라 도형 존재만으로 판정한다
        // (ExitWeightPlate를 쓰지 않는다 — DoorSystem 쪽 문 연동이 필요 없다).
        GameObject plateBBox = GameObject.Find("PlateB_실게이트너머(배치안)");
        V3_PlateSensor sensorB = WirePresenceSensor(sub, plateBBox, "P5_PlateB");

        GameObject plateCBox = GameObject.Find("PlateC_실게이트너머(배치안)");
        V3_PlateSensor sensorC = WirePresenceSensor(sub, plateCBox, "P5_PlateC");

        // 실 게이트 앵커: PlateB/C_실게이트3.0_마커 AABB 상단 중앙에 ThreadAnchor 1개씩.
        WireMarkerAnchor(sub, "PlateB_실게이트3.0_마커(배치안·이동 09-05)", "P5_PlateB_GateAnchor");
        WireMarkerAnchor(sub, "PlateC_실게이트3.0_마커(배치안·이동 09-05)", "P5_PlateC_GateAnchor");

        if (sensorA == null || sensorB == null || sensorC == null)
        {
            V3.Warn("P5 GoalGate: 판 센서 3개 중 일부를 만들지 못해 V3_GoalGate 배선을 생략한다.");
            return;
        }

        GameObject logicGo = new GameObject("P5_GoalGateLogic");
        logicGo.transform.SetParent(sub.transform, false);
        V3_GoalGate gg = logicGo.AddComponent<V3_GoalGate>();
        gg.plateA = sensorA;
        gg.plateB = sensorB;
        gg.plateC = sensorC;
        gg.toleranceSec = 3f;     // 지시: "마지막 눌림 기준 3초 이내 전부 true".
        gg.gateObject = goalGate;
    }

    private static V3_PlateSensor WirePresenceSensor(GameObject parent, GameObject footprintSource, string name)
    {
        GameObject box = FootprintTrigger(parent, name + "_Sensor", footprintSource, 0.3f);
        if (box == null) return null;
        V3_PlateSensor sensor = box.AddComponent<V3_PlateSensor>();
        sensor.requiredWeight = 0f; // 도형 존재만(가중치 없음) — "실 게이트 너머 도달" 판정.
        // [A1 반영, map-reviewer 24차] FootprintTrigger가 만드는 box는 콜라이더만 있고 렌더러가
        // 없어(빈 GameObject) V3_PlateSensor.Awake()의 자기/자식 렌더러 탐색이 항상 실패했다 —
        // 판 B·C만 자기 판 조명(Q2)이 안 걸리고 판 A만 걸리던 원인. footprintSource(실제로 보이는
        // 판 오브젝트, "Orange" V3.Box)의 Renderer를 명시 전달해 3판 전부 조명이 동작하게 한다 —
        // 여전히 그 판 "자신"의 렌더러만 밝힌다(🔒H2 P5 ①″ "자기 자신만" 유지, 다른 판·개수
        // 표시와 무관).
        if (footprintSource != null)
        {
            Renderer plateRend = footprintSource.GetComponent<Renderer>();
            if (plateRend != null) sensor.ConfigureRenderer(plateRend);
        }
        return sensor;
    }

    private static void WireMarkerAnchor(GameObject parent, string markerName, string anchorName)
    {
        if (!TryMarkerTopCenter(markerName, out Vector3 topCenter)) return;
        ThreadAnchor anchor = SpawnViaMenu<ThreadAnchor>("Tools/DreamThread/Create Anchor");
        if (anchor == null) return;
        anchor.gameObject.name = anchorName;
        anchor.transform.SetParent(parent.transform, true);
        anchor.transform.position = topCenter;
        // connectRange 기본값(4) 유지 — 지시에 별도 값 없음.
    }

    // ── P4 스윙 고리 6개 ──────────────────────────────────────────────────────────────

    private static void WireSwingRings(GameObject parent)
    {
        GameObject sub = SubGroup(parent, "P4_SwingRings");
        for (int i = 1; i <= 6; i++)
        {
            string ringName = $"SwingRing_{i}_z24";
            GameObject ring = GameObject.Find(ringName);
            if (ring == null)
            {
                // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] 존재-확인 실패도 무인 검증
                // exitCode에 반영되게 Debug.LogError로 올린다.
                Debug.LogError($"[KitchenMapV3] {ringName}을 찾지 못했다 — 건너뛴다.");
                continue;
            }

            ThreadAnchor anchor = SpawnViaMenu<ThreadAnchor>("Tools/DreamThread/Create Anchor");
            if (anchor == null) continue;
            anchor.gameObject.name = $"SwingRing_{i}_Anchor";
            anchor.transform.SetParent(sub.transform, true);
            anchor.transform.position = ring.transform.position; // 지시: "SwingRing 위치로 이동, 원 박스는 시각 유지".
            // connectRange 기본값 유지.
        }
    }

    // ── P4 빨랫줄 (Line_Pole_L ↔ Line_Pole_R, ThreadBridge 다경간 체인) ───────────────

    private static void WireClothesline(GameObject parent)
    {
        // R2 반려(map-reviewer 22차): 이전 버전은 기둥(Line_Pole_L/R) 콜라이더 상단 대각을 앵커선
        // 기준으로 썼다(x·y가 모두 어긋나 있어 실거리 84.07U — 세로·가로 어긋난 두 기둥을 그대로
        // 이었기 때문). 그런데 씬에는 실제로 "보이는" 빨랫줄 오브젝트가 이미 있다 — 🔒N4
        // "실재 지형 우선": V3_Build.cs Shell()의 `Clothesline_빨랫줄(기믹:네모시 1.65 처짐)`
        // (Doc(10,96,12)~(86.9,97,12.3), NoColl()로 콜라이더만 뺀 렌더러 전용 오브젝트). 좌표
        // 리터럴을 다시 베끼지 않고 그 오브젝트의 렌더러 world bounds를 직접 읽어 앵커선을 얹는다
        // — V3_Build.cs 수치가 나중에 바뀌어도 이 코드는 안 깨진다.
        const string clotheslineName = "Clothesline_빨랫줄(기믹:네모시 1.65 처짐)";
        GameObject cloth = GameObject.Find(clotheslineName);
        if (cloth == null)
        {
            // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] R6 당시엔 "SpawnViaMenu 실패 시"에
            // 한정해 이 존재-확인 실패(SpawnViaMenu와 무관)는 V3.Warn으로 남기고 확장 여부를 §3
            // 검토 요청으로 넘겼었다 — 이번 회차에 컨트롤타워가 Clothesline·SwingRing·GOAL_Gate·
            // P2 마커·Start_Mat·PlateA/B/C의 GameObject.Find류 존재-확인 실패 전체를 Debug.LogError로
            // 승격하도록 판정, 반영한다.
            Debug.LogError($"[KitchenMapV3] '{clotheslineName}'을 찾지 못해 빨랫줄 배선을 건너뛴다.");
            return;
        }
        Renderer clothRend = cloth.GetComponent<Renderer>();
        if (clothRend == null)
        {
            V3.Warn($"'{clotheslineName}'에 렌더러가 없어 좌표를 못 읽었다 — 빨랫줄 배선을 건너뛴다.");
            return;
        }
        Bounds cb = clothRend.bounds;
        // 문서 좌표로 (10,96.5,12.15)~(86.9,96.5,12.15) — x는 줄의 양 끝, y·z는 줄 두께의 중앙.
        Vector3 topL = new Vector3(cb.min.x, cb.center.y, cb.center.z);
        Vector3 topR = new Vector3(cb.max.x, cb.center.y, cb.center.z);
        float chord = Vector3.Distance(topL, topR);

        // [C1 정정, map-reviewer 24차] 신규 확인(R2)의 "양쪽 다 안 겹친다"는 서술은 부정확했다 —
        // 서쪽만 실제 불일치다. 서쪽: Line_Pole_L(Doc x3.4~5.4·y86.6~88.6)의 최근접 코너
        // (5.4,88.6)와 줄 서단(Doc x=10,y=96.5) 사이 실거리 9.14U(dx4.6·dy7.9) — 안 닿는다.
        // 동쪽: Line_Pole_R(Doc x87~89·y95.5~97.5)의 y범위가 줄 y범위(96~97)를 통째로 포함하고,
        // x 간극도 0.10U(기둥 서단 x=87 − 줄 동단 x=86.9)뿐이라 사실상 맞닿아 있다 — 불일치가
        // 아니다. 이번 수정은 코드가 참조하는 좌표 소스를 기둥→줄로 바꿨을 뿐 어느 쪽 지형도
        // 옮기지 않았다(철칙: 콜라이더=측정 지형 — 임의로 지형을 옮기지 않는다) — 서쪽 불일치
        // 조치 필요 여부만 map-reviewer/컨트롤타워 몫으로 남긴다.
        GameObject sub = SubGroup(parent, "P4_Clothesline");

        // maxSpan(14) 기준 경간 분할 — chord≈76.9U(계획서 추정치 "76.9U"와 이번엔 실제로 일치한다.
        // 이전 회차의 "84.07U 불일치"는 기둥 좌표를 썼을 때 생긴 문제였다) → 76.9/14=5.49…→
        // 올림 6경간, 구간당 ≈12.82U(<14, 아래 부동소수 여유 루프도 통과).
        const float maxSpanDefault = 14f;
        int segments = Mathf.Max(1, Mathf.CeilToInt(chord / maxSpanDefault));
        while (chord / segments > maxSpanDefault - 0.5f) segments++; // 부동소수 경계 여유.

        ThreadAnchor[] anchors = new ThreadAnchor[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            Vector3 pos = Vector3.Lerp(topL, topR, (float)i / segments);
            ThreadAnchor a = SpawnViaMenu<ThreadAnchor>("Tools/DreamThread/Create Anchor");
            // SpawnViaMenu 자신이 실패를 이미 Debug.LogError로 냈다(R6) — 여기 후속 메시지는
            // "그래서 이후 진행을 중단한다"는 부가 정보라 V3.Warn으로 충분(exitCode는 이미 반영됨).
            if (a == null) { V3.Warn($"Clothesline_Anchor_{i} 생성 실패 — 빨랫줄 배선을 중단한다."); return; }
            a.gameObject.name = "Clothesline_Anchor_" + i + (i == 0 ? "_West" : i == segments ? "_East" : "");
            a.transform.SetParent(sub.transform, true);
            a.transform.position = pos;
            anchors[i] = a;
        }

        for (int i = 0; i < segments; i++)
        {
            ThreadBridge bridge = SpawnViaMenu<ThreadBridge>("Tools/DreamThread/Create Rope Bridge");
            if (bridge == null) { V3.Warn($"Clothesline_Bridge_{i + 1} 생성 실패 — 이후 경간을 중단한다."); return; }

            // 생성기가 만든 임시 앵커 2개는 우리 실앵커로 갈아끼우고 지운다(불필요한 잔재 방지).
            ThreadAnchor placeholderA = bridge.anchorA;
            ThreadAnchor placeholderB = bridge.anchorB;
            bridge.anchorA = anchors[i];
            bridge.anchorB = anchors[i + 1];
            if (placeholderA != null) Object.DestroyImmediate(placeholderA.gameObject);
            if (placeholderB != null) Object.DestroyImmediate(placeholderB.gameObject);

            bridge.gameObject.name = $"Clothesline_Bridge_{i + 1}";
            bridge.transform.SetParent(sub.transform, true);
            // R3+Q4 반려: segmentCount는 [SerializeField] 아닌 순수 public int 필드다
            // (ThreadBridge.segmentCount 필드) — 배선 코드에서 직접 낮출 수 있다. 기본값 12를
            // 그대로 두면 경간마다 Seg_0..11 각각 키네마틱 Rigidbody가 붙어(ThreadBridge.BuildSpan())
            // 6경간×12=72개가 되어 공통규칙 §3 "구역 Rigidbody ≤40개"를 넘는다(직전 회차 수치
            // 기준으로는 7경간×12=84였다 — 이번 R2로 6경간이 되며 이미 72로 줄었지만 그래도
            // 초과). segmentCount=6으로 낮춰 6경간×6=36≤40으로 맞춘다 — 필드가 공개돼 있지
            // 않았다면 상신이 필요했을 사안이나, 이번엔 필드가 있어 직접 반영한다.
            bridge.segmentCount = 6;
            // sagPerWeight(0.55)·maxSpan(14) 전부 기본값 유지 — 지시: "sagPerWeight 기본 0.55".
        }

        V3.Log($"Clothesline: 줄 오브젝트 기준 실거리 {chord:0.0}U를 {segments}경간(구간당 ≈{(chord / segments):0.0}U, " +
               "경간당 segmentCount=6)으로 분할했다 — 앵커 기준을 기둥 상단 대각에서 실제로 보이는 " +
               "Clothesline 오브젝트로 교체(R2). [C1 정정] 서쪽 기둥(Line_Pole_L)만 줄과 실제로 " +
               "안 닿는다(9.14U) — 동쪽(Line_Pole_R)은 y포함·x간극 0.10U로 사실상 맞닿아 불일치가 " +
               "아니다(위 주석·회신 참고).");
    }

    // ── P2 실 게이트 (후드 진입부) ───────────────────────────────────────────────────

    private static void WireP2Gate(GameObject parent)
    {
        const string markerName = "P2_실게이트3.0_마커(후드 진입부)";
        GameObject marker = GameObject.Find(markerName);
        if (marker == null)
        {
            // [승격, map-reviewer 24차 항목5/컨트롤타워 판정] 존재-확인 실패도 무인 검증
            // exitCode에 반영되게 Debug.LogError로 올린다.
            Debug.LogError($"[KitchenMapV3] {markerName}을 찾지 못했다 — P2 배선을 건너뛴다.");
            return;
        }
        Renderer rend = marker.GetComponent<Renderer>();
        if (rend == null) { V3.Warn($"{markerName}에 렌더러가 없어 bounds를 못 읽었다."); return; }
        Bounds b = rend.bounds;

        GameObject sub = SubGroup(parent, "P2_Gate");

        // AABB "양 끝"(진행축 X 기준 — 마커 폭 3.0이 그대로 hangWeightThreshold 3.0 네모 거부
        // 스펙의 물리적 폭이다)에 앵커 하나씩.
        Vector3 endW = new Vector3(b.min.x, b.max.y, b.center.z);
        Vector3 endE = new Vector3(b.max.x, b.max.y, b.center.z);

        ThreadAnchor a1 = SpawnViaMenu<ThreadAnchor>("Tools/DreamThread/Create Anchor");
        if (a1 != null)
        {
            a1.gameObject.name = "P2_GateAnchor_W";
            a1.transform.SetParent(sub.transform, true);
            a1.transform.position = endW;
        }

        ThreadAnchor a2 = SpawnViaMenu<ThreadAnchor>("Tools/DreamThread/Create Anchor");
        if (a2 != null)
        {
            a2.gameObject.name = "P2_GateAnchor_E";
            a2.transform.SetParent(sub.transform, true);
            a2.transform.position = endE;
        }

        // hangWeightThreshold=3.0(네모 거부) 확인 — DreamThreadController.hangWeightThreshold 필드 기본값 인용,
        // 스펙과 일치하므로 손대지 않는다(생성기 EnsureController()가 이미 하나를 보장했다).
        DreamThreadController ctrl = Object.FindObjectOfType<DreamThreadController>();
        if (ctrl != null && !Mathf.Approximately(ctrl.hangWeightThreshold, 3.0f))
            V3.Warn($"DreamThreadController.hangWeightThreshold={ctrl.hangWeightThreshold} " +
                    "(기본 3.0이 아니다) — P2 실 게이트 스펙(3.0)과 다르다. 확인 필요.");
    }

    // ── P3 투석기 (efk_pro_develop/Assets/CatapultSystem 읽기전용 사본, 사용자 결정 2026-09-09
    // — 저장소 경계를 develop까지 확장, KitchenMapV3/Assets/CatapultSystem에 diff 0 반입 완료) ──
    // [컨트롤타워 결정 2026-09-09, TO_코워크 [#2] 5항] 사본 반입은 유지하되 배선은 Build All
    // 체인에서 뺐다 — map-reviewer 26차 검문 반려(R1~R8: 겹침 23쌍·질량150 MassAudit ❌·뒷마당
    // RB 41>40 등, 상신 판정 전) 때문에 기본 OFF다. 아래 전용 메뉴로만 수동 실행한다.

    [MenuItem("Tools/KitchenMapV3/2c. Wire Catapult (실험 — 기본 OFF, Audit 깨짐)", false, 5)]
    public static void WireCatapultMenu()
    {
        // [R2 반영, map-reviewer 28차] 마커 존재 확인을 V3.Root()·그룹 생성보다 먼저로 옮긴다 —
        // 이전엔 이 확인이 WireCatapult() 안에만 있어, Build All 없이 2c만 눌러 마커가 없는
        // 상태로 실행하면 V3.Root()가 새 루트를("KitchenMapV3_Blockout") 만들고 그 아래 빈
        // "V3_Gimmicks" 그룹까지 만든 뒤에야 실패해 잔여물 2개(빈 root+빈 그룹)가 남았다
        // (CleanupResidue는 SpawnViaMenu 진입 시점부터만 감시해 이 둘은 못 지운다). 마커가
        // 없으면 root·그룹 어느 것도 만들지 않고 즉시 return한다.
        const string markerNameCheck = "Catapult_건조대_자리(방향고정:창틀·서측)";
        if (GameObject.Find(markerNameCheck) == null)
        {
            Debug.LogError($"[KitchenMapV3] {markerNameCheck}을 찾지 못했다 — 2 Build All 먼저 실행해라.");
            return;
        }
        Debug.LogWarning("[KitchenMapV3] 검문 26차: 겹침 23쌍·질량 150·RB 41 — 상신 판정 전 실험 전용.");
        // [A2 반영, map-reviewer 28차] 2c 실행 시 사본 .mat 7종이 URP로 재기록되는 부작용
        // (09-06 [결정]으로 diff 예외 처리됨)을 경고에 명시 — 이전엔 경고 문구·메뉴 이름 어디에도
        // 없었다.
        Debug.LogWarning("[KitchenMapV3] 사본 CatapultSystem/Materials .mat 7종이 URP로 재기록됨(09-06 [결정] 예외).");
        GameObject root = V3.Root();
        Transform existing = root.transform.Find(GroupName);
        GameObject group = existing != null ? existing.gameObject : new GameObject(GroupName);
        if (existing == null) group.transform.SetParent(root.transform, false);
        WireCatapult(group);
    }

    /// <summary>
    /// 정식 생성기(`Tools/Catapult/Create Catapult`, `CatapultMenuItem.cs` 유일한 MenuItem)를 한 번
    /// 호출해 투석기 전체(받침대·바퀴·트레슬·팔·버킷·조향 손잡이)를 만들고, V3_Build.cs의
    /// "Catapult_건조대_자리(방향고정:창틀·서측)" 마커(x6~20·y100~108·z0~6, Shell() L429) 위치로
    /// 옮긴다. 마커가 없으면 배선하지 않는다(지시 원칙).
    ///
    /// [중요 — 구현하지 않고 검토 요청으로만 남긴 것]
    /// 1) **역할 매핑 불일치(가장 큰 문제).** RELAY_병합본_1of4.md:157(B17)의 설계는 "①네모가
    ///    발판에 올라 장력 축적 ②세모가 실로 사출각 고정 ③구가 바구니 탑승 → 카운트다운 발사"다.
    ///    그런데 저장소 코드는 역할이 전부 다르게 하드코딩돼 있다(역할 게이트 grep 확인) —
    ///    `CatapultBucket`의 HandleBoardInput()·OnTriggerEnter()·OnTriggerStay() 셋 다 버킷
    ///    탑승·발사 대상을 **정육면체 전용**으로 거부하고,
    ///    `CatapultLoadController.TryConnect()`는 장전(당김줄 연결)을 **정사면체 전용**으로 거부하며,
    ///    `CatapultSteerHandle.TryDock()`은 조향석 도킹을 **구 전용**으로 거부한다. 즉 코드는
    ///    "정육면체가 탄다 · 정사면체가 당긴다 · 구가 조향한다"인데 설계는 "구가 탄다 · 네모가
    ///    발판을 밟는다 · 세모가 조준한다"다 — 탑승자부터 서로 다르다. 사본을 고치면 diff 0
    ///    원칙(철칙 1) 위반이라 코드를 바꾸지 않았고, 판단도 이 파일에서 하지 않았다 — 당시
    ///    컨트롤타워 판정 대기 항목이었고, 아래 [정정]에서 종결됐다(설계를 코드에 맞추는 쪽).
    ///    **[정정 2026-09-09, 30차 이월/컨트롤타워 확정]** Q1 3역할(네모 탑승·발사, 세모 장전,
    ///    구 조향) 전부 코드 정본으로 종결 — "발사되는 도형만" 확정이었던 이전 09-09 서술을
    ///    대체한다. 남은 것은 🔒H2 P3 문면 재검토·릴레이 정정 통지뿐이다(로직은 이 파일에서
    ///    바꾸지 않았다 — 위 배치·배선은 여전히 생성기 기본값 그대로).
    /// 2) **알려진 미해결 버그 다수(협업/03_질문함.md PR-54 검토, 2026-08-06).** 최신 커밋
    ///    (`8ff81ff`, 이번에 반입한 사본과 동일) 기준으로 (a) `CatapultSteerHandle.OnDisable()`이
    ///    없어 투석기가 비활성화/파괴될 때 도킹된 구의 부모·isKinematic·ExternallyDriven 복구가
    ///    보장되지 않는다(원래 P1, 최신 커밋에도 그대로 — 협업/03_질문함.md:295 "여전히 없어"),
    ///    (b) 조향 도킹이 `growMultiplier`를 1.5로 덮어쓴 뒤 원래값을 복원하지 않는다(원래 P1로
    ///    지적됐으나, 같은 문서 293행에서 "작성자·사용자가 의도한 동작으로 승인한 전제에서는
    ///    결함으로 재분류하지 않는다"고 판정 — 남은 것은 "PRD 문서화 누락"뿐). 발사 중 재입력
    ///    소프트락(원래 P0)은 해당 커밋에서 고쳐졌다(확인함, `CatapultArm.Fire()`의 State==Launching 가드).
    /// 3) **조준 방향([TBD]) — 후보 실좌표는 있다, 확정은 상신.** "창틀"이라는 이름의 오브젝트
    ///    (목표 = 실내 재진입구 창틀, RELAY_병합본_1of4.md:156)는 없지만, 대응 후보는 실재한다 —
    ///    `WindowSill_창턱12.0(넘는턱)`(V3_Build.cs:95, doc x9.6~30.4·y84.2~85·z11.5~12)과 그
    ///    위 창 개구(doc x10~30·z12~20, V3_Build.cs:22~23·83) (map-reviewer 26차 검문 실측 —
    ///    이전 판 "전수 grep 0건"은 사실오류, JSON에도 Window_Sill·Window_Glass가 있다). 생성기
    ///    기본 방향(회전 0)의 발사 벡터를 손계산하면 이미 대략 그 방향을 향한다(map-reviewer 26차:
    ///    dir=AngleAxis(50°,right)*(−forward)=(0,+0.766,−0.643)=doc −Y·상향, x13은 개구
    ///    x10~30 안). 그래도 yRot을 이 좌표에 맞춰 명시적으로 재계산할지, 생성기 기본값을 그대로
    ///    확정으로 볼지는 이 자리에서 판정하지 않는다 — 조준 확정은 컨트롤타워 상신.
    /// 4) **DryRack_건조대받침·DryRack_봉_1~4 실좌표 콜라이더와 물리적으로 겹친다.** 마커와 정확히
    ///    같은 자리(x6~20·y100~108, Shell() L423~425)에 이미 진짜 콜라이더가 있다 — B16 대사
    ///    "빨래건조대 = 투석기"(RELAY_병합본_1of4.md:156)로 보아 이 건조대가 투석기의 자리표시자였던
    ///    것으로 보이지만, 확정 지시가 없어 기존 콜라이더를 지우거나 NoColl로 바꾸지 않았다 —
    ///    Audit ②(비kinematic Rigidbody↔임의 콜라이더 겹침 0) 위반이 사실상 확정적이다. 조치안:
    ///    (a) DryRack_* 콜라이더를 NoColl 마커로 전환, (b) 투석기를 건조대 자리 밖으로 재배치,
    ///    둘 다 판단 대상.
    /// 5) **🔒H4 저촉 여부 — 상신 중, 자체 판정 안 함.** 문면이 두 갈래다: RELAY_병합본_1of4.md:689·
    ///    2of4:400 "뒷마당에 물통 외 질량 2.75 이상 가동 프롭을 두지 않는다"(뒷마당 전역 금지로
    ///    읽으면 정면 충돌 — 투석기 루트 Rigidbody 질량 150, `CatapultMenuItem.CreateCatapult()`가 뒷마당
    ///    doc y100~108에 놓인다) vs 1of4:800 "각 봉우리 무게판 반경 내"(반경 한정으로 읽으면
    ///    거리가 관건). 최근접 무게판은 PlateA(V3_Build.cs:399, x46~52·y104~108)이고 실거리는
    ///    **28.71U**(투석기 동단 기준, map-reviewer 26차 검문 실측 — 이전 판 "58U 이상"은 판A를
    ///    빠뜨린 오기였다). 어느 문면을 적용할지·28.71U가 충분한지는 이 자리에서 판정하지 않는다
    ///    — 컨트롤타워 상신 중.
    /// 6) **[검증] 수치 변경 없음 — "장치 파라미터 수치 없음"과 "관문 조건 있음"은 다른 말이다.**
    ///    이 함수는 위치·부모만 재대입하고, 생성기가 만든 필드 기본값은 전부 그대로 둔다 —
    ///    투석기 자체의 장치 파라미터(사출력·각도·질량 배분 등)는 릴레이·잠금 어디에도 수치
    ///    스펙이 없다(coord-auditor 09-06 회신, 맵2_V3_릴레이설계/코드검산보고서_잔여4건.md:
    ///    19,75~83 "데이터 부재(신규 스펙) 유지"). 다만 이 장치가 맞물려야 할 관문 조건 자체는
    ///    문서에 있다 — 하중판 임계 2.75(RELAY_병합본_1of4.md:751)·실 게이트 3.0(1of4:683)·
    ///    🔒H2 P3-①②③(1of4:840)·🔒H3 개정(RELAY_병합본_3of4.md:127, "창틀 방향 고정·횟수 제한
    ///    없음"). 생성기 산출물에는 발판(하중판)도 사출각 고정 실 장치도 없어(당김앵커+버킷+
    ///    조향링뿐) 이 관문 조건과 현재 배선의 격차는 그대로 남는다(map-reviewer 26차 검문 지적,
    ///    §1 판정 요청 참고) — 임의 수치를 지어내지 않는다(철칙 5).
    /// </summary>
    private static void WireCatapult(GameObject parent)
    {
        const string markerName = "Catapult_건조대_자리(방향고정:창틀·서측)";
        GameObject marker = GameObject.Find(markerName);
        if (marker == null)
        {
            Debug.LogError($"[KitchenMapV3] {markerName}을 찾지 못했다 — 투석기 배선을 건너뛴다.");
            return;
        }

        // [R1 반영, map-reviewer 28차] 진입부에서 기존 "P3_Catapult_Group"을 먼저 지운다(2b
        // Wire() 진입부의 그룹 DestroyImmediate와 동일 패턴 — [정정 2026-09-09, 검문32차 A1]
        // 이 참조를 줄번호로 적으면 편집마다 다시 어긋나 줄번호 대신 함수명 앵커로 고정한다.
        // 재실행 전 결과물을 지우고 처음부터 다시 만들어야 멱등이다).
        // 이전엔 아래 SubGroup()이 항상 새 GameObject를 만들고 기존 그룹을 지우지 않아, 2c를
        // 두 번 누르면 투석기 2대·질량 300(150×2)·동명 "P3_Catapult_Group" 2개가 씬에 남았다
        // (SpawnViaMenu는 "새로 생긴 T 정확히 1개"만 보므로 2회차도 통과해 버렸다 — 비멱등).
        Transform oldSub = parent.transform.Find("P3_Catapult_Group");
        if (oldSub != null) Object.DestroyImmediate(oldSub.gameObject);

        // 생성기(CatapultMenuItem.CreateCatapult())는 "Catapult" 루트에 딱 하나
        // CatapultLoadController를 붙인다 — SpawnViaMenu<T> 판정 대상으로 쓰기에 안전하다(다른
        // Wire* 함수의 ThreadAnchor/ExitWeightPlate 판정과 동일 패턴). 배치모드/무인 검증에선
        // SceneView가 없어 원점(0,0,0)에 생성되므로(CreateCatapult()의 SceneView.lastActiveSceneView
        // null 폴백 분기), 아래에서 마커 위치로 강제 재배치한다.
        // [경미 해소, map-reviewer 26차] 서브그룹 생성을 SpawnViaMenu 성공 확인 이후로 옮겼다 —
        // 이전엔 이 그룹이 실패해도 씬에 빈 채로 남았다(SpawnViaMenu 자신의 실패-정리는 자기보다
        // 먼저 만들어진 부모 그룹까지는 못 지운다).
        CatapultLoadController ctrl = SpawnViaMenu<CatapultLoadController>("Tools/Catapult/Create Catapult");
        if (ctrl == null) return;

        // [경미 해소, map-reviewer 26차] 서브그룹 이름을 "P3_Catapult_Group"으로 바꾸고 root.name은
        // 생성기 이름("Catapult")을 그대로 둔다 — 이전엔 서브그룹과 루트가 둘 다 "P3_Catapult"라
        // 계층에 동명 그룹이 겹쳤다.
        GameObject sub = SubGroup(parent, "P3_Catapult_Group");
        GameObject root = ctrl.gameObject;
        root.transform.SetParent(sub.transform, true);

        Renderer markerRend = marker.GetComponent<Renderer>();
        if (markerRend == null)
        {
            V3.Warn($"{markerName}에 렌더러가 없어 좌표를 못 읽었다 — 원점(0,0,0) 배치로 남는다.");
        }
        else
        {
            // 마커 world bounds 바닥면 중앙(문서 z0 = 지면) — 마커 자체가 이미 V3.Doc()로 변환된
            // 월드 좌표라 재변환하지 않는다(WireMarkerAnchor 등 기존 패턴과 동일).
            Bounds mb = markerRend.bounds;
            root.transform.position = new Vector3(mb.center.x, mb.min.y, mb.center.z);
            // 방향(yRot)은 위 클래스 주석 3)의 이유로 생성기 기본값(0)을 바꾸지 않는다.
        }

        V3.Log($"Wire Catapult: '{markerName}' 위치에 정식 생성기(Tools/Catapult/Create Catapult) " +
               "결과를 배치했다 — 위치·부모만 재대입, 방향·수치 필드는 전부 생성기 기본값(TBD, " +
               "근거 없음) 그대로. 역할 매핑 불일치·기존 버그·DryRack 겹침 등 검토 요청 다수는 " +
               "WireCatapult 클래스 주석과 보고서 참고 — map-reviewer/컨트롤타워 판정 대기.");
    }
}
#endif
