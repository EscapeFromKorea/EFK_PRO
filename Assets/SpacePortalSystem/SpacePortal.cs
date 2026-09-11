using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 설치된 포탈 인스턴스(발신자). <see cref="PlayerEnergyReceiver"/>가 설치를 확정하면 런타임
/// AddComponent로 생성되고, 같은 색으로 재설치할 때는 새로 만들지 않고 이 인스턴스의 위치/부모만
/// 갱신한다(즉시 교체, PRD §3.2). 색당 정확히 하나만 존재한다 — 전역 정적 참조
/// <see cref="Orange"/>/<see cref="Blue"/>가 그 유일성을 보장한다.
///
/// 짝(Partner)이 없는 동안(한쪽만 설치된 상태)은 트리거가 아무 일도 하지 않는다 — "입구만 있고
/// 출구가 없는" 상태에서 들어가면 통과가 성립하지 않아야 한다(PRD §4.5).
///
/// [통과 3단계] 콜라이더가 평면에 걸쳐 있는 동안(OnTriggerEnter~Exit)은 반대편에 클론을 렌더링해
/// "일부만 진입했을 때 사라지거나 튀는" 문제를 시각적으로 없앤다. 실제 위치/속도 대입은 콜라이더
/// 중심(Rigidbody.position)이 포탈 로컬 평면(Z=0)을 완전히 넘는 순간 한 번에 일어난다(PRD §3.5).
///
/// [속도 변환, 2026-09-11 재확정] 방향은 더 이상 입구→출구 상대 회전을 그대로 따르지 않는다 —
/// 항상 출구 포탈의 forward(바깥쪽)로 곧게 나온다(보통 포탈에 수직으로 걸어 들어가므로, 대각선
/// 진입에도 늘 정면으로 튀어나오는 편이 더 자연스럽다는 실측 피드백). 속력(크기)만 입구 속도의
/// magnitude로 보존한다. <see cref="exitForwardSpeed"/>(최소 추진력)와 <see cref="exitHopSpeed"/>
/// (위로 살짝 뜨는 연출, 출구 up 방향)는 그 위에 더해지는 의도된 연출용 추가치다 — PRD §2/§3.5가
/// 원래 말한 "3축 회전 재배향"에서 방향 부분을 대체한 것이며, 회전(각속도)은 일반 Rigidbody(§3.6)
/// 한정으로 여전히 상대 회전을 그대로 쓴다(아래 else 분기).
///
/// [클론 렌더링] 클리핑 플레인(포탈 평면 기준으로 원본/클론이 겹치는 부분을 셰이더로 잘라내는 정석
/// 기법)은 안 쓴다(Built-in RP에서 공유 머티리얼마다 커스텀 셰이더를 새로 입혀야 하는 비용 — 최종
/// 보고 참고). 대신 진입 측엔 원본이, 출구 측엔 위치·회전이 매 프레임 갱신되는 "유령 사본"이 함께
/// 보이되, 진입 깊이에 비례해 0→1로 커지게 해(<see cref="RefreshCloneTransforms"/>) 걸치자마자
/// 풀 크기로 팝인하는 부자연스러움을 줄인다(2026-09-11). 짧은 겹침 구간에 이중으로 보일 수 있다는
/// 것은 여전히 감수한다.
/// </summary>
public class SpacePortal : MonoBehaviour
{
    public const float DefaultWidth = 1.6f;
    public const float DefaultHeight = 2.2f;

    [Tooltip("이 포탈의 색. PlayerEnergyReceiver가 생성 직후 설정한다.")]
    public EnergyColor color;

    [Header("크기")]
    public float width = DefaultWidth;
    public float height = DefaultHeight;
    [Tooltip("트리거 두께(Unit). 진입~완전 통과 사이 '걸쳐 있는' 구간의 여유다 — 너무 얇으면 " +
             "고속 진입 시 Enter/Exit가 한 스텝에 같이 발생해 클론 구간이 사실상 사라진다.")]
    public float triggerDepth = 1.0f;

    [Header("재진입 쿨다운")]
    [Tooltip("통과 직후 같은 쌍(이 포탈+짝)에 다시 판정되지 않는 시간(초). 기본 0.5초(PRD §5).")]
    public float reentryCooldown = 0.5f;

