using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 꿈의 실타래 Phase 2 — 세모 핀으로 런타임 앵커를 만들고, 세모를 벽에 붙여 타고 오르게 한다. 씬에 하나만 둔다.
///
/// [무엇을 하나]
/// 조작 중인 플레이어가 세모(Kind==Tetrahedron)일 때 G 입력에 반응해, 세모가 이동/바라보는
/// 방향으로 레이캐스트를 쏴 맞은 벽 표면에 고리(런타임 GameObject + ThreadAnchor + 빛나는 작은
/// 마커)를 생성하고 — 동시에 **세모 자신도 그 자리에 완전 고정 부착**한다(부착 중엔 그 자리에
/// 못 박힌 듯 멈춰 있고 미세 이동도 없다). 점프를 누르면 위로 도약하며 탈착되어 공중에서 자유
/// 이동할 수 있고(고리는 박은 자리에 남음), 더 높은 벽에서 G를 다시 누르면 새 고리 + 재부착한다 —
/// 이 (고정 부착→점프 탈착→공중 이동→G 재부착) 루프로 벽을 타고 오른다. 생성된 고리는 일반
/// ThreadAnchor라 구·세모가 F로 매달릴 수 있다(Phase 1 그대로, 컨트롤러 무변경).
///
/// [고리 개수 — 동시 2개(PRD 확정)]
/// 고리는 박은 자리에 고정, 동시 최대 2개. 3번째를 박으면 가장 오래된 것(리스트 인덱스 0)을 자동
/// 회수한다. 수동 회수는 **전용 키 T**로 한다 — T를 누르면 범위 제한 없이 박은 고리를 **전부** 제거.
/// G(박기)와 분리해, 고리 근처에서 박으려다 회수돼 버리는 충돌을 없앴다. 고정 2라 리스트 하나로 충분.
///
/// [왜 벽 부착을 isKinematic으로 하나 — 로스터 churn 트랩 회피 (핵심 판단)]
/// 세모를 벽에 고정하려면 mover가 매 FixedUpdate에 하는 velocity 하드 대입을 막아야 한다. 그런데
/// DreamThreadController가 Phase 1에서 겪었듯 mover.enabled=false로 끄면 PlayerMover.OnDisable →
/// PlayerControlSwitcher.UnregisterPlayer가 이 세모를 Tab 로스터에서 빼, 조작권·카메라가 딴 데로
/// 튀고 stale IsControlled로 동시입력이 생긴다(그 트랩 때문에 컨트롤러도 결국 mover를 끄는 대신
/// PlayerMover.ExternallyDriven 플래그로 바꿨다 — 여기 선택이 먼저 옳았던 셈이다).
/// 여기서는 그 복잡성을 아예 피한다: **Rigidbody.isKinematic=true**로 부착한다. 키네마틱 바디는
/// 중력·힘·mover의 velocity 대입을 전부 무시하므로(PhysX가 키네마틱 velocity를 적분하지 않음)
/// mover.enabled를 끌 필요가 없다 → 로스터가 그대로라 조작권/카메라가 튀지 않는다. 부착 중엔 그
/// 자리에 완전 고정(입력 이동 없음)이고, 탈착은 isKinematic=false + (점프면 위로 velocity 부여)로 한다.
///
/// [점프 충돌 처리] 부착 중 PlayerJump.enabled=false로 꺼(런타임 토글, 파일 수정 아님) 클라이밍
/// 도약("Jump" 입력)과 일반 점프의 이중 발화를 원천 차단한다(부착 중엔 비접지라 PlayerJump가 어차피
/// 안 뛰지만, 명시적으로 꺼 안전하게 만든다). 탈착 시 다시 켠다.
///
/// [입력 — 2026-10-03, F·G·T → E 탭/홀드 (키맵 통합안 §2-1)]
/// 키를 직접 읽지 않고 <see cref="InteractionController"/>에 액션을 올린다: 핀 박기+벽 부착 = E 탭, 핀 전부 회수 =
/// E 홀드, 벽에서 떼기 = E 탭(부착 중, 붙잡힌 상태에서도 허용). 점프 도약 탈착은 Space 그대로다.
/// - 핀 박기는 벽에 거의 닿았을 때(placeRange 안)에만 올라오고, 그때는 DreamThreadController의 "실 연결"보다
///   **한 단계 높다**(Panel + 10). 처음엔 같은 순위에서 거리로 가렸는데(벽 타기 루프가 최하위로는 깨지기 때문),
///   핀이 벽에서 0.2U 띄워 박혀 벽 앞 0.4U의 몸에서는 방금 박은 핀이 벽보다 가까워 **연타하면 자기 핀에 매달려
///   튕겨 나갔다**(2026-10-03 Loki 실측: 연타 직후 ExternallyDriven + 옆 속도). 핀에 매달리려면 벽에서 placeRange
///   밖으로 물러난다.
/// - 벽 부착 중 탭은 **떼기만** 한다. 예전엔 F 한 번이 "떼기 + 근처 고리에 매달기"를 연쇄했지만 입력 하나는
///   동작 하나다 — 매달리려면 한 번 더 누른다. 키네마틱 바디에 컨트롤러가 조인트를 붙이면 스윙이 깨지므로
///   매달리기 전에 반드시 dynamic으로 되돌려져야 하는 점은 그대로다(부착 중 연결 액션은 붙잡힘 필터가 막는다).
/// - 로프 매달림/발사 중(컨트롤러가 ExternallyDriven을 세움)엔 핀 박기를 올리지 않는다 — 그 세모에 키네마틱을
///   걸면 조인트/스윙과 충돌한다.
///
/// [하드룰] 핀 생성/회수·벽부착·클라이밍 전부 이 폴더 안에서 끝난다. PlayerSystem은 IsControlled/
/// Kind/Rigidbody를 읽고, mover의 enabled는 건드리지 않으며(부착은 키네마틱이라), PlayerJump.enabled만
/// 런타임 토글한다 — 파일 수정 없음.
/// </summary>
[DefaultExecutionOrder(-50)]
public class ThreadPinPlacer : MonoBehaviour, IInteractionProvider
{
    [Header("레이캐스트 (벽 탐지)")]
    [Tooltip("세모 중심에서 이동 방향으로 쏘는 레이의 최대 사거리(Unit). 이 안에 벽이 없으면 핀 생성 실패.")]
    public float rayRange = 6f;
    [Tooltip("핀 박기를 올리는(그리고 실제로 박는) 최대 거리(Unit) — 세모 중심에서 벽 표면까지. 벽에 거의 닿았을 때만 " +
             "활성화되게 좁게 둔다(2026-10-03, 6U 사거리에선 공중에서도 박혀 연속으로 누르면 벽에 끼는 문제가 있었다). " +
             "rayRange와 이 값 중 작은 쪽이 실제 사거리다. 세모 반크기가 있으므로 몸이 벽에 붙었을 때 중심 거리가 " +
             "이미 그만큼이라 너무 작게 두면 영영 안 박힌다.")]
    public float placeRange = 1.2f;
    [Tooltip("핀을 박을 수 있는 '벽' 레이어. 기본 전체(~0). 플레이어(자신·타 도형) 콜라이더는 레이어가 " +
             "아니라 계층(PlayerMover 보유)으로 판별해 제외하므로, 벽이 플레이어와 같은 레이어(흔한 Default)여도 박힌다.")]
    public LayerMask wallLayer = ~0;
    [Tooltip("핀을 벽 표면에서 법선 방향으로 이 거리만큼 띄운다(Unit). 0이면 표면에 딱 붙고, 살짝 " +
             "띄우면 실이 벽에 파묻히지 않아 걸기 쉽다.")]
    public float pinSurfaceOffset = 0.2f;
    [Tooltip("velocity를 '이동 중'으로 인정하는 최소 수평 속력(Unit/s). 이보다 느리면 마지막 이동 방향을 쓴다.")]
    public float minMoveSpeed = 0.5f;

