// ③ ShapeGimmickSetup (에디터 자동 씬 생성 헬퍼)은 별도 파일로 분리되었습니다.
//    위치: Editor/ShapeGimmickSetup.cs
//    반드시 Unity 프로젝트의 "Editor" 폴더 안에 넣어야 런타임 빌드 오류가 발생하지 않습니다.

using UnityEngine;

/// <summary>
/// 런타임에서 사용 가능한 플레이어 초기 설정 컴포넌트.
/// 씬 시작 시 플레이어 태그와 Rigidbody가 올바른지 자동 확인합니다.
/// </summary>
public class PlayerSetup : MonoBehaviour
{
    void Awake()
    {
        // 태그 확인
        if (!gameObject.CompareTag("Player"))
            Debug.LogWarning($"[PlayerSetup] '{gameObject.name}'의 태그가 'Player'가 아닙니다. ScalePad가 인식하지 못할 수 있습니다.");

        // Rigidbody 확인
        if (GetComponent<Rigidbody>() == null)
            Debug.LogWarning($"[PlayerSetup] '{gameObject.name}'에 Rigidbody가 없습니다. OnTriggerEnter가 동작하려면 플레이어 또는 패드 중 하나에 Rigidbody가 있어야 합니다.");

        // ⑦ PlayerShapeController가 없으면 경고 후 자동으로 추가
        //    ShapeGimmickSetup에서 PlayerSetup이 부착되지 않는 경우도 있으므로,
        //    수동으로 PlayerSetup만 붙였을 때를 위한 안전망
        if (GetComponent<PlayerShapeController>() == null)
        {
            Debug.LogWarning($"[PlayerSetup] '{gameObject.name}'에 PlayerShapeController가 없습니다. 자동으로 추가합니다.");
            gameObject.AddComponent<PlayerShapeController>();
        }
    }
}