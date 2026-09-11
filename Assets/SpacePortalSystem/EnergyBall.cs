using UnityEngine;

/// <summary>포탈 색 — 주황(입구) / 파랑(출구). PRD §1 원문 그대로.</summary>
public enum EnergyColor { Orange, Blue }

/// <summary>
/// 레벨에 정확히 두 개(주황 1 · 파랑 1) 배치되는 고정 개체. 스포너가 아니다 — "3번째 볼"이 새로
/// 생기는 경우는 없다(PRD §3.1). Ground(바닥에 떠 있음) / Carried(플레이어가 소지, 볼 자체는
/// 숨김) / Installed(포탈로 변환돼 필드에 존재, 볼도 숨김) 세 상태를 스스로 갖는다.
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

    public enum BallState { Ground, Carried, Installed }

    public BallState State { get; private set; } = BallState.Ground;
    public PlayerEnergyReceiver Owner { get; private set; }

    private Vector3 groundPosition;
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

    private void SetVisible(bool visible)
    {
        foreach (Renderer r in renderers) if (r != null) r.enabled = visible;
        foreach (Collider c in colliders) if (c != null) c.enabled = visible;
    }
}
