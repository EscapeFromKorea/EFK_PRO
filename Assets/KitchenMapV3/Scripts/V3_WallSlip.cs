using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수정안 ① 래퍼 시제품 (2026-09-06, 사용자 플레이 보고 대응).
///
/// [증상] 세모(정사면체)로 서랍 계단(Box_Step, 턱 0.96U) 앞에 붙어 정지하면 점프가 먹지 않고,
/// 뒤로 살짝 뺐다가 다시 누르면 풀린다. V2 맵 다른 구조물에서도 동일 — 도형·계단 종류에 무관한
/// 공통 증상으로 보고됨.
///
/// [가설] 정지 마찰이 원인이다. 벽(수직면)에 몸을 붙인 채 전진 입력을 유지하면(PlayerMover의
/// LegacyVelocityFixedUpdate()가 접지 중 velocity를 그대로 "대입"한다 — PlayerMover.cs)
/// 콜라이더가 벽면에 계속 눌리고, 그 접촉의 정지 마찰이 몸을 벽에 붙잡는다. 이때 접지 판정
/// (PlayerGroundContact — SphereCast·OnCollisionStay 모두 "위를 향한(normal.y&gt;0.5) 면"만
/// 인정, PlayerGroundContact.groundNormalThreshold 필드 — FixedUpdate()의 SphereCast 분기·
/// OnCollisionStay() 양쪽에서 참조)이 벽면(수직면, normal.y≈0)만 계속 잡으면 바닥
/// 접촉을 못 잡아 접지가 false로 떨어진다 — PlayerJump.FixedUpdate()의
/// `if (!IsGrounded()) return;`에 걸려 점프 입력 자체가 무시된다. 뒤로
/// 빼면 벽 접촉이 끊기고 중력으로 살짝 가라앉으며 SphereCast가 다시 바닥을 잡아(코요테 유예 —
/// PlayerGroundContact.groundedGraceTime 필드·IsGrounded 프로퍼티) 접지가 복구되고 점프가
/// 다시 먹는다 — 사용자가 보고한 "뒤로 빼야 풀린다" 증상과 정확히 들어맞는다.
///
/// [수정안 ①] 벽(수직면) 접촉 중이거나 접지가 아닌 순간에는 이 오브젝트(및 자식)의 모든
/// 콜라이더 물리 머티리얼을 무마찰(마찰 0, frictionCombine=Minimum)로 바꿔 벽면 정지 마찰이
/// 몸을 붙잡지 못하게 한다. 접지 중이고 벽 비접촉이면 원본 마찰로 되돌려 평소 이동 그립
/// (구 0.1/네모 0.5/세모 0.8 — PlayerShapeStats.friction 필드, PlayerObjectMenuItem.
/// LoadOrCreateStats()의 Sphere/Tetrahedron/Cube 케이스)을 그대로 보존한다. 사용자 승인 전에는
/// 실제 플레이어(V3_Play.cs)에 붙이지
/// 않는다 — 이 컴포넌트는 실험 스크립트(V3_PhysLab.cs)가 AddComponent로만 붙인다.
///
/// [Play Mode 자동 경로] Rigidbody 보유 플레이어 오브젝트에 AddComponent하면 그대로 동작한다.
/// OnCollisionStay/OnCollisionExit이 매 물리 스텝의 벽 접촉 여부(wallContact)를 갱신하고,
/// FixedUpdate가 PlayerShapeController.IsGrounded()(있으면)를 읽어 Tick()을 호출한다.
///
/// [에디트모드 물리실험 연동] V3_PhysLab.cs는 Edit Mode에서 Physics.Simulate()로 직접 물리
/// 스텝을 돌린다. Update/FixedUpdate/OnCollisionStay는 ExecuteInEditMode가 없는 일반
/// MonoBehaviour에서 Edit Mode 중 전혀 호출되지 않는다(PlayerMover의 Update/FixedUpdate가 Edit
/// Mode에서 안 도는 것과 같은 이유 — 에디터가 스크립트 실행 루프 자체를 돌리지 않음, Play Mode
/// 전용). 그래서 하네스는 자신이 직접 계산한 wallContact·grounded 값을 Tick(bool,bool)에 넘겨
/// 같은 SwapMaterials 로직을 강제로 구동한다 — Play Mode 자동 경로와 에디트모드 하네스 경로는
/// Tick() 호출 이후 로직이 완전히 동일하다(코드 분기 없음).
///
/// [정정 2026-09-09, 검문25차 (라)] "Awake 이후 생명주기 콜백이 전혀 호출되지 않는다"던 이전
/// 서술은 Awake 자체는 호출된다고 전제하고 있었다 — 사실과 다를 수 있다. Editor 스크립트
/// (V3_PhysLab.cs)가 강제로 도는 실행 흐름 안에서 GameObject.AddComponent&lt;V3_WallSlip&gt;()를
/// 호출할 때, 그 Awake가 같은 프레임에 동기적으로 실행된다는 보장은 없다(유력 가설 — 확정은
/// 유니티 1회 실행·계측으로만 가능하다, "모르겠다"에 가깝다). Awake가 지연되거나 스킵되면
/// CacheColliders·BuildFrictionlessMaterial이 안 돌아 tracked가 비고, SwapMaterials가 대상
/// 콜라이더 0개로 사실상 무동작이 된다 — 실험에서 관측된 "래퍼 ON/OFF 결과 완전 동일"의 유력한
/// 원인이다. 그래서 하네스는 AddComponent 직후 아래 <see cref="Initialize"/>를 명시적으로 호출해
/// 이 불확실성 자체를 없앤다(멱등 — Awake가 나중에 정상적으로 불려도 다시 캐싱할 뿐 부작용 없음).
///
/// [설계 수정 2026-09-09, 컨트롤타워 결정 — 검문32차 D1 기전 + 3회차 실측 복원스왑=1로 확정]
/// Initialize() 명시 호출로 위 불확실성이 사라진 뒤에도 래퍼 ON이 세모(a) 등 다수 케이스에서
/// 여전히 핀닝됐다 — 원인은 <see cref="Tick"/>의 복원 조건 자체였다. 3높이 프로브(검문31차 지시)로
/// 벽 상실 임계가 도형 무관 "발밑 z=단높이−0.05"로 좁아졌지만, 그 순간에도 접지 판정은 코요테
/// 유예 0.1초 때문에 여전히 true다(SphereCast 최종 접지 상실 시각과의 간격이 세모 0.037~0.079초·
/// 네모/구 0.021~0.089초 — 컨트롤타워 손계산, map-reviewer.md §1 #32 32차 핵심). 즉 "벽 상실 직후
/// 아직 공중(코요테 접지)"인 프레임에 wallContactNow=false·grounded=true가 되어 Tick이 마찰을
/// 원본으로 복원시키고, 상승 중이던 몸이 그 복원된 마찰에 붙잡혀 핀닝될 수 있다 — [정정 2026-09-09
/// #37, map-reviewer 33차 R1] "restoreSwapWhileRising(상승중 복원스왑 횟수)=1인 케이스는 전부
/// ✗로 확인돼 이 기전이 실측으로 확정됐다"던 원래 서술은 역방향 오류였다: 물리실험_2026-09-09_
/// 1805.json 전수 재현 결과 ✗ 케이스는 전부 rsr≥1이지만, rsr≥1 20건 중 7건은 통과(네모 ON 통과
/// 전부)라 rsr≥1은 핀닝의 필요조건일 수 있어도 충분조건은 아니다. 실제 핀닝 3중 시그니처(maxZ가
/// 단 상단 근처에서 정지 + 최종x가 출발 벽면에 고정 + rsr≥1)는 [정정 2026-09-09, 검문34차 정정 —
/// 33차 자신의 목록이 누락이었다] 5건이 아니라 6건이다 — 세모 0.8 ON a
/// (maxZ 0.802·최종x 2.33)·세모 0.8 ON b(0.801·2.33)·세모 0.96 ON a(1.056·2.33)·세모 1.0 ON a
/// (1.050·2.33)·네모 0.5 ON a(0.500·2.70)·세모 0.5 ON b(maxZ 0.461·최종x 2.35·rsr 1, 33차 목록에서
/// 빠져 있었다). 수정: <see cref="Tick"/>의 무마찰 유지 조건에
/// "상승 중(Rigidbody 수직속도 &gt; 0.05)이면 벽 비접촉·접지 상태와 무관하게 무마찰을 유지한다"를
/// OR로 추가했다 — 상승이 끝나(수직속도 ≤ 0.05) 정점 근처이거나 하강할 때만 원래 조건(벽 비접촉
/// AND 접지)으로 복원 판단을 되돌린다. 히스테리시스 시간 상수는 도입하지 않는다(최소 수정 원칙).
///
/// [신규 2026-09-09, GPT검토패키지_2026-09-09 지적3·컨트롤타워 재현됨] 원래 이 컴포넌트는
/// OnDisable/OnDestroy가 없었다 — 무마찰 상태(currentlyFrictionless=true)인 도중 이 컴포넌트를
/// 끄거나(enabled=false) 제거(RemoveComponent/파괴)하면, 그 순간 콜라이더에 물려 있던
/// frictionless 재질이 그대로 남아 원본 마찰(구 0.1/네모 0.5/세모 0.8)이 영영 안 돌아온다.
/// <see cref="OnDisable"/>·<see cref="OnDestroy"/>가 SwapMaterials(false) 경로를 재사용해 원복하고,
/// OnDestroy는 추가로 생성된 frictionless PhysicMaterial 자체도 파괴한다(에디터 모드
/// DestroyImmediate·Play 모드 Destroy로 분기 — Application.isPlaying 기준). SwapMaterials에는
/// tracked 콜라이더의 null 가드를 추가했다 — 이 컴포넌트가 루트에 있고 콜라이더는 자식에 있어,
/// GameObject 전체 파괴(DestroyImmediate(root) 등) 경로에서 자식 콜라이더가 이 컴포넌트의
/// OnDisable/OnDestroy보다 먼저 파괴돼 있을 가능성을 배제할 수 없기 때문이다(Unity가 같은
/// 파괴 트랜잭션 안의 컴포넌트 간 OnDestroy 순서를 문서로 보장하지 않는다). [하네스 이중 파괴
/// 확인] V3_PhysLab.cs RunCase는 wrapper.FrictionlessMaterial을 Initialize() 직후
/// createdMaterials 리스트에 담아뒀다가 실행 종료 finally에서 각각 `if (mat != null)
/// DestroyImmediate(mat)`로 정리한다 — 이 OnDestroy가 같은 재질을 먼저 파괴해도 Unity
/// Object의 오버로드된 `==`가 파괴된 참조를 null로 취급하므로 finally의 null 체크가 그 항목을
/// 건너뛴다(예외 없음, 실제 이중 Destroy 호출이 발생하지 않음 — 정적 코드 검토로 확인, 유니티
/// 실행 실측은 아님).
/// </summary>
[DisallowMultipleComponent]
public class V3_WallSlip : MonoBehaviour
{
    /// <summary>[신규 2026-09-09 #37, map-reviewer 33차 A1] Tick()의 "상승 중" 판정 임계.
    /// V3_PhysLab.cs의 계측(risingNow)도 이 상수를 그대로 참조해 두 값이 어긋나지 않게 한다 —
    /// 이전에는 V3_PhysLab.cs가 하드코딩한 &gt;0f를 따로 썼는데, 그 탓에 0&lt;vy≤0.05 구간(정상
    /// 착지 복원)까지 "상승중 복원"으로 잘못 셌다. Editor(V3_PhysLab.cs)가 Runtime(이 파일)을
    /// 참조하는 방향만 컴파일 가능해 상수는 Runtime 쪽인 여기 둔다.</summary>
    public const float RisingVyThreshold = 0.05f;