    [Header("핀 (런타임 앵커)")]
    [Tooltip("생성된 핀 앵커의 연결 범위(Unit). 플레이어가 이 거리 안에서 F로 매달릴 수 있다(ThreadAnchor.connectRange).")]
    public float pinConnectRange = 4f;
    [Tooltip("핀 시각 마커 지름(Unit).")]
    public float pinMarkerSize = 0.3f;

    [Header("벽 부착 / 클라이밍 (세모 전용)")]
    [Tooltip("점프로 탈착할 때 위로 주는 도약 속도(Unit/s). 부착은 그 자리에 '완전 고정'이고(미세 이동 없음), " +
             "상승은 이 도약 → 공중 이동 → G 재부착 루프로만 한다. 도달 높이 ≈ v^2/(2g)(대략 9면 ~4 Unit).")]
    public float climbLeapSpeed = 9f;

    private const int MaxPins = 2;

    // 동시 최대 2개. 삽입 순서 = 리스트 순서라 인덱스 0이 가장 오래된 핀. 고정 2라 풀링/큐 불필요(YAGNI).
    private readonly List<GameObject> pins = new List<GameObject>();
    // 핀 마커 공용 머티리얼. 핀마다 새로 만들면 누수되므로 한 번 만들어 재사용한다.
    private Material pinMaterial;
    // 매 프레임 갱신하는 마지막 유의미한 수평 이동 방향(정지 시 레이 방향 폴백).
    private Vector3 lastMoveDir = Vector3.forward;
    // 마지막으로 부착했던 벽의 법선. 공중 재부착(클라이밍) 시 수직 도약 직후처럼 수평 이동이 미약하거나
    // 벽면과 평행이 되어 이동 방향 레이가 벽을 못 맞히는 경우, 이 벽(-법선 방향)으로 한 번 더 쏘는 폴백에 쓴다.
    private Vector3 lastWallNormal = Vector3.zero;

