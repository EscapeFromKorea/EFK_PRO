// 이 파일은 반드시 "Editor" 폴더 안에 위치해야 한다.
//    Editor 폴더 밖에 두면 런타임 빌드 시 UnityEditor 참조로 컴파일 에러가 난다.

using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Catapult > Create Sling Catapult — 기본 투석기(`CatapultMenuItem`)와 완전히 같은
/// 메커니즘(같은 팔 회전 각도, 같은 버킷 캐비티, 같은 조향 손잡이 위치)을 쓰되 겉모습만 "손수레
/// 투석기"에서 "Y자 슬링 프레임"으로 바꾼 리스킨이다(2026-08-31, `docs/PRD/Catapult.md` §8.2 안 A).
///
/// 물리/파묻힘 검산에 관여하는 상수·메서드는 전부 `CatapultMenuItem`의 것을 그대로 재사용한다
/// (internal로 노출돼 있다) — 같은 숫자를 이 파일에 다시 베끼면 한쪽만 고치고 잊는 드리프트가 난다
/// (이 저장소가 카타풀트 26차 개편 내내 씬-코드 드리프트로 고생한 이력, `CatapultSystem/CLAUDE.md`
/// 참고).
///
/// 바뀌는 것 — 전부 콜라이더 없는 순수 시각이거나, 있어도 어떤 검산 공식도 참조하지 않는 순수 장식:
/// - 손수레(바퀴 2개 + X자 트레슬 4개 + 하부 보강대 + 바퀴 축) → 좌대에서 피벗까지 이어지는 두 다리
///   (Y자 프레임)
/// - 2단 테이퍼 각목 팔 + 균형추 → 균형추 없는 둥근 단일 곡대(Capsule)
/// - 각목 손잡이 막대(Rod, Cube) → 둥근 밧줄 느낌의 실린더
/// - 재질 색(밧줄/가죽/목재 톤으로 구분)
///
/// 안 바뀌는 것(물리·게임플레이, `CatapultMenuItem`을 그대로 호출) — 버킷 캐비티 전체(`CreateBucket`,
/// 파묻힘 검산 대상), 조향 손잡이 위치·크기(`CreateSteerRingVisual`), 루트 Rigidbody 설정
/// (`ConfigureRootRigidbody`), 저마찰 재질(`CreateLowFrictionMaterial`). `CatapultArm.restAngle`/
/// `pulledAngle`(그 파일의 필드 기본값)도 이 파일이 전혀 참조하지 않으므로 그대로 공유된다 — 즉
/// 파묻힘 검산·조향 도달거리 재검산이 전혀 필요 없다(치수는 `scale` 하나에 비례해서만 바뀐다).
///
/// **`float scale` 매개변수로 전체 크기를 결정한다(2026-08-31 확장) — 배율 하나만 다르게 넘기면
/// 완전히 다른 크기의 같은 형태가 나온다.** `Create Sling Catapult` 메뉴는 기본 투석기와 같은
/// `CatapultMenuItem.Scale`(3f)을 넘기고, `MiniCatapultMenuItem`(ScalePad 축소 전용 미니 투석기,
/// `docs/PRD/Catapult.md` §8.3 안 B)은 더 작은 배율을 넘겨 이 파일의 `BuildSlingCatapult`를 그대로
/// 재사용한다 — 부품이 적어(바퀴·트레슬·균형추 없음) 축소했을 때 형태가 덜 지저분하다는 순수
/// 실용적 이유로 미니 투석기의 뼈대로 선택됐다.
/// </summary>
public static class SlingCatapultMenuItem
{
    [MenuItem("Tools/Catapult/Create Sling Catapult")]
    private static void CreateSlingCatapult()
    {
        Vector3 origin = ResolveSpawnOrigin();
        BuildSlingCatapult(origin, CatapultMenuItem.Scale, "SlingCatapult");

        Debug.Log("[Catapult] Y자 슬링 프레임 투석기 생성 완료 — 조작법은 기본 투석기와 완전히 " +
                   "동일합니다(정육면체 탑승 / 구 도킹 조향 / 정사면체 장전).");
    }