    [Tooltip("끄면 항상 원본 마찰만 쓴다(래퍼 비활성 — 실험의 '래퍼 OFF' 상태 재현용).")]
    public bool enabledSlip = true;

    [Tooltip("[Play Mode 자동 경로 전용] 이번 물리 스텝에 벽(수직면) 접촉이 있었는지. " +
             "OnCollisionStay가 채우고 FixedUpdate가 소비 직후 초기화한다. 에디트모드 하네스는 " +
             "이 필드를 안 쓰고 Tick(bool,bool)의 첫 인자로 직접 넘긴다.")]
    public bool wallContact;

    [Tooltip("이 값 미만이면 접촉면을 '벽(수직면)'으로 본다 — PlayerMover.DeflectAirMoveFromWall()과 " +
             "동일 임계(|normal.y|&lt;0.5).")]
    public float wallNormalYThreshold = 0.5f;

    /// <summary>현재 무마찰 상태인지(읽기 전용 — 실험 로그·디버그용).</summary>
    public bool IsFrictionlessNow => currentlyFrictionless;

    /// <summary>Initialize()가 만든 무마찰 PhysicMaterial(읽기 전용) — 하네스가 실행 종료 시
    /// DestroyImmediate로 정리할 수 있도록 노출한다(검문25차 A5, PhysicMaterial 잔존 방지).</summary>
    public PhysicMaterial FrictionlessMaterial => frictionless;