    [Header("출구 연출")]
    [Tooltip("출구 포탈의 로컬 forward(바깥쪽) 방향으로 미는 속도(Unit/s). 진입 속도가 거의 0이어도 " +
             "'제자리에서 튀는' 느낌이 아니라 들어간 방향으로 한 걸음 걸어나오는 느낌을 준다" +
             "(2026-09-11 확정).")]
    public float exitForwardSpeed = 1f; // 2026-09-11 하향(2→1) — 너무 세게 날아나간다는 실측 피드백
    [Tooltip("출구에서 살짝 튀어나오듯 위로 얹는 속도(Unit/s, 출구 포탈의 로컬 up 방향). PRD의 " +
             "'속력 완전 보존' 원칙에 대한 의도된 예외다(2026-09-11 확정 — 진입/통과를 더 " +
             "자연스럽게 보이게 하려는 순수 연출용 스파이크, 둘 다 0으로 두면 기존처럼 완전 보존).")]
    public float exitHopSpeed = 0.7f; // 2026-09-11 하향(1.5→0.7) — 너무 세게 날아나간다는 실측 피드백
    [Tooltip("출구 속도를 실은 뒤 PlayerMover 조작권을 이만큼(초) 더 묶어둔다. 0이면 텔레포트 직후 " +
             "바로 조작권을 돌려주는데, 그 순간 PlayerMover가 다음 FixedUpdate에서 (특히 입력이 " +
             "없으면) velocity.x/z를 즉시 0으로 재대입해 출구 속도가 한 물리 틱 만에 지워진다" +
             "(2026-09-11 실측 확인). 값을 두면 실제로 그만큼 밀려나간 뒤 조작권이 돌아온다.")]
    public float exitControlLockSeconds = 0.25f;

    [Header("테두리")]
    [Tooltip("포탈 색(주황/파랑) 테두리 선 두께(Unit). LineRenderer 사각 테두리 — 채워진 면이 " +
             "아니라 얇은 선이라 뷰스루 전체를 가리는 z-fighting 사고가 구조적으로 불가능하다" +
             "(2026-09-11 재설계, 이전 채워진 쿼드 방식의 실측 버그 대응).")]
    public float borderThickness = 0.05f;

    [Header("시각 — 포탈 너머 보기 (PRD §4.6)")]
    public bool enableViewThrough = true;
    public int renderTextureResolution = 512;

    /// <summary>씬에 색당 최대 하나만 존재한다는 것을 보장하는 전역 참조.</summary>
    public static SpacePortal Orange { get; private set; }
    public static SpacePortal Blue { get; private set; }

    /// <summary>짝 포탈. 캐시하지 않고 항상 정적 참조에서 계산한다 — 두 포탈이 서로 다른 프레임에
    /// 생성/제거돼도 항상 최신 상태다.</summary>
    public SpacePortal Partner => color == EnergyColor.Orange ? Blue : Orange;

    private static readonly Quaternion Flip180 = Quaternion.Euler(0f, 180f, 0f);

    private BoxCollider trigger;

    private class ClonePart
    {
        public Transform source;
        public Transform clone;
    }

    private class TraversalState
    {
        public bool wasInFront;
        public GameObject cloneRoot;
        public List<ClonePart> parts;
    }

    private readonly Dictionary<Rigidbody, TraversalState> tracked = new Dictionary<Rigidbody, TraversalState>();
    private readonly Dictionary<Rigidbody, float> cooldownUntil = new Dictionary<Rigidbody, float>();

    // 시각 — 재귀 렌더링
    private MeshRenderer quadRenderer;
    private Material quadMaterial;
    private Camera viewCamera;
    private RenderTexture viewTexture;
    private Texture2D blackTexture; // 짝이 없을 때 — 뷰스루 대신 위치 표시용 검정 단색(PRD 확정, 2026-09-11)
    private LineRenderer borderLine;

    // 재귀 1단계 제한(PRD §4.6): 이 프레임에 이미 한 겹 렌더 중이면, 더 안쪽 카메라는 자기
    // RenderTexture를 갱신하지 않는다(직전 프레임 값을 그대로 보여준다 — 무한 재귀 방지).
    private static int renderDepth;
    private const int MaxRenderDepth = 1;

