using UnityEngine;

/// <summary>
/// 정육면체만 딱딱 블록(<see cref="SnapBlock"/>) 1개를 머리 위에 얹어 들고 이동·점프해,
/// 턱이나 선반 위에 올려놓는 주체. <see cref="BlockCarryController"/>가 런타임에 플레이어 Root에
/// AddComponent한다 — PlayerSystem 파일도, 씬의 플레이어 3종도, SnapBlockSystem도 건드리지
/// 않는다(교차 폴더 하드룰). PlayerStickerCarrier / PlayerRollModeReceiver 와 같은 "기믹이
/// 붙여주는 컴포넌트" 패턴이다.
///
/// [입력] 키를 직접 읽지 않는다. 들기/내려놓기는 E 탭 액션으로 <see cref="InteractionController"/>에
/// 올린다(키맵 통합안 §7-1). 이 플레이어가 조작 대상일 때만(PlayerMover.IsControlled &amp;&amp;
/// !ExternallyDriven) 액션을 올린다. Tab으로 다른 도형을 조작 중이면 반응하지 않는다.
///
/// [타겟팅 — 근접] 카메라 조준이 아니라 "플레이어에서 pickupRange 안, 가장 가까운 자유 상태
/// SnapBlock"을 대상으로 삼는다(마찰 스티커·딱딱 블록과 동일). 대상 블록 위에 작은 공(조준점)이
/// 떠서 색으로 상태를 알린다: 하양=들 수 있음 / 빨강=거부(도형·질량·결합) / 하양(든 상태)=내려놓기 가능
/// / 빨강(든 상태)=내려놓을 공간 없음.
///
/// [왜 부모화도 조인트도 아닌 "위치 추종 + 자세 고정"인가]
///  · 부모화하면 구가 구를 때 자식 블록도 함께 돌아 "블록은 회전하지 않는다" 요구를 깬다.
///  · FixedJoint로 매달면 블록 질량이 조인트를 통해 점프·이동에 sag로 새어들어 "점프 높이가
///    낮아지면 안 된다"(±0.05U)를 위협한다.
///  → 든 블록의 Rigidbody를 isKinematic으로 돌리고(플레이어 질량에 0 기여), LateUpdate마다
///    머리 위 지점으로 위치만 옮기고 회전은 픽업 순간 값에 고정한다. PlayerJump.LaunchToHeight가
///    질량 무관 velocity 대입이라, 도달 높이는 빈손과 동일하게 유지된다.
///
/// [자동 내려놓기] 리스폰·리셋으로 몸이 순간이동하거나(위치 점프 감지), 실타래 매달림·굴리기
/// 모드로 몸을 외부가 소유하거나(ExternallyDriven / Rigidbody.isKinematic), 크기 변형이
/// 진행 중이면(localScale 변화 감지) 현재 위치에 즉시 드롭한다 — 들고 있는 채로 상태가 꼬여
/// 소프트락이 되는 것을 막는다.
///
/// [규약] 전역 상태(Physics.*) 미변경(MP-01). PlayerSystem·씬 무수정.
/// </summary>
[RequireComponent(typeof(PlayerMover))]
[DisallowMultipleComponent]
public class PlayerBlockCarrier : MonoBehaviour, IInteractionProvider
{
    [Header("타겟팅 (근접)")]
    [Tooltip("이 거리(Unit) 안에서 가장 가까운 SnapBlock을 대상으로 삼는다. 플레이어 위치에서 " +
             "블록 콜라이더의 가장 가까운 점까지의 거리로 잰다.")]
    public float pickupRange = 3.5f;

    [Header("운반")]
    [Tooltip("든 블록을 플레이어 중심에서 이 높이(Unit) 위에 고정한다.")]
    public float carryHeight = 1.1f;
    [Tooltip("내려놓기 지점을 바라보는 방향으로 이 거리(Unit)만큼 앞에 잡는다.")]
    public float dropDistance = 1.2f;
    [Tooltip("들 수 있는 블록 Rigidbody 질량 상한. 초과하면 거부한다.")]
    public float maxCarryMass = 2f;

    [Header("자동 내려놓기 감지")]
    [Tooltip("한 프레임에 플레이어가 이 거리(Unit) 넘게 순간이동하면 리스폰/리셋으로 보고 즉시 드롭한다.")]
    public float teleportDropThreshold = 3f;