    // internal — `MiniCatapultMenuItem`도 씬 뷰 중앙 스폰 규칙을 그대로 재사용한다.
    internal static Vector3 ResolveSpawnOrigin()
    {
        return SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
    }

    // internal — Y자 슬링 프레임 전체를 조립한다. `scale`이 모든 치수를 결정한다(`CatapultMenuItem`의
    // scale-비례 공식을 그대로 재사용하므로, 이 값만 다르게 넘기면 파묻힘 검산·조향 도달거리가
    // 원본과 같은 비율로 함께 줄어든다 — 재검산이 필요 없는 이유는 `MiniCatapultMenuItem.cs` 상단
    // 주석 참고). `rootName`은 씬에서 구분하기 위한 GameObject 이름이다.
    internal static GameObject BuildSlingCatapult(Vector3 origin, float scale, string rootName)
    {
        CatapultMenuItem.EnsureMaterialFolder();

        GameObject root = new GameObject(rootName);
        root.transform.position = origin;
        CatapultLoadController loadController = root.AddComponent<CatapultLoadController>();

        // 물리 골격(질량·감쇠·CCD 등)은 배율과 무관하게 기본 투석기와 완전히 동일하다 — 값을
        // 복제하지 않고 같은 메서드를 호출한다. mass가 scale과 무관하게 고정이라는 점(미니 투석기
        // 에서는 상대적으로 더 무거워진다는 뜻)은 `MiniCatapultMenuItem.cs` 상단 TBD 주석 참고.
        Rigidbody rootBody = CatapultMenuItem.ConfigureRootRigidbody(root);
        PhysicMaterial lowFriction = CatapultMenuItem.CreateLowFrictionMaterial();

        float baseTopY = CatapultMenuItem.BaseTopYFor(scale);
        float apexY = CatapultMenuItem.ApexYFor(scale);
        float baseRadius = 0.9f * scale;
        float legThickness = 0.16f * scale;
        float legBaseHalfX = 0.9f * scale;

        CreateBasePlatform(root.transform, lowFriction, baseTopY, baseRadius);
        CreateFrameLegs(root.transform, baseTopY, apexY, legBaseHalfX, legThickness);
        GameObject armPivot = CatapultMenuItem.CreateArmPivot(root.transform, apexY);
        CreateArmVisual(armPivot.transform, CatapultMenuItem.ArmLengthFor(scale), scale);

        // 버킷 캐비티(치수/파묻힘 검산)는 기본 투석기와 완전히 같은 공식(`scale`만 다르게 넘김) —
        // 재질만 다르게 넘긴다.
        Material pouchMat = CatapultMenuItem.LoadOrCreateMaterial("SlingPouch", new Color(0.42f, 0.3f, 0.16f));
        GameObject bucketInner = CatapultMenuItem.CreateBucket(armPivot.transform, scale, out GameObject anchor, pouchMat);

        Vector3 pivotLocal = CatapultMenuItem.SteerHandlePivotLocalFor(scale);
        float ringRadius = CatapultMenuItem.SteerRingRadiusFor(scale);
        float tubeThickness = CatapultMenuItem.SteerRingTubeThicknessFor(scale);
        GameObject steerRing = CreateSteerHandle(root.transform, pivotLocal, ringRadius, tubeThickness, baseRadius, scale);

        CatapultArm arm = armPivot.GetComponent<CatapultArm>();
        arm.armPivot = armPivot.transform;
        arm.aimRoot = root.transform;
        arm.bucket = bucketInner.GetComponent<CatapultBucket>();

        loadController.anchor = anchor.GetComponent<CatapultPullAnchor>();
        loadController.arm = arm;

        CatapultSteerHandle steerHandle = root.AddComponent<CatapultSteerHandle>();
        steerHandle.dockAnchor = steerRing.transform;
        steerHandle.rootBody = rootBody;

        Undo.RegisterCreatedObjectUndo(root, "Create " + rootName);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = root;
        return root;
    }