    private void Awake()
    {
        trigger = GetComponent<BoxCollider>();
        if (trigger == null) trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(width, height, triggerDepth);

        SetupVisual();
    }

    private void OnDestroy()
    {
        PortalSurface surface = transform.parent != null ? transform.parent.GetComponent<PortalSurface>() : null;
        if (Orange == this) Orange = null;
        if (Blue == this) Blue = null;
        surface?.RefreshOccupancy(); // 점유 해제 — 이 포탈이 막아뒀던 충돌을 되돌린다.
        ClearAllTracking();
        if (viewTexture != null) Destroy(viewTexture);
        if (blackTexture != null) Destroy(blackTexture);
    }

    /// <summary>설치/즉시 교체 진입점. color는 호출자(PlayerEnergyReceiver)가 AddComponent 직후
    /// 먼저 세팅해야 한다 — 전역 등록(Orange/Blue)은 여기서, color가 확정된 뒤에 한다.</summary>
    public void PlaceAt(Vector3 center, Vector3 normal, Vector3 up, Transform parentSurface)
    {
        if (color == EnergyColor.Orange) Orange = this; else Blue = this;

        PortalSurface oldSurface = transform.parent != null ? transform.parent.GetComponent<PortalSurface>() : null;

        transform.SetParent(parentSurface, true);
        transform.position = center;
        transform.rotation = Quaternion.LookRotation(normal, up);

        trigger.size = new Vector3(width, height, triggerDepth);
        trigger.center = Vector3.zero;

        // color는 Awake()(SetupVisual 호출 시점)보다 나중에 확정되므로 테두리 색은 여기서 매번
        // 다시 입힌다(재설치/이동 때도 안전하게 idempotent).
        if (borderLine != null)
        {
            Color c = color == EnergyColor.Orange ? new Color(1f, 0.55f, 0.1f) : new Color(0.25f, 0.55f, 1f);
            borderLine.startColor = c;
            borderLine.endColor = c;
        }

        // 위치가 바뀌면 이전 straddle 상태(어느 쪽에 있었는지)는 무의미해진다 — 진행 중이던 클론도 버린다.
        ClearAllTracking();

        PortalSurface newSurface = parentSurface != null ? parentSurface.GetComponent<PortalSurface>() : null;
        newSurface?.RefreshOccupancy();
        if (oldSurface != null && oldSurface != newSurface) oldSurface.RefreshOccupancy();
    }

    private bool IsInFront(Vector3 worldPos) => transform.InverseTransformPoint(worldPos).z >= 0f;

    private void OnTriggerEnter(Collider other)
    {
        if (Partner == null) return;

        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;

        PlayerShapeIdentity identity = body.GetComponentInParent<PlayerShapeIdentity>();
        if (identity != null)
        {
            // [이중 발화 방지] 플레이어는 Player_Mesh(트리거)/Player_Collider(솔리드) 두 콜라이더를
            // 갖는다 — 솔리드 하나만 받는다(Portal.cs와 같은 관례).
            bool isSolid = identity.solidCollider != null ? identity.solidCollider == other : !other.isTrigger;
            if (!isSolid) return;
        }
        else if (body.GetComponent<PortalTraversable>() == null)
        {
            return; // 마커 없는 일반 Rigidbody는 통과 대상이 아니다(PRD §3.6).
        }

        if (cooldownUntil.TryGetValue(body, out float until) && Time.time < until) return;
        if (tracked.ContainsKey(body)) return;

        tracked[body] = new TraversalState { wasInFront = IsInFront(body.position) };
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;

        PlayerShapeIdentity identity = body.GetComponentInParent<PlayerShapeIdentity>();
        if (identity != null)
        {
            bool isSolid = identity.solidCollider != null ? identity.solidCollider == other : !other.isTrigger;
            if (!isSolid) return;
        }

        if (tracked.TryGetValue(body, out TraversalState st))
        {
            DestroyClone(st);
            tracked.Remove(body);
        }
    }

