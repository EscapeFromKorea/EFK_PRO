using System.Collections;
using UnityEngine;

/// <summary>
/// 체크포인트 구역 — 플레이어가 들어오는 즉시 "마지막 복귀 지점"으로 저장되는 트리거 볼륨이다
/// (HourglassSystem/SlowZone과 같은 계열: 씬에 박스를 놓고 크기로 범위를 정한다).
///
/// 별도 상호작용 없이 밟는 즉시 저장되는 이유는 안전망이 조용해야 하기 때문이다 — "발견해서
/// 활성화하는" 체크포인트는 놓치고 지나간 플레이어를 훨씬 뒤로 되돌려 벌을 준다.
///
/// 복귀 지점 두 개가 이 볼륨 하나에서 파생된다(별도 배치 지점을 두지 않는다):
/// - 낙하 스폰 = 상단면 중앙(살짝 안쪽). 낙하 리스폰이 여기서 시작하므로 <b>구역 높이가 곧 낙하
///   연출의 길이</b>다 — 위아래로 길게 만든다(권장 15~20 Unit, 약 1.7~2초 낙하). 낮으면 "떨어진다"가
///   아니라 "툭 떨어뜨린다"가 된다.
/// - 페이드 스폰 = 중앙 X/Z의 <b>실제 바닥</b>. 볼륨 밑면을 바닥으로 쓰면 지형에 딱 맞춰 배치해야만
///   정상 동작하고 조금만 파묻히면 플레이어가 바닥 속에서 나타나므로, 구역 중앙에서 아래로 레이를
///   쏘아 찾는다. 레이는 매 프레임이 아니라 <b>체크포인트로 저장되는 순간 1회</b>만 쏜다.
///
/// 저장 대상은 씬에 하나뿐인 RespawnController다(체크포인트는 세 도형이 공유하며 마지막에 갱신된
/// 하나만 유지된다 — 카운터가 공유인데 체크포인트만 도형별이면 "팀 단위 실패"라는 전제와 어긋난다).
///
/// [깃발 = 3단계 시각화]
/// 볼륨은 눈에 안 보이므로 "여기가 체크포인트다 / 내가 잡았다 / 지금 되돌아갈 곳은 여기다"를
/// 막대 위 깃발로 알린다. 상태가 셋인 이유: <b>미방문 = 깃발 없음</b>(아직 안 잡은 곳),
/// <b>방문했지만 비활성 = 흰 깃발</b>(지나온 경로), <b>활성 = 초록 깃발</b>(지금 되돌아갈 곳).
/// 잡은 곳을 전부 초록으로 두면 실제로 저장된 체크포인트는 하나뿐인데 여러 개가 켜져 있어
/// <b>UI가 거짓말을 한다</b> — 초록은 언제나 정확히 하나다.
///
/// 상태 전환은 컨트롤러가 밀어 준다(구역이 스스로 "내가 활성인가"를 물어보지 않는다) — 활성
/// 체크포인트가 하나뿐이라는 사실이 컨트롤러의 필드 하나로 이미 표현돼 있어서, 구역이 그걸 다시
/// 추적하면 두 벌의 진실이 생긴다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RespawnZone : MonoBehaviour
{
    public enum FlagState { Hidden, Visited, Active }

    [Header("스폰 지점")]
    [Tooltip("낙하 스폰을 구역 상단면에서 이만큼 아래로 내린다(Unit). 상단면에 딱 붙이면 도형 몸이 " +
             "구역 밖으로 튀어나온 채 시작해 천장 지형에 끼일 수 있다.")]
    public float dropSpawnInset = 0.5f;

    [Tooltip("바닥을 찾는 레이를 구역 밑면보다 이만큼 더 아래까지 쏜다(Unit). 볼륨을 지형에서 살짝 " +
             "띄워 배치해도 바닥을 찾게 해주는 여유분이다.")]
    public float groundRayExtra = 2f;

    [Header("깃발 (시각화 — 비워 두면 깃발 없이 동작한다)")]
    [Tooltip("게양될 깃발의 Renderer. 막대(Pole) 아래에 두고, 인스펙터에 배치한 위치가 '게양 완료' " +
             "지점이 된다(게양 연출은 아래 raiseFromLocalY에서 여기까지 올라온다).")]
    public Renderer flagRenderer;

    [Tooltip("지금 되돌아갈 체크포인트일 때의 색. 씬 전체에서 정확히 하나만 이 색이다.")]
    public Color activeColor = new Color(0.25f, 0.9f, 0.4f);

    [Tooltip("잡았지만 지금은 활성이 아닌 체크포인트의 색(지나온 경로 표시).")]
    public Color visitedColor = Color.white;

    [Tooltip("깃발이 처음 게양될 때 올라오는 시간(초). 0이면 즉시 나타난다 — 툭 생기면 버그처럼 보인다.")]
    public float raiseSeconds = 0.4f;

    [Tooltip("게양 시작 높이(깃발 부모 기준 로컬 Y). 막대 밑동이 부모의 원점이면 0 그대로 두면 된다.")]
    public float raiseFromLocalY = 0f;

    private Collider zoneCollider;
    private FlagState flagState = FlagState.Hidden;
    private Vector3 flagRaisedLocalPos;
    private MaterialPropertyBlock flagBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        zoneCollider = GetComponent<Collider>();
        if (!zoneCollider.isTrigger)
            Debug.LogError("[RespawnZone] Collider가 Trigger가 아니다. isTrigger를 켜라.", this);

        if (flagRenderer == null) return;
        // 인스펙터에 배치된 위치가 게양 완료 지점이다. 게양 전에는 밑동으로 내려 두고 숨긴다
        // (에디터에서 깃발이 보이는 채로 저장됐을 수 있어 런타임에 강제로 끈다).
        flagRaisedLocalPos = flagRenderer.transform.localPosition;
        flagBlock = new MaterialPropertyBlock();
        flagRenderer.enabled = false;
    }

    /// <summary>컨트롤러가 체크포인트 상태 변화를 밀어 준다. 활성은 씬에서 항상 하나뿐이다.</summary>
    public void SetFlagState(FlagState next)
    {
        if (flagRenderer == null || flagState == next) return;

        bool wasHidden = flagState == FlagState.Hidden;
        flagState = next;

        if (next == FlagState.Hidden)
        {
            flagRenderer.enabled = false;
            return;
        }

        // 공유 머티리얼 에셋을 건드리지 않게 MaterialPropertyBlock으로 색만 덮어쓴다(무지개
        // 다리와 같은 방식). 여기서는 블렌드 모드를 바꿀 일이 없어 머티리얼 인스턴스화가 불필요하다.
        flagRenderer.GetPropertyBlock(flagBlock);
        flagBlock.SetColor(ColorId, next == FlagState.Active ? activeColor : visitedColor);
        flagRenderer.SetPropertyBlock(flagBlock);

        flagRenderer.enabled = true;
        if (wasHidden && raiseSeconds > 0f && isActiveAndEnabled)
            StartCoroutine(RaiseFlag());
        else
            flagRenderer.transform.localPosition = flagRaisedLocalPos;
    }

    private IEnumerator RaiseFlag()
    {
        Transform t = flagRenderer.transform;
        Vector3 from = new Vector3(flagRaisedLocalPos.x, raiseFromLocalY, flagRaisedLocalPos.z);

        float elapsed = 0f;
        while (elapsed < raiseSeconds)
        {
            elapsed += Time.deltaTime;
            t.localPosition = Vector3.Lerp(from, flagRaisedLocalPos, elapsed / raiseSeconds);
            yield return null;
        }
        t.localPosition = flagRaisedLocalPos;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // 플레이어는 콜라이더가 둘(Player_Mesh/Player_Collider)이라 진입마다 두 번 불린다.
        // 같은 구역의 중복 저장은 컨트롤러가 걸러낸다(레이도 그때 한 번만 쏜다).
        RespawnController.SetCheckpoint(this);
    }

    /// <summary>낙하 리스폰이 시작될 지점 — 구역 상단면 중앙.</summary>
    public Vector3 DropSpawnPoint
    {
        get
        {
            Bounds b = ZoneBounds;
            return new Vector3(b.center.x, b.max.y - dropSpawnInset, b.center.z);
        }
    }

    /// <summary>페이드 리스폰이 설 실제 바닥 지점(중앙 X/Z + 레이가 찾은 바닥 Y).
    /// 체크포인트가 저장되는 순간에만 호출된다 — 매 프레임 쏘는 레이가 아니다.</summary>
    public Vector3 FindGroundPoint()
    {
        Bounds b = ZoneBounds;
        if (TryFindGroundY(b, out float y)) return new Vector3(b.center.x, y, b.center.z);

        Debug.LogWarning($"[RespawnZone] '{name}' 중앙 아래에서 바닥을 못 찾아 볼륨 밑면을 대신 쓴다 " +
                         "— 구역이 지형 위에 놓였는지, groundRayExtra가 충분한지 확인해라.", this);
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    private bool TryFindGroundY(Bounds b, out float groundY)
    {
        // 구역 안에 서 있는 플레이어를 바닥으로 착각하지 않게 계층으로 거른다. 레이어로 거르면
        // 플레이어와 지형이 둘 다 Default인 이 저장소의 씬에서 지형까지 통째로 걸러진다
        // (ThreadPinPlacer가 같은 함정을 겪고 계층 필터로 바꿨다).
        RaycastHit[] hits = Physics.RaycastAll(b.center, Vector3.down, b.extents.y + groundRayExtra,
                                               ~0, QueryTriggerInteraction.Ignore);
        groundY = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerMover>() != null) continue;
            // 자기 자식(막대·깃발)은 바닥이 아니다. 메뉴가 만드는 막대는 콜라이더가 없지만,
            // 누가 나중에 붙이면 레이가 막대 꼭대기를 바닥으로 잡아 플레이어가 공중에 선다.
            if (hit.collider.transform.IsChildOf(transform)) continue;
            // 낙석(FallingRock)은 트리거가 아닌 평범한 dynamic Rigidbody라 위 필터에 안 걸린다.
            // 체크포인트 저장 순간 낙석이 마침 구역 중앙 축을 지나가면 그 표면을 바닥으로 잡아,
            // 페이드 리스폰이 허공(낙석이 지나간 자리)에 서게 된다 — 낙석도 바닥 후보에서 뺀다.
            if (hit.collider.GetComponentInParent<FallingRock>() != null) continue;
            if (hit.point.y > groundY) groundY = hit.point.y;
        }
        return !float.IsNegativeInfinity(groundY);
    }

    // 회전 배치한 구역에서는 bounds가 월드 AABB라 실제 볼륨보다 커진다(SlowZone 기즈모와 같은 한계).
    // 체크포인트 구역은 축 정렬로 두는 것을 전제로 한다.
    private Bounds ZoneBounds
    {
        get
        {
            Collider col = zoneCollider != null ? zoneCollider : GetComponent<Collider>();
            return col.bounds;
        }
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.12f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        // 선택 중에만 실제 스폰 두 곳을 그린다. 여기서는 경고를 남기지 않는다(에디터 프레임마다
        // 콘솔이 도배된다) — 바닥을 못 찾으면 마커를 빨갛게 칠해 배치 문제를 눈으로 알린다.
        Bounds b = col.bounds;
        bool found = TryFindGroundY(b, out float groundY);
        Vector3 drop = DropSpawnPoint;
        Vector3 ground = new Vector3(b.center.x, found ? groundY : b.min.y, b.center.z);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(drop, 0.35f);
        Gizmos.DrawLine(drop, ground);
        Gizmos.color = found ? Color.yellow : Color.red;
        Gizmos.DrawWireSphere(ground, 0.25f);
    }
}