    [Header("성능")]
    [Tooltip("대상 블록을 다시 찾는 간격(초). FindObjectsOfType로 씬을 매 프레임 훑으면 매 프레임 " +
             "GC 할당이 생겨 구처럼 실제 물리로 구르는 도형에서 눈에 띄는 꿀렁임(프레임 히치)을 " +
             "유발한다 — 플레이테스트에서 실측된 원인. 0.15초 간격이면 '블록 근처로 다가가면 " +
             "조준점이 뜬다' 체감에는 차이가 없다.")]
    public float retargetInterval = 0.15f;

    private PlayerMover mover;
    private PlayerShapeIdentity shapeId;

    // 든 블록 상태.
    private SnapBlock carried;
    private Rigidbody carriedBody;
    private Quaternion carriedRotation;          // 픽업 순간에 고정한 자세.
    private bool carriedKinematicWas;            // 원복용 캐시.
    private RigidbodyInterpolation carriedInterpWas;
    private readonly System.Collections.Generic.List<Collider> ignoredWithPlayer =
        new System.Collections.Generic.List<Collider>();

    // 자동 드롭 감지용 이전 프레임 스냅샷.
    private Vector3 lastPos;
    private Vector3 lastScale;

    // 마지막 스캔 시점의 대상(없으면 null) + 거부 사유. retargetInterval 간격으로만 갱신된다.
    private SnapBlock aimed;
    private string rejectReason;
    private float aimedDistance;
    private float nextRetargetTime;

    // 컨트롤러에 액션을 올려도 되는 상태인가(Update의 게이트를 통과한 프레임에만 true) + 내려놓기 가능 여부.
    private bool interactable;
    private bool dropClear;
    private System.Action onInteractTap;

    private Transform reticle;
    private Renderer reticleRenderer;

    private static readonly System.Collections.Generic.List<SnapBlock> blockBuf =
        new System.Collections.Generic.List<SnapBlock>();

    private readonly Collider[] dropOverlapBuffer = new Collider[16];

    private static readonly Color colorOk = new Color(1f, 1f, 1f, 0.95f);
    private static readonly Color colorReject = new Color(0.9f, 0.2f, 0.2f, 0.95f);

    private void Awake()
    {
        mover = GetComponent<PlayerMover>();
        shapeId = GetComponentInChildren<PlayerShapeIdentity>();
        lastPos = transform.position;
        lastScale = transform.localScale;
        onInteractTap = OnInteractTap;
    }

    private void OnEnable()
    {
        InteractionController.Register(this);
    }

    private void OnDisable()
    {
        InteractionController.Unregister(this);
        interactable = false;
        if (carried != null) DropInPlace("컴포넌트 비활성화");
        HideReticle();
    }

    private void OnDestroy()
    {
        if (carried != null) DropInPlace("컴포넌트 파괴");
        if (reticleRenderer != null) Destroy(reticleRenderer.material);
        if (reticle != null) Destroy(reticle.gameObject);
    }

    private void Update()
    {
        if (pendingBlockCol != null) UpdatePendingIgnore();

        // 조작권이 없어도 자동 드롭 조건은 계속 감시한다(다른 도형 조작 중 이 몸이 리스폰될 수 있다).
        // 순간이동·크기 변화는 "직전 프레임 대비"라, 감시 후 이번 프레임 스냅샷을 항상 갱신한다
        // (안 그러면 픽업 직후 첫 프레임에 오래된 lastPos와 비교해 오탐 드롭이 난다).
        if (carried != null && ShouldAutoDrop(out string why))
        {
            DropInPlace(why);
            RecordFrameSnapshot();
            return;
        }
        RecordFrameSnapshot();

        interactable = false;
        if (mover == null || !mover.IsControlled || mover.ExternallyDriven)
        {
            aimed = null;
            HideReticle();
            return;
        }

        // 정육면체만 블록을 옮긴다(2026-10-03 확정: 구·정사면체는 도형 특성상 블록을 옮기는 게 어색하다.
        // 결합은 도형 제한 없음). 정육면체가 아니면 EvaluateReject가 항상 거부하므로, 스캔·조준점도
        // 아예 안 띄운다(PR #92 리뷰 지적, 경미).
        if (shapeId == null || shapeId.Kind != PlayerShapeStats.ShapeKind.Cube)
        {
            aimed = null;
            HideReticle();
            return;
        }

        // FindObjectsOfType로 씬을 훑는 건 매 프레임이 아니라 retargetInterval마다만 — 안 그러면
        // 매 프레임 GC 할당이 생겨 물리로 구르는 도형(특히 구)에서 눈에 띄는 히치가 난다.
        if (carried == null && Time.time >= nextRetargetTime)
        {
            UpdateTarget();
            nextRetargetTime = Time.time + Mathf.Max(0.02f, retargetInterval);
        }

        UpdateReticle();
        interactable = true;
    }