    private void FixedUpdate()
    {
        if (Partner == null)
        {
            if (tracked.Count > 0) ClearAllTracking();
            return;
        }

        List<Rigidbody> crossed = null;
        List<Rigidbody> stale = null;
        foreach (KeyValuePair<Rigidbody, TraversalState> kv in tracked)
        {
            Rigidbody body = kv.Key;
            if (body == null)
            {
                // 트리거에 걸쳐 있던 바디가 OnTriggerExit 없이 파괴된 경우(겹친 채 Destroy() 등) —
                // 클론 GameObject가 정리 안 된 채 영구히 남는 걸 막는다(2026-09-10 리뷰에서 발견).
                DestroyClone(kv.Value);
                (stale ??= new List<Rigidbody>()).Add(body);
                continue;
            }
            TraversalState st = kv.Value;

            if (st.cloneRoot == null) BuildClone(body, st);
            // 이 포탈(진입 측) 평면까지 남은 거리에 비례해 클론을 0→1로 키운다 — 걸치자마자 풀
            // 크기로 팝인하던 것을 "건너가는 만큼 드러난다"는 느낌으로 바꾼다(클리핑 플레인 없이
            // 순수 스케일만으로 얻는 근사치, 2026-09-11). 완전히 넘는 순간(텔레포트)엔 1에 도달해
            // 원본과 자연스럽게 이어진다.
            float localZ = transform.InverseTransformPoint(body.position).z;
            float revealFrac = Mathf.Clamp01(1f - Mathf.Abs(localZ) / Mathf.Max(0.01f, triggerDepth * 0.5f));
            RefreshCloneTransforms(st, revealFrac);

            bool inFront = IsInFront(body.position);
            if (inFront != st.wasInFront)
                (crossed ??= new List<Rigidbody>()).Add(body);
        }

        if (stale != null)
            foreach (Rigidbody body in stale)
                tracked.Remove(body);

        if (crossed != null)
            foreach (Rigidbody body in crossed)
                TeleportAcross(body);

        PruneCooldowns();
    }

    // cooldownUntil은 tracked와 달리 진입/이탈 이벤트가 아니라 텔레포트 시점에만 채워지므로, 만료되거나
    // 대상이 파괴된 항목을 여기서 직접 걷어내지 않으면 포탈 사용 횟수만큼 영구히 쌓인다(2026-09-10 리뷰).
    private void PruneCooldowns()
    {
        if (cooldownUntil.Count == 0) return;
        List<Rigidbody> expired = null;
        foreach (KeyValuePair<Rigidbody, float> kv in cooldownUntil)
            if (kv.Key == null || kv.Value < Time.time)
                (expired ??= new List<Rigidbody>()).Add(kv.Key);
        if (expired != null)
            foreach (Rigidbody body in expired)
                cooldownUntil.Remove(body);
    }

    // ── 텔레포트 ─────────────────────────────────────────────────────────────