    /// <summary>[신규 2026-09-09, 검문27차 R4] 현재 추적 중인 콜라이더 개수(읽기 전용). swapCount는
    /// tracked가 0개여도 SwapMaterials 호출 자체(대상 콜라이더 없이 currentlyFrictionless 상태만
    /// 바뀜)로 증가할 수 있어 "swapCount>0 = 래퍼가 실제 콜라이더에 작용했다"는 증거가 못 된다 —
    /// 하네스가 이 값을 케이스별로 1회 기록해 스왑 횟수와 나란히 유효성을 검증할 수 있게 한다.</summary>
    public int TrackedCount => tracked.Count;

    private PlayerShapeController shapeController;
    private Rigidbody cachedRigidbody;
    private bool rigidbodyMissingWarned;
    private bool currentlyFrictionless;
    private bool wallContactAccum;

    private readonly List<Collider> tracked = new List<Collider>();
    private readonly Dictionary<Collider, PhysicMaterial> knownOriginal = new Dictionary<Collider, PhysicMaterial>();
    private PhysicMaterial frictionless;

    void Awake()
    {
        Initialize();
    }

    /// <summary>Awake와 동일한 초기화(멱등 — 몇 번을 불러도 안전). Edit Mode 하네스
    /// (V3_PhysLab.cs)가 AddComponent 직후 이 메서드를 명시적으로 호출해, Awake가 그 프레임에
    /// 동기 실행된다는 보장이 없는 문제(클래스 상단 주석 [정정 2026-09-09] 참고)를 우회한다.
    /// 이미 초기화된 상태에서 다시 불려도(예: Awake가 나중에 정상적으로 실행됨) frictionless
    /// 재질을 중복 생성하지 않는다. [정정 2026-09-09, 검문27차 R5] tracked 재캐싱은 현재
    /// 무마찰 상태가 아닐 때만 한다 — 무마찰인 동안 다시 캐싱하면 knownOriginal이 frictionless
    /// 자신을 "원본"으로 잘못 기록해 이후 원복(SwapMaterials(false))이 실패한다.</summary>
    public void Initialize()
    {
        if (shapeController == null) shapeController = GetComponent<PlayerShapeController>();
        // [신규 2026-09-09, 컨트롤타워 결정 — 검문32차 D1] Tick()의 "상승 중 복원 금지" 조건이
        // 읽을 Rigidbody.velocity.y를 여기서 캐싱한다. 없으면(이 컴포넌트가 Rigidbody 없는
        // 오브젝트에 잘못 붙은 경우) 그 조건만 생략하고 1회 경고한다 — Tick() 참고.
        if (cachedRigidbody == null) cachedRigidbody = GetComponent<Rigidbody>();
        if (frictionless == null) BuildFrictionlessMaterial();
        // [정정 2026-09-09, 검문27차 R5] currentlyFrictionless인 동안 CacheColliders를 다시 돌리면
        // knownOriginal에 "지금의 sharedMaterial"(=frictionless 자신)이 원본인 것처럼 잘못
        // 캐싱된다 — 무마찰이 아닐 때만 재캐싱해 이 오염을 막는다.
        if (!currentlyFrictionless) CacheColliders();
    }