    // 벽 부착 상태(동시 1명 — 조작 중 세모 하나만). 아래 참조들은 부착 동안만 유효하다.
    private bool wallAttached;
    private PlayerMover attachedMover;
    private Rigidbody attachedBody;
    private PlayerJump attachedJump;

    // 벽 탐지 캐시 — 액션이 "지금 벽 앞이라 박을 수 있나"를 알아야 하는데 레이캐스트를 매 프레임 쏘면 낭비라
    // probeInterval마다만 갱신한다. 실제 박을 때(ExecutePlace)는 그 순간 새로 쏜다.
    private const float ProbeInterval = 0.1f;
    private float nextProbeTime;
    private bool probeWallValid;
    private float probeWallDistance;

    private System.Action onPlace, onDetach, onRetrieve;

    void OnEnable() => InteractionController.Register(this);

    void Update()
    {
        PlayerMover controlled = InteractionController.Controlled;
        CacheMoveDir(controlled);

        // 부착 중이면 부착 세모의 입력(점프 도약탈착)만 처리한다. 파킹(부착 세모가 조작 대상이 아님) 시엔
        // 아무것도 안 한다 — UpdateWhileAttached의 IsControlled 가드.
        if (wallAttached)
        {
            probeWallValid = false;
            UpdateWhileAttached();
            return;
        }

        RefreshWallProbe(controlled);
    }

    private static bool IsTetrahedron(PlayerMover mover)
    {
        PlayerShapeIdentity identity = mover != null ? mover.GetComponent<PlayerShapeIdentity>() : null;
        return identity != null && identity.Kind == PlayerShapeStats.ShapeKind.Tetrahedron;
    }

    private void RefreshWallProbe(PlayerMover controlled)
    {
        if (Time.time < nextProbeTime) return;
        nextProbeTime = Time.time + ProbeInterval;

        probeWallValid = false;
        if (controlled == null || controlled.ExternallyDriven || !IsTetrahedron(controlled)) return;
        Rigidbody body = controlled.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return;

        if (TryRaycastWall(body, out RaycastHit wall))
        {
            probeWallValid = true;
            probeWallDistance = wall.distance;
        }
    }

    private bool AnyPin()
    {
        foreach (GameObject p in pins)
            if (p != null) return true;
        return false;
    }

    // 벽 부착 중에는 떼기(탭)와 핀 전체 회수(홀드)를, 아니면 세모가 벽 앞일 때 핀 박기(탭)를 올린다. 핀이 있으면
    // 회수(홀드)가 가능해지므로 이 세모의 E 탭은 "뗄 때" 실행으로 밀린다(통합안 §2-1 탭/홀드 규칙).
    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        PlayerMover controlled = InteractionController.Controlled;
        if (controlled == null) return;

        onPlace ??= ExecutePlace;
        onDetach ??= () => DetachFromWall(withLeap: false);
        onRetrieve ??= ExecuteRetrieve;