    private void TeleportAcross(Rigidbody body)
    {
        SpacePortal partner = Partner;
        if (partner == null) return;
        if (!tracked.TryGetValue(body, out TraversalState st)) return;

        DestroyClone(st);
        tracked.Remove(body);

        Vector3 localPos = transform.InverseTransformPoint(body.position);
        Vector3 newPos = partner.transform.TransformPoint(Flip180 * localPos);

        // 방향은 더 이상 입구→출구 상대 회전으로 재배향하지 않는다 — 보통 포탈은 면에 수직으로
        // 걸어 들어가므로, 나올 때도 항상 출구 면 바깥쪽(forward)으로 곧게 나오는 편이 훨씬
        // 자연스럽다(2026-09-11 확정, PRD §2/§3.5의 "3축 회전 재배향"에서 방향 부분을 대체 —
        // 속력(크기)은 여전히 보존한다). 대각선으로 비스듬히 들어가도 항상 정면으로 튀어나온다.
        // exitForwardSpeed는 진입 속도가 0에 가까워도 최소 추진력을 보장하고, exitHopSpeed는
        // 살짝 뜨는 연출을 더한다.
        float speed = body.velocity.magnitude;
        Vector3 newVel = partner.transform.forward * (speed + partner.exitForwardSpeed) + partner.transform.up * partner.exitHopSpeed;

        // ponytail: "벽 방향으로 나간다" 신고 진단용 임시 계측(Loki). partner.transform.forward가
        // 실제로 벽 쪽을 향하고 있는지(= 그 패널의 rotation이 뒤집혀 있는지)를 직접 확인한다.
        bool blockedAhead = Physics.Raycast(partner.transform.position, partner.transform.forward, out RaycastHit fwdHit, 1.5f, ~0, QueryTriggerInteraction.Ignore);
        LokiTelemetry.Event("exit_debug",
            $"exitPortal={partner.color} fwd={partner.transform.forward:F2} newVel={newVel:F2} blockedAhead={blockedAhead} blockedBy={(blockedAhead ? fwdHit.collider.name : "none")}");

        PlayerShapeIdentity identity = body.GetComponentInParent<PlayerShapeIdentity>();
        if (identity != null)
        {
            // 플레이어는 몸 회전을 건드리지 않는다(§2 — FreezeRotation이라 회전이라는 물리량이 없다).
            StartCoroutine(TeleportPlayerRoutine(identity.GetComponent<PlayerMover>(), body, newPos, newVel, partner.exitControlLockSeconds));
        }
        else
        {
            // 일반 Rigidbody(PortalTraversable)는 회전도 실제 변환 대상이다(§3.6).
            Vector3 localAngVel = transform.InverseTransformDirection(body.angularVelocity);
            Vector3 newAngVel = partner.transform.TransformDirection(Flip180 * localAngVel);
            Quaternion rotDelta = partner.transform.rotation * Flip180 * Quaternion.Inverse(transform.rotation);

            body.position = newPos;
            body.rotation = rotDelta * body.rotation;
            body.velocity = newVel;
            body.angularVelocity = newAngVel;
        }

        float until = Time.time + Mathf.Max(reentryCooldown, partner.reentryCooldown);
        cooldownUntil[body] = until;
        partner.cooldownUntil[body] = until;
    }

    /// <summary>플레이어 전용 텔레포트. RespawnController.TeleportRoutine과 같은 패턴 — 텔레포트
    /// 프레임엔 isKinematic을 잠깐 켜 PlayerMover의 같은 스텝 velocity 재대입에 씹히지 않게 하고,
    /// 다음 프레임에 풀며 목표 속도를 대입한다(PRD §3.5, 2026-09-10 확정 §1 항목 11).
    /// 같은 프레임에 PlayerFollowCamera.SnapToTarget()도 호출한다(§1 항목 12).
    ///
    /// isKinematic만으로는 부족하다 — isKinematic을 다음 프레임에 풀자마자 PlayerMover.FixedUpdate가
    /// 그 스텝의 LegacyVelocityFixedUpdate에서 rb.velocity.x/z를 정상 이동 로직으로 재대입해, 방금
    /// 회전시킨 운동량 보존 속도를 한 물리 틱 만에 덮어써버린다. RespawnController는 이 문제를
    /// 호출자가 ExternallyDriven을 걸고/풀어서 피하므로(RespawnController.cs), 여기서도 같은 패턴을
    /// 직접 건다(2026-09-10 리뷰에서 발견 — 이 기믹의 헤드라인 기능인 운동량 보존이 실제로는
    /// 작동하지 않고 있었다).</summary>
    private IEnumerator TeleportPlayerRoutine(PlayerMover mover, Rigidbody rb, Vector3 newPos, Vector3 newVel, float controlLockSeconds)
    {
        if (rb == null) yield break;

        bool prevKinematic = rb.isKinematic;
        bool prevDriven = mover != null && mover.ExternallyDriven;
        RigidbodyInterpolation prevInterp = rb.interpolation;
        rb.interpolation = RigidbodyInterpolation.None; // 순간이동 한 프레임 늘어짐 방지(RespawnController와 동일 이유)
        rb.isKinematic = true;
        if (mover != null) mover.ExternallyDriven = true;

        rb.position = newPos;
        if (mover != null) mover.transform.position = newPos;

        // 부스트 중이면 다음 FixedUpdate에 velocity가 통째로 되살아난다(AccelSystem 관용구).
        PlayerAccelReceiver accel = rb.GetComponent<PlayerAccelReceiver>();
        if (accel != null) accel.CancelBoost();

        PlayerFollowCamera.SnapToTarget(rb.transform);

        yield return new WaitForFixedUpdate();

        if (rb == null) yield break;
        rb.isKinematic = prevKinematic;
        rb.interpolation = prevInterp;
        rb.velocity = newVel;

        // ExternallyDriven을 여기서 바로 풀면 다음 FixedUpdate에 PlayerMover.LegacyVelocityFixedUpdate가
        // (입력이 없으면 특히) velocity.x/z를 즉시 0으로 재대입해 방금 넣은 출구 속도가 물리 틱 하나
        // 만에 지워진다 — "면 방향으로 곧게 나오게" 했는데 실제로는 거의 안 보이던 원인이었다
        // (2026-09-11 실측 확인). 잠깐 더 잠가 실제로 그만큼 밀려나간 뒤 조작권을 돌려준다.
        if (mover != null && controlLockSeconds > 0f)
            yield return new WaitForSeconds(controlLockSeconds);

        if (mover != null) mover.ExternallyDriven = prevDriven;
    }

