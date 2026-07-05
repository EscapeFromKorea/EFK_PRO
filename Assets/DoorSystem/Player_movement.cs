using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMover : MonoBehaviour   //플레이어 움직임 관련(기믹 테스트를 위해 임시로 적용)
{ 
    public float moveSpeed = 5f;   //플레이어의 속도 조절 (5xn 단위)
    private Rigidbody rb;    //rigidbody component 추가해야함

    void Start()
    {
        rb = GetComponent<Rigidbody>();   //rigidbody component에 있는 요소 가져오는 것
        rb.freezeRotation = true;          //충돌시 회전 없게
    }

    void FixedUpdate()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(h, 0f, v) * moveSpeed;      //움직임 설정
        rb.velocity = new Vector3(move.x, rb.velocity.y, move.z);  //속도 변경
    }
}