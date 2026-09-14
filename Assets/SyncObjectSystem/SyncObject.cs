using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 동기화 오브젝트. 같은 <see cref="pairId"/> + 같은 <see cref="shapeKind"/>를 가진 두 개가 짝을
/// 맺어, 한쪽(Leader)의 움직임을 다른 쪽(Follower)이 미러한다. 유리벽·다른 방으로 분리된 공간의
/// 플랫폼·엘리베이터·무게 발판을 원격 조작하는 데 쓴다.
///
/// [피드백 루프 / 오차 누적 방지]
///  · Follower는 Rigidbody를 isKinematic으로 두어 플레이어가 못 민다 → 독립 입력이 없어 에코가
///    생기지 않는다.
///  · 타깃은 항상 "초기 위치 + 오프셋" <b>절대값</b>으로 계산한다. 프레임 델타를 누적하지 않으므로
///    장시간 움직여도 두 물체 사이 오차가 쌓이지 않는다.
///  · Bidirectional에서는 sync를 적용한 직후 <see cref="feedbackGuardSeconds"/> 동안 자기 변위를
///    입력으로 읽지 않는다.
///
/// [떨림 / 폭주 억제]
///  · Follower는 FixedUpdate에서 <see cref="Rigidbody.MovePosition"/> / MoveRotation으로 구동한다.
///  · 장애물에 부딪혀도 kinematic이라 관통/정지할 뿐 물리가 폭주하지 않는다.
///
/// [규약] 전역 Physics.* 미변경(MP-01). PlayerSystem·씬 무수정.
/// </summary>
[DisallowMultipleComponent]
public class SyncObject : MonoBehaviour
{
    public enum SyncMode { PositionShared, PositionMirrored, Rotation, WeightResponse }
    public enum SyncRole { Leader, Follower, Bidirectional }

    [Header("페어링")]
    [Tooltip("같은 값을 가진 SyncObject 2개가 짝을 맺는다.")]
    public string pairId = "A";
    [Tooltip("동일 도형끼리만 동기화된다 — 짝의 shapeKind가 다르면 페어링을 거부한다.")]
    public PlayerShapeStats.ShapeKind shapeKind = PlayerShapeStats.ShapeKind.Cube;

    [Header("역할 / 모드")]
    [Tooltip("Leader = 플레이어가 미는 다이나믹 / Follower = kinematic, 짝을 미러 / Bidirectional = 양쪽 다이나믹, 변위가 큰 쪽이 순간 리더.")]
    public SyncRole role = SyncRole.Follower;
    public SyncMode mode = SyncMode.PositionShared;

    [Header("축 / 범위 / 방향")]
    public bool syncX = true;
    public bool syncY = true;
    public bool syncZ = true;
    [Tooltip("초기 위치 기준 허용 오프셋(Unit). 초과분은 축별로 클램프된다.")]
    public Vector3 moveRange = new Vector3(5f, 5f, 5f);
    [Tooltip("Rotation 모드에서 허용 회전 범위(도).")]
    public float rotationRange = 180f;
    [Tooltip("반대 방향으로 움직이게 한다. PositionMirrored 모드는 항상 반전이다.")]
    public bool invert = false;
    [Tooltip("크기가 다른 공간 대응 — 짝의 오프셋에 이 비율을 곱해 따라간다.")]
    public float followRatio = 1f;

    [Header("무게 반응 (mode = WeightResponse)")]
    [Tooltip("이 오브젝트 위 총 질량이 이 값 이상이면 짝이 눌린다.")]
    public float weightThreshold = 2f;
    [Tooltip("눌렸을 때 짝이 내려가는 깊이(Unit, -Y).")]
    public float weightPressDepth = 1f;

    [Header("Bidirectional")]
    [Tooltip("이 거리(Unit) 미만의 프레임 변위는 리더 판정에서 무시한다.")]
    public float moveDeadzone = 0.05f;
    [Tooltip("sync를 적용한 뒤 자기 변위를 입력으로 다시 읽기까지의 유예(초).")]
    public float feedbackGuardSeconds = 0.1f;

    [Header("시각 (LineRenderer)")]
    [Tooltip("연결선 색 — 플레이어 도형 색과 맞춘다.")]
    public Color pairColor = Color.cyan;
    [Tooltip("범위 이탈·링크 끊김 시 연결선 색.")]
    public Color blockedColor = new Color(1f, 0.3f, 0.3f);
    public float lineWidth = 0.06f;

    private static readonly Dictionary<string, List<SyncObject>> registry =
        new Dictionary<string, List<SyncObject>>();

