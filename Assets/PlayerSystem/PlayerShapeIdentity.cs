using UnityEngine;

/// <summary>
/// 플레이어 Root에 부착. 자신의 PlayerShapeStats 에셋을 참조해 Start에서 이속/점프높이/질량/물리
/// 머티리얼(마찰·반발)을 적용하고, 도형 종류(Kind)를 외부에 노출한다(요구사항 #2·#6·#7).
///
/// 기믹은 닿은 Collider에서 GetComponentInParent&lt;PlayerShapeIdentity&gt;()로 이 컴포넌트를 찾아
/// Kind로 누가 닿았는지 판별한다(CompareTag 하드코딩 대신). 스탯이 에셋에 있으므로 값 조정에
/// 재컴파일이 필요 없다.
/// </summary>
public class PlayerShapeIdentity : MonoBehaviour
{
    [Tooltip("이 플레이어의 도형 스탯 에셋. 이속/점프높이/질량/마찰/반발을 여기서 읽어 적용한다.")]
    public PlayerShapeStats stats;

    [Tooltip("물리 머티리얼(마찰/반발)을 적용할 솔리드 콜라이더(Player_Collider).")]
    public Collider solidCollider;

    /// <summary>기믹이 조회하는 도형 종류. stats가 없으면 Sphere로 간주.</summary>
    public PlayerShapeStats.ShapeKind Kind =>
        stats != null ? stats.kind : PlayerShapeStats.ShapeKind.Sphere;

    void Start()
    {
        if (stats == null)
        {
            Debug.LogWarning($"[PlayerShapeIdentity] '{name}'에 PlayerShapeStats가 지정되지 않아 스탯을 적용하지 못했습니다.");
            return;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.mass = stats.mass;

        PlayerMover mover = GetComponent<PlayerMover>();
        if (mover != null) mover.moveSpeed = stats.moveSpeed;

        PlayerJump jump = GetComponent<PlayerJump>();
        if (jump != null) jump.jumpHeight = stats.jumpHeight;

        if (solidCollider != null)
        {
            // 이 오브젝트 전용 인스턴스 재질(공유 에셋/다른 콜라이더 오염 방지).
            solidCollider.material = new PhysicMaterial($"{name}_Physic")
            {
                staticFriction = stats.friction,
                dynamicFriction = stats.friction,
                frictionCombine = PhysicMaterialCombine.Average,
                bounciness = stats.bounciness,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
        }
    }
}
