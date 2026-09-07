using UnityEngine;

/// <summary>
/// 레일카 탑승/하차 — 2026-09-05 확정: 걸어서 올라타는 방식(트리거) 대신 `CatapultBucket`의 C키
/// 탑승 패턴을 재사용한다(거리 게이트 + 즉시 텔레포트 + 부모화 + isKinematic). 카트가 곡선 레일을
/// 따라 위치뿐 아니라 방향(회전)도 계속 바뀌므로, 이전의 "velocity만 얹어 따라가게 한다"는
/// 방식(직선에서만 잘 맞음)보다 완전 종속(부모화)이 곡선에서도 정확하다 — 회전까지 자동으로
/// 따라간다.
///
/// [왜 카트 루트에 직접 부모화해도 되는가] `CatapultBucket`은 비균일 스케일을 가진
/// `Catapult_BucketInner`가 아니라 순수 회전(스케일 (1,1,1)) `armPivot`에 부모화한다 — 비균일
/// 스케일에 회전이 겹치면 `SetParent(..., true)`의 스케일 역산이 전단(shear)을 만들어 탑승자가
/// 찌그러지기 때문이다(`CatapultSystem/CLAUDE.md` "10차 개편" 참고). 레일카는 이 함정을 설계
/// 단계에서 피한다 — 루트 Transform의 `localScale`을 항상 `(1,1,1)`로 고정하고, 외형(U자형
/// 광산차 몸체)은 전부 자식 오브젝트의 로컬 스케일로만 표현한다(`RailCartMenuItem` 참고). 그래서
/// 탑승자를 카트 루트에 직접 부모화해도 전단이 생기지 않는다.
/// </summary>
public class RailCartRider : MonoBehaviour
{
    [Tooltip("탑승/하차 대상 카트. 비워두면 이 컴포넌트가 붙은 오브젝트의 RailCart를 찾는다.")]
    public RailCart cart;
    [Tooltip("이 거리 안에서 C를 누르면 탑승(또는 하차)한다.")]
    public float boardRange = 2.5f;
    [Tooltip("탑승 시 플레이어를 놓을 좌석 지점(카트 자식 Transform). 비워두면 이 오브젝트의 " +
             "위치를 쓴다.")]
    public Transform seat;
    [Tooltip("하차 시 플레이어를 내려놓을 지점 — 카트 로컬 좌표계 기준 오프셋(옆으로 비켜서 내림).")]
    public Vector3 exitLocalOffset = new Vector3(0.9f, 0f, 0f);

    private Rigidbody occupantBody;
    private PlayerMover occupantMover;
    private Transform occupantOriginalParent;

    void Awake()
    {
        if (cart == null) cart = GetComponent<RailCart>();
    }

    void Update()
    {
        // 탑승 중엔 다른 기믹(DreamThreadSystem 등)이 같은 프레임에 ExternallyDriven을 꺼도
        // 자기 몫만큼은 자가 치유한다 — RailCartRider의 이전 버전이 겪은 것과 같은 소유권 경합
        // 방지(`RailCartRider.cs` 이력 참고).
        if (occupantMover != null) occupantMover.ExternallyDriven = true;

        if (!Input.GetKeyDown(KeyCode.C)) return;

        if (occupantBody != null)
        {
            Unboard();
            return;
        }

        TryBoard();
    }

    private void TryBoard()
    {
        PlayerMover mover = FindControlledPlayer();
        if (mover == null) return;
        Rigidbody body = mover.GetComponent<Rigidbody>();
        if (body == null) return;

        float myDistance = Vector3.Distance(body.position, transform.position);
        if (myDistance > boardRange) return;
        if (!IsNearestRider(body.position, myDistance)) return;

        Board(mover, body);
    }

    private static PlayerMover FindControlledPlayer()
    {
        foreach (PlayerMover m in Object.FindObjectsOfType<PlayerMover>())
            if (m.IsControlled) return m;
        return null;
    }

    // 여러 레일카의 탑승 범위가 겹치는 위치에서 C를 누르면 가장 가까운 카트에만 탑승한다 —
    // CatapultBucket.IsNearestBucket과 같은 이유(C는 전역 키).
    private bool IsNearestRider(Vector3 playerPosition, float myDistance)
    {
        foreach (RailCartRider other in Object.FindObjectsOfType<RailCartRider>())
        {
            if (other == this) continue;
            float otherDistance = Vector3.Distance(playerPosition, other.transform.position);
            if (otherDistance > other.boardRange) continue;
            if (otherDistance < myDistance) return false;
            if (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()) return false;
        }
        return true;
    }

    private void Board(PlayerMover mover, Rigidbody body)
    {
        occupantBody = body;
        occupantMover = mover;
        occupantOriginalParent = body.transform.parent;

        body.isKinematic = true;
        mover.ExternallyDriven = true;

        Transform mountParent = cart != null ? cart.transform : transform;
        body.transform.SetParent(mountParent, true);
        body.transform.position = seat != null ? seat.position : transform.position;
        body.transform.localRotation = Quaternion.identity;
    }

    private void Unboard()
    {
        Rigidbody body = occupantBody;
        PlayerMover mover = occupantMover;

        Transform mountParent = cart != null ? cart.transform : transform;
        Vector3 exitPos = mountParent.TransformPoint(exitLocalOffset);

        body.transform.SetParent(occupantOriginalParent, true);
        body.transform.position = exitPos;
        body.isKinematic = false;
        mover.ExternallyDriven = false;

        occupantBody = null;
        occupantMover = null;
        occupantOriginalParent = null;
    }
}
