using UnityEngine;

/// <summary>
/// 동기화 오브젝트 데모/QA 전용 도구 — 중력을 키 하나로 켜고 끈다. 명세서엔 중력 요구사항이 없고
/// <see cref="SyncObject"/> 본체는 중력을 전혀 건드리지 않는다(순수 테스트 편의).
///
/// 기본은 꺼진 채 시작한다 — 어디에 옮겨놔도 그 자리에 가만히 있어서 범위·정밀 위치 테스트가
/// 쉽다. 키를 누르면 그 순간만 중력을 켜 떨어지는 모습을 볼 수 있고, 다시 누르면 꺼지며 그
/// 자리에서 즉시 멈춘다(잔여 속도를 0으로 비움 — 밀리던 채로 안 멈추면 정밀 배치가 안 된다).
///
/// Create Sync Pair 메뉴가 Leader에만 붙인다(Follower는 kinematic이라 중력 자체가 무관).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SyncDemoGravityToggle : MonoBehaviour
{
    [Tooltip("누를 때마다 이 오브젝트의 중력을 켜짐/꺼짐 토글한다.")]
    public KeyCode toggleKey = KeyCode.Z;

    private Rigidbody body;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(toggleKey)) return;

        body.useGravity = !body.useGravity;
        if (!body.useGravity)
        {
            // 꺼지는 순간 그 자리에 딱 멈춘다 — 밀리던 잔여 속도가 남아 있으면 정밀 배치가 안 된다.
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        Debug.Log($"[SyncDemo] '{name}' 중력 {(body.useGravity ? "켬" : "끔")} ('{toggleKey}' 키로 토글).");
    }
}
