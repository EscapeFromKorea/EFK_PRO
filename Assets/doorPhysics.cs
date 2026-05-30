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