    // Fast Enter Play Mode(도메인 리로드 생략) 시 이전 세션 잔재가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => registry.Clear();

    private SyncObject partner;
    private Rigidbody body;
    private Vector3 initialPos;
    private Quaternion initialRot;

    private Vector3 lastFramePos;
    private float guardUntil;
    private bool linkBlocked;

    private LineRenderer line;

    private readonly Dictionary<Rigidbody, int> overlaps = new Dictionary<Rigidbody, int>();

    /// <summary>이 오브젝트가 리더로서 내보내는, 클램프된 위치 오프셋(축 마스크 적용).</summary>
    public Vector3 SharedOffset { get; private set; }
    /// <summary>이 오브젝트가 리더로서 내보내는 회전 오프셋(도, Rotation 모드).</summary>
    public Vector3 SharedEuler { get; private set; }

    private bool ForceInvert => mode == SyncMode.PositionMirrored || invert;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        initialPos = transform.position;
        initialRot = transform.rotation;
        lastFramePos = initialPos;

        if (role == SyncRole.Follower && body != null)
            body.isKinematic = true;

        if (!registry.TryGetValue(pairId, out List<SyncObject> list))
        {
            list = new List<SyncObject>();
            registry[pairId] = list;
        }
        list.Add(this);
    }

    private void OnDestroy()
    {
        if (registry.TryGetValue(pairId, out List<SyncObject> list))
        {
            list.Remove(this);
            if (list.Count == 0) registry.Remove(pairId);
        }
        if (line != null) Destroy(line.gameObject);
    }

    private void Start()
    {
        ResolvePartner();
        BuildLine();
    }

    private void ResolvePartner()
    {
        partner = null;
        if (!registry.TryGetValue(pairId, out List<SyncObject> list)) return;

        List<SyncObject> matches = new List<SyncObject>();
        foreach (SyncObject s in list)
        {
            if (s == null || s == this) continue;
            if (s.shapeKind != shapeKind)
            {
                Debug.LogWarning($"[SyncObject] pairId '{pairId}' 짝의 도형이 다릅니다 " +
                                 $"({shapeKind} vs {s.shapeKind}) — 동일 도형끼리만 동기화됩니다.", this);
                continue;
            }
            matches.Add(s);
        }

        if (matches.Count == 0)
        {
            Debug.LogWarning($"[SyncObject] pairId '{pairId}' 짝을 찾지 못했습니다.", this);
            return;
        }
        if (matches.Count > 1)
            Debug.LogWarning($"[SyncObject] pairId '{pairId}'에 SyncObject가 3개 이상입니다 — 첫 번째만 짝으로 씁니다.", this);

        partner = matches[0];
        Debug.Log($"[SyncObject] '{name}' ↔ '{partner.name}' 페어링 완료 (pairId '{pairId}', " +
                  $"{shapeKind}, role={role}, mode={mode}).", this);
    }

    private void FixedUpdate()
    {
        if (partner == null)
        {
            SetLinkBlocked(true);
            UpdateLine();
            return;
        }

        bool isDriver = role == SyncRole.Leader ||
                        (role == SyncRole.Bidirectional && IsBidirectionalDriver());

        if (isDriver)
            DriveAsLeader();
        else
            FollowPartner();

        UpdateLine();
        lastFramePos = transform.position;
    }

    // Bidirectional: 이번 프레임 자기 변위가 데드존을 넘고, 유예 중이 아니면 리더.
    private bool IsBidirectionalDriver()
    {
        if (Time.time < guardUntil) return false;
        return (transform.position - lastFramePos).magnitude > moveDeadzone;
    }

    private void DriveAsLeader()
    {
        if (mode == SyncMode.Rotation)
        {
            Vector3 e = NormalizeAngles((Quaternion.Inverse(initialRot) * transform.rotation).eulerAngles);
            e = new Vector3(
                syncX ? Mathf.Clamp(e.x, -rotationRange, rotationRange) : 0f,
                syncY ? Mathf.Clamp(e.y, -rotationRange, rotationRange) : 0f,
                syncZ ? Mathf.Clamp(e.z, -rotationRange, rotationRange) : 0f);
            SharedEuler = e;
            return;
        }

        Vector3 offset;
        if (mode == SyncMode.WeightResponse)
        {
            offset = new Vector3(0f, TotalOverlapMass() >= weightThreshold ? -weightPressDepth : 0f, 0f);
        }
        else
        {
            offset = transform.position - initialPos;
        }

        bool clamped = false;
        offset = new Vector3(
            AxisComponent(offset.x, moveRange.x, syncX, ref clamped),
            AxisComponent(offset.y, moveRange.y, syncY, ref clamped),
            AxisComponent(offset.z, moveRange.z, syncZ, ref clamped));

        SharedOffset = offset;
        SetLinkBlocked(clamped);
    }

    private void FollowPartner()
    {
        float sign = ForceInvert ? -1f : 1f;

        if (partner.mode == SyncMode.Rotation || mode == SyncMode.Rotation)
        {
            Quaternion target = initialRot * Quaternion.Euler(partner.SharedEuler * (sign * followRatio));
            if (body != null && body.isKinematic) body.MoveRotation(target);
            else transform.rotation = target;
        }
        else
        {
            Vector3 target = initialPos + partner.SharedOffset * (sign * followRatio);
            if (body != null && body.isKinematic) body.MovePosition(target);
            else transform.position = target;
        }

        SetLinkBlocked(partner.linkBlocked);
        if (role == SyncRole.Bidirectional)
            guardUntil = Time.time + feedbackGuardSeconds;
    }

    private static float AxisComponent(float value, float range, bool enabled, ref bool clamped)
    {
        if (!enabled) return 0f;
        float c = Mathf.Clamp(value, -Mathf.Abs(range), Mathf.Abs(range));
        if (!Mathf.Approximately(c, value)) clamped = true;
        return c;
    }

    private static Vector3 NormalizeAngles(Vector3 e) =>
        new Vector3(NormalizeAngle(e.x), NormalizeAngle(e.y), NormalizeAngle(e.z));

    private static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        if (a < -180f) a += 360f;
        return a;
    }

    private float TotalOverlapMass()
    {
        float sum = 0f;
        foreach (KeyValuePair<Rigidbody, int> kv in overlaps)
            if (kv.Key != null) sum += kv.Key.mass;
        return sum;
    }

    private void OnCollisionEnter(Collision c)
    {
        if (c.rigidbody == null) return;
        overlaps.TryGetValue(c.rigidbody, out int n);
        overlaps[c.rigidbody] = n + 1;
    }

    private void OnCollisionExit(Collision c)
    {
        if (c.rigidbody == null) return;
        if (overlaps.TryGetValue(c.rigidbody, out int n))
        {
            if (n <= 1) overlaps.Remove(c.rigidbody);
            else overlaps[c.rigidbody] = n - 1;
        }
    }

    /// <summary>초기 위치·회전으로 되돌리고 상태를 초기화한다(퍼즐 리셋 시스템이 호출).</summary>
    public void ResetSync()
    {
        transform.SetPositionAndRotation(initialPos, initialRot);
        if (body != null && !body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        SharedOffset = Vector3.zero;
        SharedEuler = Vector3.zero;
        guardUntil = 0f;
        lastFramePos = initialPos;
        overlaps.Clear();
        SetLinkBlocked(false);
    }

    // ── 시각 ─────────────────────────────────────────────────

    private void BuildLine()
    {
        GameObject go = new GameObject($"SyncLink_{pairId}");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.widthMultiplier = lineWidth;
        line.useWorldSpace = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.textureMode = LineTextureMode.Tile;   // pairId별 대시 질감 구분 여지
        UpdateLine();
    }

    private void UpdateLine()
    {
        if (line == null) return;
        bool show = partner != null;
        line.enabled = show;
        if (!show) return;

        line.SetPosition(0, transform.position);
        line.SetPosition(1, partner.transform.position);
        Color c = linkBlocked ? blockedColor : pairColor;
        line.startColor = c;
        line.endColor = c;
    }

    // 상태가 바뀔 때만 로그(매 FixedUpdate 호출되므로 스팸 방지).
    private void SetLinkBlocked(bool blocked)
    {
        if (blocked == linkBlocked) { linkBlocked = blocked; return; }
        linkBlocked = blocked;
        if (blocked) Debug.Log($"[SyncObject] '{name}' 범위 초과/링크 끊김 — 연결선 빨강.", this);
        else Debug.Log($"[SyncObject] '{name}' 범위 내로 복귀 — 연결선 정상.", this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = pairColor;
        Vector3 c = Application.isPlaying ? initialPos : transform.position;
        Gizmos.DrawWireCube(c, Vector3.Scale(moveRange, new Vector3(
            syncX ? 2f : 0f, syncY ? 2f : 0f, syncZ ? 2f : 0f)) + Vector3.one * 0.05f);
    }
}