    // 손수레 상판(Cube, 바퀴 부착 전제) 대신 둥근 좌대(Cylinder) — 바닥 접촉 콜라이더라 저마찰
    // 재질을 그대로 적용한다.
    //
    // [투석기가 공중에 떠 보이던 버그 — 근본 원인] 원통 프리미티브의 기본 콜라이더는
    // `CapsuleCollider`인데, `CatapultMenuItem.CreateWheel`이 이미 문서화한 것과 정확히 같은 함정
    // (클래스 상단 "바퀴 콜라이더" 주석 참고)에 이 좌대도 걸렸다 — 좌대는 반지름(baseRadius)이
    // 두께(baseTopY)보다 훨씬 큰 납작한 원판이라 "world height < 2×world radius"가 항상 성립해
    // Unity가 원통 구간을 0으로 clamp하고 **반지름과 같은 반지름의 완전한 구**가 된다. 그 거대한
    // 구 콜라이더가 바닥에 닿아 멈추면, 루트는 "구 중심이 바닥 위 radius만큼"에서 정지하는데 시각
    // 메시(얇은 원판)는 훨씬 아래(루트 기준 baseTopY*0.5)에 있어 — 결과적으로 좌대~다리~팔~버킷
    // 전체가 (baseRadius − baseTopY*0.5)만큼 공중에 떠 보였다. `CreateWheel`과 똑같이 이 콜라이더를
    // 지우고 원통 메시의 로컬 바운딩 박스와 정확히 같은 `size=(1,2,1)`의 `BoxCollider`로 교체해
    // 해결한다(원형이 사각형으로 근사되는 것은 걸어 다니는 상판 가장자리 정도의 오차라 무해하다).
    private static void CreateBasePlatform(Transform parent, PhysicMaterial groundMaterial, float baseTopY, float baseRadius)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObj.name = "SlingCatapult_Base";
        baseObj.transform.SetParent(parent, false);
        baseObj.transform.localPosition = new Vector3(0f, baseTopY * 0.5f, 0f);
        // 기본 Cylinder는 scale=1일 때 반지름 0.5·높이 2 — 반지름은 *2f, 높이(두께)는 *0.5f로 환산.
        baseObj.transform.localScale = new Vector3(baseRadius * 2f, baseTopY * 0.5f, baseRadius * 2f);
        baseObj.GetComponent<Renderer>().sharedMaterial =
            CatapultMenuItem.LoadOrCreateMaterial("SlingBase", new Color(0.42f, 0.36f, 0.28f));