    void FixedUpdate()
    {
        // Play Mode 전용 자동 경로 — 에디트모드 하네스(V3_PhysLab)는 이 메서드를 타지 않고
        // Tick()을 직접 호출한다(클래스 상단 주석 참고).
        bool grounded = shapeController != null && shapeController.IsGrounded();
        Tick(wallContactAccum, grounded);
        wallContact = wallContactAccum;
        wallContactAccum = false;
    }

    void OnCollisionStay(Collision collision)
    {
        foreach (ContactPoint c in collision.contacts)
        {
            if (Mathf.Abs(c.normal.y) < wallNormalYThreshold)
            {
                wallContactAccum = true;
                break;
            }
        }
    }

    /// <summary>[신규 2026-09-09, GPT검토패키지_2026-09-09 지적3] 컴포넌트가 꺼질 때(enabled=false,
    /// 또는 GameObject 비활성화) 무마찰 상태였다면 원본 마찰을 복원한다 — SwapMaterials(false)
    /// 경로를 그대로 재사용(로직 중복 없음). 무마찰이 아니었다면 아무것도 하지 않는다.</summary>
    void OnDisable()
    {
        if (currentlyFrictionless)
        {
            SwapMaterials(false);
            currentlyFrictionless = false;
        }
    }

    /// <summary>[신규 2026-09-09, GPT검토패키지_2026-09-09 지적3] 컴포넌트/오브젝트가 파괴될 때도
    /// 같은 원복을 보장한다 — OnDisable이 먼저 불렸다면(Unity가 파괴 전 OnDisable→OnDestroy 순으로
    /// 부르는 경로) currentlyFrictionless가 이미 false라 이 블록은 멱등하게 스킵된다. 이어서 이
    /// 컴포넌트가 생성한 frictionless PhysicMaterial 자체를 파괴한다(Initialize()가 만든 별도
    /// 에셋 — GameObject 파괴로 자동으로 없어지지 않는다). 에디터 모드/Play 모드에서 파괴 API가
    /// 다르므로(DestroyImmediate는 Play 모드에서 경고를 낸다) Application.isPlaying으로 분기한다.</summary>
    void OnDestroy()
    {
        if (currentlyFrictionless)
        {
            SwapMaterials(false);
            currentlyFrictionless = false;
        }
        if (frictionless != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(frictionless);
            else UnityEngine.Object.DestroyImmediate(frictionless);
            frictionless = null;
        }
    }

