using UnityEngine;

/// <summary>
/// 투석기별 장전 컨트롤러(씬 싱글턴 아님, PRD 확정 — 투석기 인스턴스마다 하나씩 붙어 여러 대를
/// 동시에 독립적으로 운용한다). C 입력으로 당김 줄 연결/해제 상태머신을 관리한다.
///
/// [DreamThreadController와의 차이 — ConfigurableJoint 대신 거리→비율 계산]
/// `DreamThreadController`의 F 연결/해제 상태머신·동시 1인 연결 게이트 패턴을 이식하되, 실타래처럼
/// 몸을 매달아 스윙시키지 않는다. 스윙 물리가 필요 없으므로 실타래가 겪은 "로프 물리 안정화가
/// 최상위 리스크"를 애초에 지지 않는다(PRD §3). 연결된 플레이어는 평소처럼 자유롭게 걸어 다니되
/// `maxTetherDistance` 밖으로는 나가지 못한다(아래 "이동 제한").
///
/// [이동 제한 — 2026-10-03, 앵커 XZ 기준 수평 원통형 하드 리밋 조인트]
/// 연결 중에는 정사면체 몸에 월드 고정 `ConfigurableJoint`(x/y/z 같은 선형 리밋)를 달아 앵커 XZ 중심
/// 반지름 `maxTetherDistance` 안에 가둔다 — `DreamThreadController`의 로프 구속과 같은 기법이지만
/// 몸을 `ExternallyDriven`으로 잡지 않으므로 안쪽에서는 `PlayerMover`가 평소처럼 걷게 한다.
///
/// **조인트 앵커의 높이를 매 스텝 몸의 높이에 맞춘다**(`(앵커.x, 몸.y, 앵커.z)`). 앵커 높이 그대로 둔 3D 구로
/// 만들면 앵커가 몸보다 높은 투석기(실측 5.8U 위)에서 수평으로 한계를 밀 때 몸이 구면을 따라 위로 미끄러져
/// 구의 적도(앵커 높이)까지 떠오른다 — 2026-10-03 Loki 실측(vel.y가 0→7.6, 수평 거리 12.0에서 dy≈0).
/// 중심을 몸 높이로 따라가게 하면 구속이 수평 반경만 막고 위아래는 자유롭다. velocity를
/// 직접 깎지 않는 이유: `PlayerMover`가 매 FixedUpdate 속도를 하드 대입하므로 스크립트가 같은 값을
/// 건드리면 순서 경합으로 구멍이 난다(저장소가 여러 번 치른 함정). 조인트는 솔버가 위치를 되돌린다.
/// 몸이 외부에 붙잡힌 동안(`ExternallyDriven` — 복귀 연출·탑승·매달림 / `isKinematic` — 굴리기·벽 부착)에는
/// 조인트를 풀고, 범위 안이면 다시 이어 붙인다. 복귀 순간이동이 조인트에 끌려가 몸이 튕기는 걸 막는다.
/// 그래서 연결 중에도 `PlayerMover.ExternallyDriven`을 세우지 않는다 — 조준자의 이동 자체가 장전
/// 메커니즘의 입력이라 막으면 안 된다(실타래의 매달림과 근본적으로 다른 지점).
///
/// [C 게이트 — "다른 플레이어가 연결 중이면 대기"]
/// `DreamThreadController`가 2026-07-30 입력 게이트 감사에서 고친 것과 같은 이유로, C가 상태를
/// 바꾸는 지점은 반드시 "연결된 당사자가 지금 조작 대상인가"를 확인한다. 이 컨트롤러는 씬 싱글턴이
/// 아니라 투석기 인스턴스마다 있지만, C 입력 자체는 모든 인스턴스가 동시에 읽는 전역 키다 — 그래서
/// 연결된 플레이어가 Tab으로 파킹된 동안 다른 플레이어가 아무 데서나 누른 C가 이 투석기의 연결을
/// 끊어버리는 사고를 막아야 한다. `TryConnect`로 흘려보내면 안 되는 이유도 동일: 그러면 파킹된
/// 플레이어의 참조가 새 플레이어로 덮여 원래 플레이어가 영구히 추적에서 사라진다.
///
/// [겹치는 범위 — 가장 가까운 투석기에만 연결]
/// 투석기를 서로 충분히 떨어뜨려 배치하는 게 기본 전제지만, 혹시 두 투석기의 `connectRange`가
/// 겹치는 위치에서 C를 누르면(모든 컨트롤러가 같은 프레임에 전역 C를 독립적으로 읽으므로) 두
/// 투석기에 동시에 연결되는 사고가 날 수 있었다. `IsNearestCatapult`가 이를 막는다 — 자신보다
/// 가까운 다른 투석기가 범위 안에 있으면 연결을 양보한다.
///
/// [실 시각화 — 2026-08-04 플레이테스트 요청]
/// 연결 중(정사면체가 C로 당김 줄을 걸었을 때, 24차 개편부터 정사면체 전용)에만 플레이어와 당김 앵커 사이에 실이 보이도록
/// `LineRenderer`를 그린다. `DreamThreadSystem`(`ThreadBridge`/`DreamThreadController`)이 이미 쓰는
/// 패턴을 그대로 이식했다(파일은 건드리지 않고 패턴만 참고) — 상태에 따라 `enabled`만 토글하고,
/// 공유 머티리얼을 오염시키지 않도록 `line.material`(인스턴스)에 색을 직접 지정한다. 색은
/// `CatapultPullAnchor`의 주황 계열과 맞췄다.
///
/// [9차 개편(2026-08-05) — 마우스 휠이 "거리→비율" 계산을 완전히 대체]
/// 사용자가 설계 방향을 확정했다: 연결 중에는 조준자가 앵커에서 멀어지든 가까워지든 당김 비율에
/// 전혀 영향이 없고, 오직 마우스 휠 스크롤 누적값(`wheelRatio`)만으로 0~1 비율이 정해진다 — 거리
/// 기반 계산에 휠 오프셋을 "더하는" 방식이 아니라 완전한 대체다. `ComputeRatio`(거리→비율)는 이제
/// 어디서도 쓰이지 않아 삭제했다(죽은 코드를 남기지 않는다) — `minPullDistance`/`maxPullDistance`
/// 필드도 함께 삭제했다(PRD §6이 확정했던 "당김 거리" 수치 체계는 이 개편으로 폐기됐다 — 자세한
/// 근거는 `docs/PRD/Catapult.md` 상단 9차 개편 요약 참고). **단, `CatapultPullAnchor.connectRange`(C를 눌러 애초에
/// 연결을 "시작"할 수 있는 거리 게이트)는 이번 변경과 무관하게 그대로 남아 있다** — 완전히 다른
/// 메커니즘(연결 성사 여부)이라 혼동하지 않을 것.
/// - **스크롤 방향(`Input.GetAxis("Mouse ScrollWheel")` 양수 = 위로 스크롤)은 그대로 당김 증가로
///   매핑했다** — 위로 스크롤할수록 더 당긴다는 관례적 방향(줌인/증가 계열 UI와 같은 감각)이라
///   부호를 뒤집지 않았다. `wheelSensitivity`(감도)와 `resetRatioOnDisconnect`(연결 해제 시 누적값
///   초기화 여부)는 이 프로젝트의 다른 "씬 튜닝 전 임시값"과 같은 패턴으로 감각적 기본값을 정했다 —
///   씬 튜닝 전 임시값 `[TBD]`(정확한 수치·정책은 실측 필요, 아래 필드 툴팁 참고).
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CatapultLoadController : MonoBehaviour, IInteractionProvider
{
    private enum State { Idle, Connected }

    [Header("연결 대상")]
    [Tooltip("이 투석기의 당김 앵커.")]
    public CatapultPullAnchor anchor;
    [Tooltip("이 투석기의 팔. BeginPull/Fire를 호출한다.")]
    public CatapultArm arm;

    [Header("실 시각화 (신규, 2026-08-04)")]
    [Tooltip("연결 중 보이는 실의 두께.")]
    public float lineWidth = 0.05f;

    [Header("마우스 휠 당김 조절 (9차 개편, 신규 — 거리 기반 계산을 완전히 대체) [TBD, 임시값]")]
    [Tooltip("휠 스크롤 한 눈금(Input.GetAxis(\"Mouse ScrollWheel\")≈±0.1)당 당김 비율이 얼마나 " +
             "바뀌는지. 기본값 1.0은 스크롤 한 눈금당 약 0.1(≈10눈금에 0→1 완전 장전)이라, 이 파일의 " +
             "다른 '10단계' 감각(CatapultArm.pullNotchCount 기본값 10)과 우연이 아니라 의도적으로 " +
             "맞춘 감각적 기본값이다 — 씬 튜닝 전 임시값.")]
    public float wheelSensitivity = 1f;
    [Tooltip("연결을 해제할 때(발사 포함) 누적된 휠 비율을 0으로 되돌릴지. true(기본값)면 다음 연결은 " +
             "항상 비율 0부터 새로 시작한다 — '매번 처음부터 당긴다'는 감각이 직관적이라고 판단한 " +
             "기본값이다. false로 두면 이전 연결에서 남은 비율이 다음 연결에도 이어진다. 씬 튜닝 전 " +
             "임시값(정책 자체가 미확정).")]
    public bool resetRatioOnDisconnect = true;

    [Header("이동 제한 (2026-10-03, 신규) [TBD, 임시값]")]
    [Tooltip("연결 중 정사면체가 앵커에서 수평(XZ)으로 이 거리(Unit)보다 멀리 가지 못한다. 높이는 제한하지 " +
             "않는다. 0 이하면 제한하지 않는다. 앵커의 connectRange보다 작게 두면 connectRange로 올려 쓴다" +
             "(연결하자마자 끌려가지 않게). 씬 튜닝 전 임시값.")]
    public float maxTetherDistance = 12f;

    private ConfigurableJoint leash;
    private State state = State.Idle;
    private PlayerMover connectedMover;
    private Rigidbody connectedBody;
    private LineRenderer line;
    private float wheelRatio; // 9차 개편 — 마우스 휠 누적 당김 비율(0~1). 거리 계산을 완전히 대체한다.

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.enabled = false;
        if (line.sharedMaterial == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (s != null) line.material = new Material(s) { color = new Color(1f, 0.6f, 0.1f, 1f) };
        }
    }

    void Reset()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = 0.05f;
        lr.numCapVertices = 2;
        lr.enabled = false;
    }

    private System.Action onInteract;

    private static readonly System.Collections.Generic.List<CatapultLoadController> all =
        new System.Collections.Generic.List<CatapultLoadController>();

    /// <summary>이 플레이어가 어느 투석기 당김 줄에든 연결돼 있는가. 마우스 휠을 같이 쓰는 다른 기믹(태엽 축)이
    /// 연결 중엔 휠을 양보하게 하는 창구.</summary>
    public static bool IsConnectedTo(PlayerMover m)
    {
        if (m == null) return false;
        for (int i = 0; i < all.Count; i++)
            if (all[i].state == State.Connected && all[i].connectedMover == m) return true;
        return false;
    }

    void OnEnable()
    {
        all.Add(this);
        InteractionController.Register(this);
        RespawnController.ReleaseHoldRequested += HandleReleaseHold;
    }

    void OnDisable()
    {
        all.Remove(this);
        InteractionController.Unregister(this);
        RespawnController.ReleaseHoldRequested -= HandleReleaseHold;
        DestroyLeash();
    }

    // 복귀 대상이 되면 순간이동 전에 연결을 통째로 끊는다(발사 없이). 조인트만 풀고 Connected를 남기면
    // 복귀 뒤에도 줄이 연결된 채로 남아(실선·휠 누적·팔 당김 유지) 멀리 떨어진 체크포인트에서도 장전
    // 상태가 이어졌다(2026-10-10 실측). 탑승자가 이미 장전돼 있으면 팔은 그대로 둔다 — 빈 버킷일
    // 때만 팔을 원위치시킨다.
    private void HandleReleaseHold(PlayerMover mover)
    {
        if (state != State.Connected || connectedMover == null || connectedMover != mover) return;
        Disconnect(fire: false);
        if (arm != null && (arm.bucket == null || !arm.bucket.HasOccupant)) arm.Fire(0f);
    }

    void FixedUpdate() => UpdateLeash();

    private void UpdateLeash()
    {
        bool want = state == State.Connected && maxTetherDistance > 0f
                    && connectedBody != null && connectedMover != null && anchor != null
                    && !connectedMover.ExternallyDriven && !connectedBody.isKinematic;
        if (!want)
        {
            DestroyLeash();
            return;
        }

        float limit = Mathf.Max(maxTetherDistance, anchor.connectRange);
        if (leash == null)
        {
            // 복귀 직후처럼 이미 범위 밖이면 안으로 들어올 때까지 잇지 않는다 — 잇는 순간 끌려간다.
            if (FlatDistance(connectedBody.position, anchor.transform.position) > limit) return;
            CreateLeash(limit);
            return;
        }

        // 앵커는 조향으로 투석기와 함께 움직이고 인스펙터 값도 바뀔 수 있으니 매 스텝 따라간다.
        // 높이는 몸에 맞춘다 — 수평 반경만 막고 수직으로는 구속하지 않으려는 것(위 "이동 제한" 주석).
        leash.connectedAnchor = LeashCenter();
        if (!Mathf.Approximately(leash.linearLimit.limit, limit))
            leash.linearLimit = new SoftJointLimit { limit = limit, bounciness = 0f, contactDistance = 0.02f };
    }

    private static float FlatDistance(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private Vector3 LeashCenter()
    {
        Vector3 a = anchor.transform.position;
        return new Vector3(a.x, connectedBody.position.y, a.z);
    }

    private void CreateLeash(float limit)
    {
        leash = connectedBody.gameObject.AddComponent<ConfigurableJoint>();
        leash.autoConfigureConnectedAnchor = false;
        leash.connectedBody = null;                               // 월드 고정 앵커
        leash.connectedAnchor = LeashCenter();
        leash.anchor = Vector3.zero;                              // 몸 중심에 건다
        // x/y/z를 같은 리밋으로 → 중심 반지름 limit의 구속(회전 대칭). 중심 높이를 몸에 맞춰 따라가므로
        // 실제로는 수평 반경 구속이 된다. 안으로는 느슨한 로프형 하드 리밋.
        leash.xMotion = ConfigurableJointMotion.Limited;
        leash.yMotion = ConfigurableJointMotion.Limited;
        leash.zMotion = ConfigurableJointMotion.Limited;
        leash.angularXMotion = ConfigurableJointMotion.Free;      // 회전은 건드리지 않는다
        leash.angularYMotion = ConfigurableJointMotion.Free;
        leash.angularZMotion = ConfigurableJointMotion.Free;
        leash.linearLimit = new SoftJointLimit { limit = limit, bounciness = 0f, contactDistance = 0.02f };
        leash.enablePreprocessing = false;
    }

    private void DestroyLeash()
    {
        if (leash != null) Destroy(leash);
        leash = null;
    }

    // 키를 직접 읽지 않는다 — 연결 중인 정사면체에게는 해제(발사), 그 외엔 정사면체가 앵커 범위 안일 때
    // 연결을 E 탭 액션으로 올린다. 다른 플레이어가 연결 중이면 회색 사유로 막는다(키맵 통합안 §2-1).
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        PlayerMover mover = InteractionController.Controlled;
        if (mover == null || anchor == null || arm == null) return;
        onInteract ??= HandleInput;

        if (state == State.Connected)
        {
            if (connectedMover == mover)
            {
                into.Add(new InteractionAction
                {
                    channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                    verb = "당김 줄 해제(발사)", enabled = true,
                    priority = InteractionPriority.StateOwner, distance = 0f, execute = onInteract,
                    allowWhenGripped = true,
                });
            }
            else if (Vector3.Distance(mover.transform.position, anchor.transform.position) <= anchor.connectRange)
            {
                into.Add(new InteractionAction
                {
                    channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                    verb = "당김 줄 연결", enabled = false,
                    reason = "다른 플레이어가 당김 줄에 연결되어 있습니다 — Tab으로 그 플레이어를 조작해 해제하세요.",
                    priority = InteractionPriority.Panel, distance = 0f, execute = onInteract,
                });
            }
            return;
        }

        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Tetrahedron) return;
        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float d = Vector3.Distance(body.position, anchor.transform.position);
        if (d > anchor.connectRange) return;
        into.Add(new InteractionAction
        {
            channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
            verb = "당김 줄 연결", enabled = true,
            priority = InteractionPriority.Panel, distance = d, execute = onInteract,
        });
    }

    void Update()
    {
        if (state == State.Connected) UpdatePull();
    }

    private void HandleInput()
    {
        if (state == State.Connected)
        {
            if (connectedMover != null && !connectedMover.IsControlled)
            {
                Debug.Log("[Catapult] 다른 플레이어가 이 투석기의 당김 줄에 연결되어 있습니다 — " +
                          "Tab으로 그 플레이어를 조작해 C로 해제하세요(동시 1인 연결).");
                return;
            }
            Disconnect(fire: true);
            return;
        }

        TryConnect();
    }

    private void UpdatePull()
    {
        if (connectedBody == null || anchor == null)
        {
            // 대상/앵커 소멸 — 발사 없이 안전하게 해제한다.
            Disconnect(fire: false);
            return;
        }

        // 9차 개편 — 조준자와 앵커 사이의 거리는 더 이상 당김 비율에 관여하지 않는다(클래스 상단
        // "9차 개편" 주석 참고). 휠 스크롤 누적값만이 유일한 입력이다.
        // 휠은 전역 입력이라, 연결자가 Tab으로 파킹된 동안 다른 플레이어의 스티커·실타래 휠이 이 팔을 같이
        // 당기던 문제를 막는다(키맵 통합안 §3 휠 소비 규칙) — 지금 연결자를 조작 중일 때만 받는다.
        float scroll = connectedMover != null && connectedMover.IsControlled ? Input.GetAxis("Mouse ScrollWheel") : 0f;
        wheelRatio = Mathf.Clamp01(wheelRatio + scroll * wheelSensitivity);

        if (arm != null) arm.BeginPull(wheelRatio);

        line.SetPosition(0, anchor.transform.position);
        line.SetPosition(1, connectedBody.position);
    }

    private void TryConnect()
    {
        if (anchor == null || arm == null) return;

        PlayerMover mover = FindControlledPlayer();
        if (mover == null)
        {
            Debug.Log("[Catapult] 조작 중인 플레이어를 찾지 못했습니다.");
            return;
        }

        // 장전은 정사면체 전용이다(24차 개편, 역할 게이트 — PRD §5. PlayerWeight 미사용).
        // 22차 개편 전까지는 "정육면체만 거부"(구·정사면체 둘 다 장전 가능)였다 — 그때는 구가 C를
        // 쓸 일이 조향(물리 충돌)엔 전혀 없어 무해한 여유였다. 22차 개편으로 구가 조향석 도킹에 C를
        // 쓰게 되면서, 조향석 dockRange와 이 당김 앵커 connectRange가 겹치는 위치에서 구가 C를
        // 누르면 두 컴포넌트가 같은 프레임에 동시에 반응해(도킹 + 당김줄 연결 둘 다 성사) "링크할
        // 때 실도 같이 매달리는" 간섭이 났다 — 사용자가 실제로 재현해 보고했다. 구 전용 역할
        // (`CatapultSteerHandle`)이 생긴 지금은 장전을 정사면체 하나로 좁혀야 두 C 기능이 겹칠 수
        // 없다(도킹은 구만, 장전은 정사면체만 — 교집합이 없다).
        PlayerShapeIdentity identity = mover.GetComponent<PlayerShapeIdentity>();
        if (identity == null || identity.Kind != PlayerShapeStats.ShapeKind.Tetrahedron)
        {
            Debug.Log("[Catapult] 당김 줄 연결은 정사면체 전용입니다(구는 조향석 도킹, 정육면체는 탑승).");
            return;
        }

        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float myDistance = Vector3.Distance(body.position, anchor.transform.position);
        if (myDistance > anchor.connectRange)
        {
            Debug.Log("[Catapult] 당김 앵커 범위 밖입니다.");
            return;
        }

        // 범위가 겹치는 투석기가 여럿이면 가장 가까운 것에만 연결한다 — 그렇지 않으면 C 한 번에
        // 여러 투석기가 동시에 같은 플레이어를 붙잡는다(각 컨트롤러가 전역 C 키를 독립적으로 읽으므로).
        if (!IsNearestCatapult(body.position, myDistance))
            return;

        connectedMover = mover;
        connectedBody = body;
        state = State.Connected;
        // 9차 개편 — 연결 순간의 초기 비율은 거리 계산이 아니라 현재 wheelRatio(리셋 정책에 따라
        // 0이거나 이전 값)를 그대로 쓴다.
        arm.BeginPull(wheelRatio);
        line.enabled = true;
        Debug.Log("[Catapult] 당김 줄에 연결했습니다. 마우스 휠로 당김 정도를 조절하세요.");
    }

    // 범위 안에 있는 다른 투석기 중 이 투석기보다 가까운 것(또는 정확히 같은 거리라면 InstanceID가
    // 더 작은 것)이 있으면 false를 반환해 연결을 양보한다.
    private bool IsNearestCatapult(Vector3 playerPosition, float myDistance)
    {
        foreach (CatapultLoadController other in FindObjectsOfType<CatapultLoadController>())
        {
            if (other == this || other.anchor == null) continue;

            float otherDistance = Vector3.Distance(playerPosition, other.anchor.transform.position);
            if (otherDistance > other.anchor.connectRange) continue;

            if (otherDistance < myDistance) return false;
            if (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }

    // 그 순간의 당김 비율(wheelRatio)로 발사(fire: true)하거나, 대상/고리 소멸 등으로 발사 없이
    // 놓는다(fire: false).
    private void Disconnect(bool fire)
    {
        float ratio = wheelRatio;

        state = State.Idle;
        connectedMover = null;
        connectedBody = null;
        DestroyLeash();
        line.enabled = false;
        if (resetRatioOnDisconnect) wheelRatio = 0f; // 다음 연결은 새로 시작(정책, 클래스 상단 주석 참고).

        if (fire && arm != null)
            arm.Fire(ratio);
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled) return m;
        return null;
    }
}