        Object.DestroyImmediate(baseObj.GetComponent<Collider>()); // CapsuleCollider clamp 문제(위 주석) — 없앤다.
        BoxCollider baseCollider = baseObj.AddComponent<BoxCollider>();
        baseCollider.size = new Vector3(1f, 2f, 1f); // 원통 메시의 로컬 바운딩 박스와 정확히 같다.
        baseCollider.material = groundMaterial;
    }

    // 손수레(바퀴+X자 트레슬)를 걷어내고, 좌대 좌우 가장자리에서 피벗(apexY, armPivot 위치)까지
    // 이어지는 두 다리로 Y자를 만든다 — "새총처럼 두 갈래가 팔을 붙든다"는 인상. 다리 시작 X는
    // 순수 장식 배치라 물리 상수와 무관하다.
    private static void CreateFrameLegs(Transform parent, float baseTopY, float apexY, float legBaseHalfX, float legThickness)
    {
        Material mat = CatapultMenuItem.LoadOrCreateMaterial("SlingFrame", new Color(0.5f, 0.4f, 0.24f));
        CreateLeg(parent, -legBaseHalfX, baseTopY, apexY, legThickness, mat);
        CreateLeg(parent, legBaseHalfX, baseTopY, apexY, legThickness, mat);
    }

    // 좌대 위(baseTopY, x=xBase, z=0)에서 피벗(0, apexY, 0)까지 뻗는 다리 하나. 트레슬의 고정 28°
    // 방식과 달리 다리 시작 X가 자유값이라, 각도를 상수로 박아두지 않고 두 점 사이 벡터에서 직접
    // 역산한다.
    private static void CreateLeg(Transform parent, float xBase, float baseTopY, float apexY, float legThickness, Material mat)
    {
        Vector3 start = new Vector3(xBase, baseTopY, 0f);
        Vector3 end = new Vector3(0f, apexY, 0f);
        Vector3 delta = end - start;
        float length = delta.magnitude;
        float angleZ = -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;

        GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leg.name = "SlingCatapult_Leg";
        leg.transform.SetParent(parent, false);
        leg.transform.localPosition = start + delta * 0.5f;
        leg.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
        leg.transform.localScale = new Vector3(legThickness, length, legThickness);
        leg.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 2단 테이퍼 각목 팔 + 균형추 대신 균형추 없는 둥근 단일 곡대(Capsule) — 새총 갈래 사이를 잇는
    // 단순한 팔 실루엣. 길이는 기본 투석기와 같은 공식(`ArmLengthFor(scale)`)을 재사용한다(버킷
    // 높이 계산이 이 값을 공유하므로 — `CatapultMenuItem.cs` 상단 "버킷 높이 계산" 주석 참고).
    private static void CreateArmVisual(Transform parent, float armLength, float scale)
    {
        Material mat = CatapultMenuItem.LoadOrCreateMaterial("SlingArm", new Color(0.55f, 0.42f, 0.2f));
        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        arm.name = "SlingCatapult_ArmVisual";
        arm.transform.SetParent(parent, false);
        arm.transform.localPosition = new Vector3(0f, armLength * 0.5f, 0f);
        float radius = 0.14f * scale;
        // 기본 Capsule은 scale=1일 때 반지름 0.5·전체 높이 2 — 반지름은 *2f, 높이는 armLength/2f로 환산.
        arm.transform.localScale = new Vector3(radius * 2f, armLength * 0.5f, radius * 2f);
        arm.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // 손잡이 — 각목 Rod(Cube) 대신 둥근 밧줄 느낌의 Cylinder. 위치·고리(Ring) 크기·좌표는 전부
    // `CatapultMenuItem`의 물리 공식을 그대로 재사용한다(도킹 지점을 임의로 옮기지 않기 위해 —
    // 옮기면 `CatapultSteerHandle.dockRange` 기준 도달 가능 범위가 리스킨마다 달라진다).
    private static GameObject CreateSteerHandle(Transform parent, Vector3 pivotLocal, float ringRadius, float tubeThickness, float baseRadius, float scale)
    {
        Material ropeMat = CatapultMenuItem.LoadOrCreateMaterial("SlingRope", new Color(0.6f, 0.5f, 0.3f));

        float ringNearEdgeZ = pivotLocal.z - ringRadius + tubeThickness * 0.5f;
        float rodStartZ = baseRadius; // 좌대 가장자리에서 시작(기본 투석기의 BaseHalfZ 역할).
        float rodLength = ringNearEdgeZ - rodStartZ;

        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = "SlingCatapult_SteerHandle_Rope";
        rod.transform.SetParent(parent, false);
        rod.transform.localPosition = new Vector3(0f, pivotLocal.y, rodStartZ + rodLength * 0.5f);
        rod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 원통 높이축(로컬 Y)을 로컬 Z로 돌려 앞으로 뻗게 한다.
        float ropeRadius = 0.05f * scale;
        rod.transform.localScale = new Vector3(ropeRadius * 2f, rodLength * 0.5f, ropeRadius * 2f);
        rod.GetComponent<Renderer>().sharedMaterial = ropeMat;

        // 고리(Ring) 자신은 기본 투석기와 완전히 같은 공식/메서드로 만든다 — 재질만 다르다.
        return CatapultMenuItem.CreateSteerRingVisual(parent, pivotLocal, ringRadius, tubeThickness, CatapultMenuItem.SteerRingSegmentCount, ropeMat);
    }
}