    /// <summary>SwapMaterials 판단 1회 — Play Mode FixedUpdate 자동 경로와 에디트모드 하네스가
    /// 공유하는 유일한 진입점(로직 분기 없음, Play Mode 경로도 이 메서드를 거치므로 아래 조건이
    /// 자동으로 동일 적용된다 — 별도 분기 불필요). wallContactNow || !grounded || (상승 중, 아래
    /// 참고) 이면 무마찰, 그 외(접지 중·벽 비접촉·상승 중이 아님)면 원본으로 복원한다.
    ///
    /// [설계 수정 2026-09-09, 컨트롤타워 결정 — 검문32차 D1 기전 + 3회차 실측(복원스왑(상승중)=1인
    /// 케이스 전부 ✗)으로 확정] 원래 조건(wallContactNow || !grounded)만으로는, 벽을 막 벗어난
    /// 순간에도 코요테 유예(0.1초)로 접지가 아직 true라 "벽 비접촉 AND 접지"가 성립해 버려 상승
    /// 도중에 마찰이 원본으로 복원되고 그 마찰이 남은 상승을 흡수해 핀닝됐다. Rigidbody 수직속도가
    /// 0.05 초과(상승 중)면 벽 접촉·접지 상태와 무관하게 무마찰을 유지해 이 복원을 막는다 —
    /// 히스테리시스 시간 상수는 도입하지 않는다(최소 수정).</summary>
    public void Tick(bool wallContactNow, bool grounded)
    {
        // 원본이 아직 "무마찰"로 바뀌지 않은 동안에만 최신값을 계속 갱신해둔다 — 이 컴포넌트가
        // PlayerShapeIdentity.Start()(도형별 마찰 물리 머티리얼을 나중에 배정)보다 먼저
        // AddComponent 되어도, 그 배정이 실제로 일어난 뒤 첫 Tick에서 올바른 원본을 캡처한다.
        if (!currentlyFrictionless) RefreshKnownOriginals();

        bool risingFast;
        if (cachedRigidbody != null)
        {
            risingFast = cachedRigidbody.velocity.y > RisingVyThreshold;
        }
        else
        {
            // Rigidbody가 없으면(설계상 있어야 하는 오브젝트에 붙었는데 아직 없는 경우 등) 이
            // 조건만 생략한다 — 나머지(wallContactNow || !grounded) 판단은 그대로 유지된다.
            risingFast = false;
            if (!rigidbodyMissingWarned)
            {
                Debug.LogWarning("V3_WallSlip.Tick: Rigidbody가 없어 '상승 중 복원 금지' 조건을 " +
                    "생략한다(검문32차 D1 대응 미적용) — " + (name ?? "(이름 없음)"));
                rigidbodyMissingWarned = true;
            }
        }

        bool wantFrictionless = enabledSlip && (wallContactNow || !grounded || risingFast);
        if (wantFrictionless != currentlyFrictionless)
        {
            SwapMaterials(wantFrictionless);
            currentlyFrictionless = wantFrictionless;
        }
    }

