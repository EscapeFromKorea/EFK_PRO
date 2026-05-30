using UnityEngine;

public class doorPhysics : MonoBehaviour
{
    public bool isBlocked = false;
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            isBlocked = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            isBlocked = false;
    }
}

//doorPhysics에서는 door 오브젝트가 player 오브젝트와 충돌했는가 아닌가의 여부만 확인, 세부적인 움직임은 padTrigger.cs에서 동작