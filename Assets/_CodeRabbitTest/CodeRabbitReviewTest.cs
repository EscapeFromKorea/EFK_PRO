using UnityEngine;

// TEMPORARY: CodeRabbit 설치 확인용 테스트 파일. 리뷰 확인 후 병합하지 않고 PR을 닫는다.
public class CodeRabbitReviewTest : MonoBehaviour
{
    private Rigidbody rb;

    void Update()
    {
        // 의도적인 문제: 매 프레임 GetComponent 호출 (성능 문제)
        rb = GetComponent<Rigidbody>();
        rb.velocity = Vector3.zero;
    }
}