    // ── 클론 렌더링 ──────────────────────────────────────────────────────────

    private void BuildClone(Rigidbody body, TraversalState st)
    {
        st.cloneRoot = new GameObject($"PortalClone_{body.name}");
        st.parts = new List<ClonePart>();

        foreach (MeshFilter mf in body.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null) continue;

            GameObject part = new GameObject("Part");
            part.transform.SetParent(st.cloneRoot.transform, false);
            MeshFilter cmf = part.AddComponent<MeshFilter>();
            cmf.sharedMesh = mf.sharedMesh;
            MeshRenderer cmr = part.AddComponent<MeshRenderer>();
            cmr.sharedMaterials = mr.sharedMaterials;

            st.parts.Add(new ClonePart { source = mf.transform, clone = part.transform });
        }
    }

    private void RefreshCloneTransforms(TraversalState st, float revealFrac)
    {
        SpacePortal partner = Partner;
        if (st.cloneRoot == null || partner == null || st.parts == null) return;

        Quaternion rotDelta = partner.transform.rotation * Flip180 * Quaternion.Inverse(transform.rotation);
        foreach (ClonePart p in st.parts)
        {
            if (p.source == null || p.clone == null) continue;
            Vector3 local = transform.InverseTransformPoint(p.source.position);
            p.clone.position = partner.transform.TransformPoint(Flip180 * local);
            p.clone.rotation = rotDelta * p.source.rotation;
            p.clone.localScale = p.source.lossyScale * revealFrac;
        }
    }

    private void DestroyClone(TraversalState st)
    {
        if (st.cloneRoot != null) Destroy(st.cloneRoot);
        st.cloneRoot = null;
        st.parts = null;
    }

    private void ClearAllTracking()
    {
        foreach (TraversalState st in tracked.Values) DestroyClone(st);
        tracked.Clear();
    }

    // ── 시각 — 재귀 렌더링 ───────────────────────────────────────────────────

    private void SetupVisual()
    {
        // 테두리 — 채워진 쿼드를 메인 쿼드 뒤에 겹쳐 두는 방식은 오프셋이 조금만 부족해도
        // z-fighting으로 뷰스루 전체를 가려버렸다(2026-09-11 실측). LineRenderer 사각 테두리로
        // 바꿨다 — 얇은 선이라 채워진 면이 아예 없어, 겹치는 사고가 구조적으로 불가능하다.
        GameObject borderGo = new GameObject("PortalBorder");
        borderGo.transform.SetParent(transform, false);
        borderGo.transform.localPosition = Vector3.zero;
        borderLine = borderGo.AddComponent<LineRenderer>();
        borderLine.useWorldSpace = false;
        borderLine.loop = true;
        borderLine.positionCount = 4;
        borderLine.widthMultiplier = borderThickness;
        borderLine.material = new Material(Shader.Find("Sprites/Default")); // color는 PlaceAt()에서 입힌다.
        borderLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float bhw = width * 0.5f, bhh = height * 0.5f;
        borderLine.SetPositions(new[]
        {
            new Vector3(-bhw, -bhh, 0f), new Vector3(bhw, -bhh, 0f),
            new Vector3(bhw, bhh, 0f), new Vector3(-bhw, bhh, 0f),
        });

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PortalVisual";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(transform, false);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localScale = new Vector3(width, height, 1f);
        quadRenderer = quad.GetComponent<MeshRenderer>();
        // Cull Off 전용 셰이더 — 기본 Unlit/Texture는 backface culling이 있어 접근하는 쪽에서
        // 안 보이고 반대쪽(벽 뒤)에서만 보이는 방향 문제가 났다(2026-09-11 실측).
        quadMaterial = new Material(Shader.Find("SpacePortalSystem/PortalVisual"));
        quadRenderer.sharedMaterial = quadMaterial;

        blackTexture = new Texture2D(1, 1);
        blackTexture.SetPixel(0, 0, Color.black);
        blackTexture.Apply();
        quadMaterial.mainTexture = blackTexture; // 짝이 생기기 전엔 검정 단색 — 설치 위치는 항상 보인다.

        GameObject camGo = new GameObject("PortalViewCamera");
        camGo.transform.SetParent(transform, false);
        viewCamera = camGo.AddComponent<Camera>();
        viewCamera.enabled = false; // 자동 렌더 루프를 타지 않는다 — LateUpdate에서 수동 Render()만 쓴다.
        viewTexture = new RenderTexture(Mathf.Max(64, renderTextureResolution), Mathf.Max(64, renderTextureResolution), 16);
        viewCamera.targetTexture = viewTexture;
    }

    private void LateUpdate()
    {
        SpacePortal partner = Partner;
        if (quadRenderer == null) return;

        // 포탈은 짝의 존재 여부와 무관하게 항상 보인다 — 짝이 없으면(단독 설치) 검정 단색으로 위치만
        // 표시하고, 짝이 생기면 뷰스루로 전환한다(2026-09-11 확정 — 예전엔 짝 없을 때 완전히
        // 안 보여 어디에 설치했는지 알 수 없었다).
        if (!enableViewThrough || partner == null || viewCamera == null)
        {
            quadMaterial.mainTexture = blackTexture;
            return;
        }
        quadMaterial.mainTexture = viewTexture;

        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        // 메인 카메라를 "이 포탈 → 짝 포탈" 상대 변환으로 옮긴 위치/방향에서 찍어, 이 포탈 면에는
        // "짝 포탈 저편에서 본 장면"이 비친다(표준 포탈 카메라 트릭).
        Quaternion rotDelta = partner.transform.rotation * Flip180 * Quaternion.Inverse(transform.rotation);
        Vector3 localCamPos = transform.InverseTransformPoint(mainCam.transform.position);
        viewCamera.transform.position = partner.transform.TransformPoint(Flip180 * localCamPos);
        viewCamera.transform.rotation = rotDelta * mainCam.transform.rotation;
        viewCamera.fieldOfView = mainCam.fieldOfView;
        viewCamera.nearClipPlane = Mathf.Max(0.05f, mainCam.nearClipPlane);
        viewCamera.farClipPlane = mainCam.farClipPlane;

        // [클리핑 플레인 생략 — 알려진 한계] 정식 포탈 카메라는 이 근평면을 짝 포탈 표면에 딱
        // 맞춰(oblique near-clip, GL.CalculateObliqueMatrix) 포탈 뒤/앞의 기하가 새어 보이지
        // 않게 한다. 여기서는 near=0.05 고정만 쓴다 — 포탈 바로 앞의 가느다란 벽 조각이 잠깐
        // 비칠 수 있다는 것을 알고 감수한 타협이다(최종 보고 참고).
        if (renderDepth >= MaxRenderDepth) return; // 재귀 1단계 제한 — 더 안쪽은 직전 프레임 텍스처 그대로.
        renderDepth++;
        viewCamera.Render();
        renderDepth--;
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Partner != null ? new Color(0.2f, 0.9f, 0.4f, 0.4f) : new Color(0.9f, 0.7f, 0.2f, 0.4f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(width, height, 0.05f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