    private void RefreshKnownOriginals()
    {
        // [정정 2026-09-10, 컨트롤타워 지시 — SwapMaterials(:274 c==null continue)와 대칭] 이
        // 루프도 같은 파괴 트랜잭션에서 자식 콜라이더가 이 컴포넌트보다 먼저 파괴됐을 가능성을
        // 배제할 수 없다(클래스 상단 주석 참고). 무가드였다면 tracked[i]가 이미 파괴된 콜라이더일
        // 때 tracked[i].sharedMaterial 접근이 MissingReferenceException을 낸다(map-reviewer 36차
        // §1 지적) — SwapMaterials와 동일하게 null이면 건너뛴다.
        for (int i = 0; i < tracked.Count; i++)
        {
            Collider c = tracked[i];
            if (c == null) continue;
            knownOriginal[c] = c.sharedMaterial;
        }
    }

    private void SwapMaterials(bool frictionlessOn)
    {
        for (int i = 0; i < tracked.Count; i++)
        {
            Collider c = tracked[i];
            // [신규 2026-09-09, GPT검토패키지_2026-09-09 지적3] OnDestroy 경로에서 같은 파괴
            // 트랜잭션 안의 자식 콜라이더가 이 컴포넌트보다 먼저 파괴돼 있을 수 있다(Unity가
            // 컴포넌트 간 OnDestroy 순서를 보장하지 않음) — 그런 콜라이더는 건너뛴다.
            if (c == null) continue;
            if (frictionlessOn)
            {
                c.material = frictionless;
            }
            else
            {
                PhysicMaterial original;
                c.material = knownOriginal.TryGetValue(c, out original) ? original : null;
            }
        }
    }

    private void CacheColliders()
    {
        tracked.Clear();
        knownOriginal.Clear();
        Collider[] all = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < all.Length; i++)
        {
            tracked.Add(all[i]);
            // 없으면(null) 그대로 둔다 — Collider.material=null은 엔진 내장 기본 PhysicMaterial을
            // 재현한다(DynamicsManager.asset m_DefaultMaterial: {fileID: 0} — 이 프로젝트는 커스텀
            // 기본 재질을 지정하지 않았으므로 PhysX 내장 기본값이 곧 "원본"이다).
            knownOriginal[all[i]] = all[i].sharedMaterial;
        }
    }

    private void BuildFrictionlessMaterial()
    {
        frictionless = new PhysicMaterial((name ?? "V3_WallSlip") + "_Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
    }
}
