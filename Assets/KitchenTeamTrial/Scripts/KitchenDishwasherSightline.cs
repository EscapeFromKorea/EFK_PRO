using UnityEngine;

// Only the shell panels crossing the actual game camera's view of its target are cut away.
[DefaultExecutionOrder(2100)]
public class KitchenDishwasherSightline : MonoBehaviour
{
    Renderer[] parts;
    Bounds[] paddedBounds;
    PlayerFollowCamera follow;

    void Awake()
    {
        parts = GetComponentsInChildren<MeshRenderer>();
        paddedBounds = new Bounds[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            paddedBounds[i] = parts[i].bounds;
            paddedBounds[i].Expand(1.8f);
        }
    }

    void LateUpdate()
    {
        if (!follow || !follow.isActiveAndEnabled)
        {
            var camera = Camera.main;
            follow = camera ? camera.GetComponent<PlayerFollowCamera>() : null;
        }
        if (!follow || !follow.target)
        {
            Restore();
            return;
        }
        Vector3 target = follow.target.position + Vector3.up * follow.lookHeightOffset;
        Vector3 offset = target - follow.transform.position;
        bool near = Mathf.Abs(target.x - transform.position.x) < 23f &&
            target.z > transform.position.z - 16f && target.z < transform.position.z + 128f;
        if (!near)
        {
            Restore();
            return;
        }
        var ray = new Ray(follow.transform.position, offset.normalized);
        float distance = offset.magnitude;
        for (int i = 0; i < parts.Length; i++)
        {
            bool blocked = paddedBounds[i].IntersectRay(ray, out float hit) && hit <= distance;
            if (parts[i].forceRenderingOff != blocked) parts[i].forceRenderingOff = blocked;
        }
        changed = true;
    }

    bool changed;
    void Restore()
    {
        if (!changed) return;
        foreach (Renderer part in parts)
            if (part) part.forceRenderingOff = false;
        changed = false;
    }

    void OnDisable() { Restore(); }
}