        if (wallAttached)
        {
            if (attachedMover != controlled) return;
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                verb = "벽에서 떼기", enabled = true,
                priority = InteractionPriority.StateOwner, distance = 0f, execute = onDetach,
                allowWhenGripped = true,
            });
            if (AnyPin())
            {
                into.Add(new InteractionAction
                {
                    channel = InteractionChannel.Hand, trigger = InteractionTrigger.Hold,
                    verb = "핀 전부 회수", enabled = true,
                    priority = InteractionPriority.Block, distance = 0f, execute = onRetrieve,
                    allowWhenGripped = true,
                });
            }
            return;
        }

        if (!IsTetrahedron(controlled)) return;

        if (probeWallValid)
        {
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                verb = "핀 박고 벽에 붙기", enabled = true,
                priority = InteractionPriority.Panel + 10, distance = probeWallDistance, execute = onPlace,
            });
        }
        if (AnyPin())
        {
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Hold,
                verb = "핀 전부 회수", enabled = true,
                priority = InteractionPriority.Block, distance = 0f, execute = onRetrieve,
            });
        }
    }

    private void ExecutePlace()
    {
        PlayerMover controlled = InteractionController.Controlled;
        if (controlled == null || controlled.ExternallyDriven || !IsTetrahedron(controlled)) return;
        Rigidbody body = controlled.GetComponent<Rigidbody>();
        if (body == null) return;
        TryPlacePinAndAttach(controlled, body);
    }

    // 범위 제한 없이 박은 핀을 **전부** 제거한다. 벽 부착은 풀지 않는다. 자동 회수(3번째 박을 때 가장 오래된
    // 것)는 별개로 유지.
    private void ExecuteRetrieve()
    {
        int removed = RetrieveAllPins();
        Debug.Log(removed > 0
            ? $"[DreamThread] 핀 {removed}개를 모두 회수했습니다."
            : "[DreamThread] 회수할 핀이 없습니다.");
    }

    // 매단 채 컨트롤러가 꺼지듯, 부착 채로 이 컴포넌트가 꺼지면 플레이어를 키네마틱·점프불가로
    // 남기지 않도록 원복한다.
    void OnDisable()
    {
        InteractionController.Unregister(this);
        if (wallAttached) DetachFromWall(withLeap: false);
    }

    // 부착 세모가 조작 대상이면 그 세모의 부착 전용 입력을 처리한다. 조작 대상이 아니면(Tab으로
    // 파킹) 입력을 읽지 않아, 새로 조작하는 플레이어와 점프/F 탈착이 동시 반응하지 않게 한다.
    private void UpdateWhileAttached()
    {
        if (attachedMover == null || attachedBody == null)
        {
            DetachFromWall(withLeap: false);
            return;
        }
        if (!attachedMover.IsControlled) return;

        // 떼기(E 탭)는 중앙 입력이 처리한다(CollectActions). 여기는 점프 도약 탈착만 읽는다.
        // 점프: 위로 도약하며 탈착. 고리는 박은 자리에 남는다.
        if (Input.GetButtonDown("Jump"))
        {
            DetachFromWall(withLeap: true);
        }
    }

    // 조작 중 세모의 유의미한 수평 이동 방향을 캐시. G 순간 velocity가 미약할 때의 폴백 방향이다.
    private void CacheMoveDir(PlayerMover controlled)
    {
        if (controlled == null) return;
        Rigidbody body = controlled.GetComponent<Rigidbody>();
        if (body == null) return;
        Vector3 v = body.velocity;
        v.y = 0f;
        if (v.sqrMagnitude >= minMoveSpeed * minMoveSpeed)
            lastMoveDir = v.normalized;
    }

    /// <summary>지금 박혀 있는 핀들을 삽입 순서대로 into에 담는다(가장 오래된 것이 앞). 줄다리가
    /// 어느 핀과 이을지 스스로 고르도록 목록만 넘긴다 — "2개짜리 짝"이나 "가장 가까운 하나" 같은
    /// 선택 규칙을 여기 두면 줄다리가 규칙을 바꿀 때마다 이 파일을 같이 고쳐야 한다.
    /// 호출자가 재사용하는 버퍼를 넘기므로 매 프레임 불러도 할당이 없다.</summary>
    public void CollectPins(List<ThreadAnchor> into)
    {
        into.Clear();
        pins.RemoveAll(p => p == null);
        foreach (GameObject p in pins)
        {
            ThreadAnchor a = p.GetComponent<ThreadAnchor>();
            if (a != null) into.Add(a);
        }
    }

    // T = 박은 핀을 전부 회수(범위 제한 없음). 제거한 개수를 반환한다.
    private int RetrieveAllPins()
    {
        pins.RemoveAll(p => p == null);
        int n = pins.Count;
        foreach (GameObject p in pins)
            if (p != null) Destroy(p);
        pins.Clear();
        return n;
    }

    private void TryPlacePinAndAttach(PlayerMover mover, Rigidbody body)
    {
        if (!TryRaycastWall(body, out RaycastHit wall))
        {
            Debug.Log("[DreamThread] 사거리 안에 핀을 박을 벽이 없습니다.");
            return;
        }

        // 3번째면 가장 오래된 것(인덱스 0) 자동 회수. 매달림 중인 고리를 회수해도 컨트롤러가
        // anchor==null을 감지해 안전하게 놓는다(스윙 쪽 무변경).
        pins.RemoveAll(p => p == null);
        if (pins.Count >= MaxPins)
        {
            Destroy(pins[0]);
            pins.RemoveAt(0);
        }
        pins.Add(CreatePin(wall.point + wall.normal * pinSurfaceOffset, wall.collider.transform));
        lastWallNormal = wall.normal; // 재부착 폴백 방향 갱신(같은 벽을 climb할 때 씀)
        Debug.Log("[DreamThread] 벽에 핀을 박았습니다(최대 2개). 세모/구가 F로 매달릴 수 있습니다.");

        // G = 고리 생성 + 세모 자신도 그 자리에 벽 부착(완전 고정).
        AttachToWall(mover, body);
    }

    // 벽을 찾는다. 1순위: 이동/바라보는 방향. 2순위: 마지막으로 부착했던 벽 방향(-lastWallNormal) —
    // 수직 도약 직후처럼 수평 이동이 미약하거나 벽면과 평행이 되어 1순위가 벽을 못 맞혀도 같은 벽에
    // 재부착되게 하는 폴백(클라이밍 루프의 핵심).
    private bool TryRaycastWall(Rigidbody body, out RaycastHit wall)
    {
        if (RaycastWallInDir(body.position, ResolveRayDirection(body), out wall)) return true;
        if (lastWallNormal != Vector3.zero && RaycastWallInDir(body.position, -lastWallNormal, out wall)) return true;
        return false;
    }

    // 한 방향으로 레이를 쏴 가장 가까운 '벽'(플레이어 제외)을 찾는다. 레이어로 자기 제외를 하면
    // 플레이어와 벽이 같은 레이어(흔한 Default)일 때 벽까지 걸러지므로, 계층(PlayerMover 보유)으로 제외한다.
    private bool RaycastWallInDir(Vector3 origin, Vector3 dir, out RaycastHit wall)
    {
        wall = default;
        if (dir.sqrMagnitude < 1e-4f) return false;
        RaycastHit[] hits = Physics.RaycastAll(origin, dir.normalized, Mathf.Min(rayRange, placeRange), wallLayer, QueryTriggerInteraction.Ignore);
        float bestDist = float.PositiveInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<PlayerMover>() != null) continue; // 자기/다른 플레이어 제외
            if (h.distance < bestDist) { bestDist = h.distance; wall = h; }
        }
        return !float.IsPositiveInfinity(bestDist);
    }

    // 세모를 벽에 붙인다. isKinematic으로 중력·힘·mover velocity를 전부 무시시켜 고정하되, mover.enabled는
    // 끄지 않아 로스터/조작권이 그대로 유지된다(로스터 churn 회피 — 클래스 주석 참고). PlayerJump는
    // 꺼 클라이밍 도약과 일반 점프의 충돌을 원천 차단한다.
    private void AttachToWall(PlayerMover mover, Rigidbody body)
    {
        attachedMover = mover;
        attachedBody = body;
        attachedJump = mover.GetComponent<PlayerJump>();

        body.isKinematic = true; // 중력·힘·mover velocity 전부 무시 → 그 자리에 완전 고정(미세 이동 없음).
        if (attachedJump != null) attachedJump.enabled = false;
        wallAttached = true;

        Debug.Log("[DreamThread] 세모가 벽에 붙어 고정됐습니다. 점프로 위로 도약하며 탈착합니다.");
    }

    // 벽에서 뗀다. withLeap이면 위로 도약 속도를 줘 공중으로 튀어오른다(클라이밍 점프). 아니면
    // 그 자리에서 dynamic으로 되돌린다(F로 매달릴 때 등). PlayerJump를 다시 켠다.
    private void DetachFromWall(bool withLeap)
    {
        if (attachedBody != null)
        {
            attachedBody.isKinematic = false;
            if (withLeap)
                attachedBody.velocity = Vector3.up * climbLeapSpeed; // 위로 도약(키네마틱 해제 후 dynamic velocity)
        }
        if (attachedJump != null) attachedJump.enabled = true;

        wallAttached = false;
        attachedMover = null;
        attachedBody = null;
        attachedJump = null;
    }

    private Vector3 ResolveRayDirection(Rigidbody body)
    {
        Vector3 v = body.velocity;
        v.y = 0f;
        if (v.sqrMagnitude >= minMoveSpeed * minMoveSpeed)
            return v.normalized;
        if (lastMoveDir.sqrMagnitude > 1e-4f)
            return lastMoveDir;
        Vector3 fwd = body.transform.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
    }

    // wallTransform 아래 자식으로 붙인다(월드 위치는 position으로 그대로 지정) — 맞은 표면이 정적이면
    // 동작 변화가 없고, StepRotatingBridge처럼 스스로 움직이는 표면이면 핀이 자동으로 따라간다. 2026-09-21
    // 실측: 다리가 회전한 자세에서 핀을 박으면 부모 없이는 그 순간 월드 좌표에 박제돼, 다리가 원래
    // 자세로 돌아온 뒤 핀만 공중에 남았다(DreamThreadController는 이미 매 프레임 anchor.transform의
    // 실시간 위치를 읽으므로 — "동적 앵커" 절 참고 — 부모만 붙이면 별도 로직 없이 해결된다).
    private GameObject CreatePin(Vector3 position, Transform wallTransform)
    {
        GameObject pin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pin.name = "DreamThread_Pin";
        Destroy(pin.GetComponent<Collider>()); // 앵커는 순수 마커 — 물리 접촉 없음(ThreadAnchor 주석 참고).
        if (wallTransform != null) pin.transform.SetParent(wallTransform, worldPositionStays: false);
        pin.transform.position = position;

        // 부모의 비균일 스케일이 마커 모양을 찌그러뜨리지 않도록 로컬 스케일을 부모 lossyScale로
        // 나눠 보정한다(CatapultBucket/RailCart가 겪은 비균일 스케일 부모화 전단 왜곡과 같은 함정 —
        // 벽이 늘려 만든 Cube라면 흔하다). 회전으로 인한 전단까지 완전히 상쇄하진 않지만, 순수 시각
        // 마커라 그 정도 근사로 충분하다.
        Vector3 parentScale = wallTransform != null ? wallTransform.lossyScale : Vector3.one;
        pin.transform.localScale = new Vector3(
            pinMarkerSize / Mathf.Max(parentScale.x, 1e-4f),
            pinMarkerSize / Mathf.Max(parentScale.y, 1e-4f),
            pinMarkerSize / Mathf.Max(parentScale.z, 1e-4f));

        Renderer r = pin.GetComponent<Renderer>();
        r.sharedMaterial = GetPinMaterial();

        ThreadAnchor a = pin.AddComponent<ThreadAnchor>();
        a.connectRange = pinConnectRange;
        return pin;
    }

    // 에디터 앵커(청록)와 구분되게 따뜻한 색으로. 런타임이라 에디터 API를 쓰지 않고, 언릿 계열
    // 셰이더로 파이프라인 무관하게 "빛나는" 느낌을 낸다(DreamThreadController.Awake의 폴백과 동일 발상).
    private Material GetPinMaterial()
    {
        if (pinMaterial != null) return pinMaterial;
        Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        pinMaterial = new Material(s) { color = new Color(1f, 0.55f, 0.75f, 1f) };
        return pinMaterial;
    }

}