    // ── 중앙 입력 연동 ────────────────────────────────────────

    /// <summary>들고 있는 블록. 없으면 null. 결합(SnapBlockController)이 든 블록을 대상으로 쓰는 데 필요하다.</summary>
    public SnapBlock Carried => carried;

    /// <summary>든 블록을 지금 위치에 그대로 놓는다(공간 검사 없음). 같은 프레임에 결합하기 직전에 부른다.</summary>
    public void ReleaseInPlace()
    {
        // 결합 자세가 이 몸과 겹칠 수 있다 — 충돌 무시를 바로 풀지 않고 몸이 빠져나올 때까지 유지한다.
        keepIgnoreUntilClear = true;
        DropInPlace("결합을 위해 들기 해제");
        keepIgnoreUntilClear = false;
    }

    private void OnInteractTap()
    {
        if (carried != null) TryDropForward();
        else TryPickup();
    }

    public void CollectActions(System.Collections.Generic.List<InteractionAction> into)
    {
        if (!interactable) return;

        if (carried != null)
        {
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                verb = "내려놓기", enabled = dropClear, reason = "내려놓을 공간이 없습니다 — 벽·천장과 겹칩니다.",
                priority = InteractionPriority.Block, distance = 0f, execute = onInteractTap,
            });
        }
        else if (aimed != null)
        {
            into.Add(new InteractionAction
            {
                channel = InteractionChannel.Hand, trigger = InteractionTrigger.Tap,
                verb = "들기", enabled = rejectReason == null, reason = rejectReason,
                priority = InteractionPriority.Block, distance = aimedDistance, execute = onInteractTap,
            });
        }
    }

    private void RecordFrameSnapshot()
    {
        lastPos = transform.position;
        lastScale = transform.localScale;
    }

    private void LateUpdate()
    {
        if (carried == null) return;

        // 든 블록이 사라졌으면(파괴 등) 상태만 정리.
        if (carriedBody == null)
        {
            ClearCarryState();
            return;
        }

        carriedBody.transform.SetPositionAndRotation(
            transform.position + Vector3.up * EffectiveCarryHeight(), carriedRotation);

    }

    // ── 타겟팅 ────────────────────────────────────────────────

    private void UpdateTarget()
    {
        aimed = null;
        rejectReason = null;

        float bestSqr = pickupRange * pickupRange;
        Vector3 me = transform.position;
        SnapBlock best = null;

        blockBuf.Clear();
        blockBuf.AddRange(Object.FindObjectsOfType<SnapBlock>());
        foreach (SnapBlock b in blockBuf)
        {
            if (b == null) continue;
            Collider col = b.GetComponent<Collider>();
            if (col == null) continue;

            float sqr = (col.ClosestPoint(me) - me).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bestSqr = sqr;
            best = b;
        }

        if (best == null) return;

        aimed = best;
        aimedDistance = Mathf.Sqrt(bestSqr);
        rejectReason = EvaluateReject(best);
    }

    // 들 수 없으면 사유 문자열, 들 수 있으면 null.
    private string EvaluateReject(SnapBlock b)
    {
        PlayerShapeStats.ShapeKind kind = shapeId != null ? shapeId.Kind : PlayerShapeStats.ShapeKind.Sphere;
        if (kind != PlayerShapeStats.ShapeKind.Cube)
            return "정육면체만 블록을 들 수 있습니다.";

        if (b.HasConnections)
            return "결합된 구조물은 들 수 없습니다.";

        Rigidbody rb = b.Body != null ? b.Body : b.GetComponent<Rigidbody>();
        if (rb != null && rb.mass > maxCarryMass)
            return $"블록이 너무 무겁습니다 (질량 {rb.mass:0.##} > 상한 {maxCarryMass:0.##}).";

        return null;
    }

    // ── 픽업 / 드롭 ───────────────────────────────────────────

    private void TryPickup()
    {
        if (aimed == null)
        {
            Debug.Log($"[BlockCarry] {pickupRange}칸 안에 SnapBlock이 없습니다 — 블록 가까이 서세요.");
            return;
        }
        if (rejectReason != null)
        {
            Debug.Log($"[BlockCarry] {rejectReason}");
            return;
        }

        Rigidbody rb = aimed.Body != null ? aimed.Body : aimed.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.Log("[BlockCarry] 블록에 Rigidbody가 없어 들 수 없습니다.");
            return;
        }

        carried = aimed;
        carriedBody = rb;
        // 움직이는(굴러가는) 블록을 집어도 비스듬한 채 얹히지 않게, 가장 가까운 축 정렬 자세로 고정한다.
        carriedRotation = SnapToAxes(rb.rotation);

        // SnapBlock 컴포넌트를 비활성화 — SnapBlockController(용접 대상 탐색)와 PlayerCubeDock
        // (도킹 대상 탐색)는 전부 FindObjectsOfType<SnapBlock>()로 후보를 찾는데, 비활성 컴포넌트는
        // 기본 오버로드에서 제외된다. 든 블록을 숨기지 않으면: 든 채로 다른 플레이어가 용접하면
        // 다음 프레임 이 컴포넌트의 강제 위치 갱신이 그 결합을 덮어쓰고, 내려놓는 순간 물리가 밀린
        // 조인트 오차를 한 번에 해소하며 블록이 먼 파트너 쪽으로 튕겨나간다(PR #92 리뷰에서 지적).
        // 다른 플레이어가 동시에 같은 블록을 들려는 이중 픽업도 같은 이유로 막힌다.
        carried.enabled = false;

        carriedKinematicWas = rb.isKinematic;
        carriedInterpWas = rb.interpolation;
        rb.isKinematic = true;
        // Interpolate가 아니라 None: Interpolate는 물리 엔진이 MovePosition으로 들어온 움직임을
        // 렌더 프레임 사이에서 보간하는 모드다. 여기선 LateUpdate에서 transform을 직접(매 렌더
        // 프레임) 갖다 박아 그 자체로 완벽한 추종을 이미 제공하는데, Interpolate가 켜진 채면 물리
        // 엔진이 "그 값을 다시 지난 프레임과 보간"하려다 매 FixedUpdate마다 위치가 살짝 튀었다
        // 복구되는 것처럼 보여 꿀렁거림/미세 회전 아티팩트로 나타난다(구가 구를 때 눈에 띔).
        rb.interpolation = RigidbodyInterpolation.None;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        IgnoreCollisionWithPlayer(carried, true);

        aimed = null;
        Debug.Log($"[BlockCarry] '{carried.name}' 을(를) 들었습니다.");
    }

    // 바라보는 방향 앞쪽 상단에 내려놓는다. 공간이 막혀 있으면 거부.
    private void TryDropForward()
    {
        Vector3 target = transform.position + LookDir() * EffectiveDropDistance() + Vector3.up * EffectiveCarryHeight();

        if (!IsDropSpaceClear(target, carriedRotation))
        {
            Debug.Log("[BlockCarry] 내려놓을 공간이 없습니다 — 벽·천장과 겹칩니다.");
            return;
        }

        PlaceCarriedAt(target, carriedRotation, "앞쪽에 내려놓음");
    }

    // 상태 꼬임 방지용 강제 드롭 — 공간 검사 없이 지금 위치에 그대로 떨군다.
    private void DropInPlace(string reason)
    {
        if (carried == null) return;
        Vector3 here = carriedBody != null ? carriedBody.transform.position
                                           : transform.position + Vector3.up * EffectiveCarryHeight();
        PlaceCarriedAt(here, carriedRotation, reason);
    }

    // 결합 직후 겹침을 푸는 중인 충돌 무시 쌍. 블록 콜라이더와 몸 콜라이더가 더 이상 겹치지 않으면 푼다.
    private bool keepIgnoreUntilClear;
    private Collider pendingBlockCol;
    private readonly System.Collections.Generic.List<Collider> pendingPlayerCols =
        new System.Collections.Generic.List<Collider>();

    private void UpdatePendingIgnore()
    {
        if (pendingBlockCol == null) { pendingPlayerCols.Clear(); return; }

        Bounds bb = pendingBlockCol.bounds;
        foreach (Collider pc in pendingPlayerCols)
            if (pc != null && pc.enabled && bb.Intersects(pc.bounds)) return;

        foreach (Collider pc in pendingPlayerCols)
            if (pc != null) Physics.IgnoreCollision(pendingBlockCol, pc, false);
        pendingPlayerCols.Clear();
        pendingBlockCol = null;
    }

    private void PlaceCarriedAt(Vector3 pos, Quaternion rot, string reason)
    {
        SnapBlock block = carried;
        Rigidbody rb = carriedBody;

        if (rb != null)
        {
            rb.transform.SetPositionAndRotation(pos, rot);
            rb.isKinematic = carriedKinematicWas;
            rb.interpolation = carriedInterpWas;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (block != null && keepIgnoreUntilClear)
        {
            pendingBlockCol = block.GetComponent<Collider>();
            pendingPlayerCols.Clear();
            pendingPlayerCols.AddRange(ignoredWithPlayer);
            ignoredWithPlayer.Clear();
            block.enabled = true;
        }
        else if (block != null)
        {
            IgnoreCollisionWithPlayer(block, false);
            block.enabled = true; // 다시 용접·도킹 대상 탐색에 보이게.
        }
        ClearCarryState();

        Debug.Log($"[BlockCarry] '{(block != null ? block.name : "블록")}' 을(를) 내려놨습니다 ({reason}).");
    }

    private void ClearCarryState()
    {
        carried = null;
        carriedBody = null;
        ignoredWithPlayer.Clear();
    }

    // ── 자동 드롭 판정 ────────────────────────────────────────

    private bool ShouldAutoDrop(out string why)
    {
        why = null;

        if (mover != null && mover.ExternallyDriven)
        {
            why = "몸이 외부 기믹에 붙잡힘(실타래 등)";
            return true;
        }

        Rigidbody myBody = mover != null ? mover.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();
        if (myBody != null && myBody.isKinematic)
        {
            why = "몸이 고정됨(굴리기 모드·벽 부착 등)";
            return true;
        }

        // 컴포넌트 존재가 아니라 모드 on 여부를 본다 — Portal은 첫 통과 때 붙인 뒤 떼지 않아서, 존재만 보면
        // 포탈을 한 번 지난 도형은 영원히 블록을 못 든다(2026-09-29 점검에서 발견).
        PlayerRollModeReceiver rollMode = GetComponent<PlayerRollModeReceiver>();
        if (rollMode != null && rollMode.RollModeActive)
        {
            why = "굴리기 모드 진입";
            return true;
        }

        if ((transform.position - lastPos).sqrMagnitude > teleportDropThreshold * teleportDropThreshold)
        {
            why = "몸이 순간이동함(리스폰·리셋)";
            return true;
        }

        if ((transform.localScale - lastScale).sqrMagnitude > 1e-4f)
        {
            why = "크기 변형 중";
            return true;
        }

        return false;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────

    // 바라보는 평면 방향: 메인 카메라 forward를 수평 투영, 없으면 이동 방향, 그것도 없으면 transform.forward.
    private Vector3 LookDir()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (f.sqrMagnitude > 1e-4f) return f.normalized;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 v = Vector3.ProjectOnPlane(rb.velocity, Vector3.up);
            if (v.sqrMagnitude > 0.04f) return v.normalized;
        }

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
    }

    // 내려놓을 자리가 비어 있는가. 블록 자신·플레이어 콜라이더·트리거는 무시.
    private bool IsDropSpaceClear(Vector3 center, Quaternion rot)
    {
        Vector3 half = CarriedHalfExtents() * 0.95f;
        // 운반 중 조준 표시가 매 프레임 부르므로 NonAlloc으로 힙 할당을 없앤다(버퍼 16개면 충분).
        int count = Physics.OverlapBoxNonAlloc(center, half, dropOverlapBuffer, rot, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider h = dropOverlapBuffer[i];
            if (h == null) continue;
            if (carried != null && h.transform.IsChildOf(carried.transform)) continue;
            if (h.transform.IsChildOf(transform) || transform.IsChildOf(h.transform)) continue;
            return false;
        }
        return true;
    }

    private static Quaternion SnapToAxes(Quaternion q)
    {
        Vector3 up = NearestAxis(q * Vector3.up);
        Vector3 fwd = Vector3.ProjectOnPlane(q * Vector3.forward, up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(q * Vector3.right, up);
        return Quaternion.LookRotation(NearestAxis(fwd), up);
    }

    private static Vector3 NearestAxis(Vector3 v)
    {
        float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
        if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
        if (ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
        return new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    // 든 블록이 정육면체 몸과 겹치지 않도록 인스펙터 값을 "최소값"으로 쓰고, 두 몸의 크기가 더 크면 그만큼
    // 띄운다. 블록이 크거나 정육면체가 커진/작아진 상태여도 머리 위·앞쪽에서 서로 파고들지 않는다.
    private Vector3 PlayerHalfExtents()
    {
        Collider pc = shapeId != null ? shapeId.solidCollider : null;
        return pc != null ? pc.bounds.extents : Vector3.one * 0.5f;
    }

    // 기준은 루트 피벗이 아니라 몸 콜라이더의 윗면이다 — 루트 피벗이 콜라이더 중심보다 아래(실측 0.5U)라서
    // "피벗 + 두 반높이"로 재면 0.5U만큼 겹쳤다(2026-10-10 Loki dbg_carry_tick: 높이 오차로 intersect=True).
    private float EffectiveCarryHeight()
    {
        Collider pc = shapeId != null ? shapeId.solidCollider : null;
        float top = pc != null ? pc.bounds.max.y - transform.position.y : PlayerHalfExtents().y;
        return Mathf.Max(carryHeight, top + CarriedHalfExtents().y + 0.05f);
    }

    private float EffectiveDropDistance()
    {
        Vector3 p = PlayerHalfExtents(), b = CarriedHalfExtents();
        return Mathf.Max(dropDistance, Mathf.Max(p.x, p.z) + Mathf.Max(b.x, b.z) + 0.1f);
    }

    private Vector3 CarriedHalfExtents()
    {
        Collider col = carriedBody != null ? carriedBody.GetComponent<Collider>() : null;
        if (col != null) return col.bounds.extents;
        return Vector3.one * 0.5f;
    }

    private void IgnoreCollisionWithPlayer(SnapBlock block, bool ignore)
    {
        Collider blockCol = block.GetComponent<Collider>();
        if (blockCol == null) return;

        if (ignore)
        {
            ignoredWithPlayer.Clear();
            foreach (Collider pc in GetComponentsInChildren<Collider>())
            {
                if (pc == null || pc == blockCol) continue;
                Physics.IgnoreCollision(blockCol, pc, true);
                ignoredWithPlayer.Add(pc);
            }
        }
        else
        {
            foreach (Collider pc in ignoredWithPlayer)
            {
                if (pc == null) continue;
                Physics.IgnoreCollision(blockCol, pc, false);
            }
            ignoredWithPlayer.Clear();
        }
    }

    // ── 조준점 ───────────────────────────────────────────────

    private void UpdateReticle()
    {
        Vector3 pos;
        Color c;

        if (carried != null)
        {
            Vector3 target = transform.position + LookDir() * EffectiveDropDistance() + Vector3.up * EffectiveCarryHeight();
            pos = target;
            dropClear = IsDropSpaceClear(target, carriedRotation);
            c = dropClear ? colorOk : colorReject;
        }
        else if (aimed != null)
        {
            pos = aimed.transform.position;
            c = rejectReason == null ? colorOk : colorReject;
        }
        else
        {
            HideReticle();
            return;
        }

        EnsureReticle();
        reticle.gameObject.SetActive(true);
        reticle.position = pos;
        if (reticleRenderer != null)
        {
            if (reticleRenderer.material.HasProperty("_BaseColor")) reticleRenderer.material.SetColor("_BaseColor", c);
            if (reticleRenderer.material.HasProperty("_Color")) reticleRenderer.material.SetColor("_Color", c);
        }
    }

    private void EnsureReticle()
    {
        if (reticle != null) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BlockCarryReticle";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.3f;
        reticleRenderer = go.GetComponent<Renderer>();
        Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        reticleRenderer.material = new Material(shader);
        reticle = go.transform;
    }

    private void HideReticle()
    {
        if (reticle != null) reticle.gameObject.SetActive(false);
    }
}
