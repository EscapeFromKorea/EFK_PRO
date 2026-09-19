using UnityEngine;

/// <summary>포탈 색 — 주황(입구) / 파랑(출구). PRD §1 원문 그대로.</summary>
public enum EnergyColor { Orange, Blue }

/// <summary>
/// 레벨에 정확히 두 개(주황 1 · 파랑 1) 배치되는 고정 개체. 스포너가 아니다 — "3번째 볼"이 새로
/// 생기는 경우는 없다(PRD §3.1). Ground(바닥에 떠 있음) / Carried(플레이어가 소지, 볼 자체는
/// 숨김) / Installed(포탈로 변환돼 필드에 존재, 볼도 숨김) 세 상태를 스스로 갖는다.
///
/// Installed는 더 이상 영구 상태가 아니다(2026-09-15) — 설치(=사용) 시 <see cref="Respawn"/>이
/// 함께 걸려, `respawnDelaySeconds`(기본 5초) 뒤 자동으로 원래 배치 자리에 Ground로 되돌아간다.
/// 포탈 자체(<see cref="SpacePortal"/>)는 별개 오브젝트라 그대로 남는다 — 이 재생성은 "포탈을
/// 다시 옮기고 싶을 때 R 2초 회수 없이도 새 볼을 집을 수 있게" 하는 것뿐, 3번째 볼이 생기는 게
/// 아니다(그 색의 볼은 항상 하나뿐이고, 회수(R 2초)로 다시 들면 대기 중이던 재생성은 취소된다).
///
/// 발신자-수신자 분리 패턴을 따른다: F 획득은 AccelPad와 같은 "트리거 + 근접 판정"으로 직접
/// 감지해 <see cref="PlayerEnergyReceiver"/>를 찾아 TryPickup(this)를 호출한다. 리시버가 없으면
/// Portal.cs의 "리시버는 포탈이 즉석에서 AddComponent" 관례처럼 그 자리에서 붙인다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnergyBall : MonoBehaviour
{
    [Tooltip("이 볼의 색. 주황=입구, 파랑=출구(PRD §1 원문).")]
    public EnergyColor color = EnergyColor.Orange;

    [Header("바닥 상태 연출")]
    public float bobHeight = 0.2f;
    public float bobSpeed = 2f;
    public float spinSpeed = 60f;

    [Tooltip("다른 색 볼을 주워 이 볼이 소지 해제될 때, 원래 자리에 다시 나타나기까지 걸리는 시간(초).")]
    public float respawnDelaySeconds = 5f;

    public enum BallState { Ground, Carried, Installed }

    public BallState State { get; private set; } = BallState.Ground;
    public PlayerEnergyReceiver Owner { get; private set; }

    private Vector3 groundPosition;
    private Vector3 originPosition; // 레벨에 최초 배치된 자리. groundPosition은 드랍마다 덮어써지므로 별도 보존.
    private Renderer[] renderers;
    private Collider[] colliders;

    // 근접 후보. Portal.cs의 lastOverlapTime과 같은 "도장 찍기" 관용구 — OnTriggerStay가 매 물리
    // 스텝 갱신하고, Update의 F 판정은 그 도장이 최근(fixedDeltaTime*2.5) 것일 때만 유효로 본다.
    private PlayerEnergyReceiver nearbyReceiver;
    private float nearbySeenTime = -1f;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        groundPosition = transform.position;
        originPosition = transform.position;
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
    }

    private void Update()
    {
        if (State != BallState.Ground) return;

        float y = groundPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(groundPosition.x, y, groundPosition.z);
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        bool fresh = nearbyReceiver != null && Time.time - nearbySeenTime <= Time.fixedDeltaTime * 2.5f;
        if (fresh && nearbyReceiver.IsControlledByLocalPlayer && Input.GetKeyDown(KeyCode.F))
            nearbyReceiver.TryPickup(this);
    }

    private void OnTriggerStay(Collider other)
    {
        if (State != BallState.Ground) return;

        PlayerShapeIdentity identity = other.GetComponentInParent<PlayerShapeIdentity>();
        if (identity == null) return;

        PlayerEnergyReceiver receiver = identity.GetComponent<PlayerEnergyReceiver>();
        if (receiver == null) receiver = identity.gameObject.AddComponent<PlayerEnergyReceiver>();

        nearbyReceiver = receiver;
        nearbySeenTime = Time.time;
    }

    public void SetCarried(PlayerEnergyReceiver owner)
    {
        // 설치 후 5초 재생성 대기 중(Respawn 코루틴 진행 중)에 회수(R 2초)로 다시 들면, 대기하던
        // 코루틴이 나중에 끝나면서 손에 든 볼을 뜬금없이 원위치에 떨어뜨리는 경합을 막는다.
        StopAllCoroutines();
        State = BallState.Carried;
        Owner = owner;
        SetVisible(false);
    }

    public void SetInstalled(PlayerEnergyReceiver owner)
    {
        State = BallState.Installed;
        Owner = owner;
        SetVisible(false);
    }

    /// <summary>드랍(R 짧게) 또는 회수(R 2초, 포탈이 다시 볼로 돌아올 때)로 Ground 상태로 되돌린다.
    /// 회수는 소지자 몸 근처(현재 위치)로, 드랍은 드랍 시점 위치로 되돌아간다 — 호출자가 위치를 정한다.</summary>
    public void SetGround(Vector3 position)
    {
        State = BallState.Ground;
        Owner = null;
        groundPosition = position;
        transform.position = position;
        SetVisible(true);
    }

    /// <summary>포탈로 설치(=사용)되어 <see cref="SetInstalled"/>로 넘어갈 때 함께 부른다 — 드랍(R)처럼
    /// 플레이어 발밑이 아니라 레벨에 최초 배치된 자리로, 즉시가 아니라 <see cref="respawnDelaySeconds"/>
    /// 뒤에 되돌아간다(그 사이 회수(R 2초)로 다시 들면 <see cref="SetCarried"/>가 이 대기를 취소한다).</summary>
    public void Respawn()
    {
        StopAllCoroutines();
        StartCoroutine(RespawnAfterDelay());
    }

    private System.Collections.IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelaySeconds);
        SetGround(originPosition);
    }

    private void SetVisible(bool visible)
    {
        foreach (Renderer r in renderers) if (r != null) r.enabled = visible;
        foreach (Collider c in colliders) if (c != null) c.enabled = visible;
    }
}
