using System;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class Map1CoopSwitch : MonoBehaviour, IMpSynced
{
    public PlayerShapeStats.ShapeKind kind;
    public Map1CoopSwitch[] requires = Array.Empty<Map1CoopSwitch>();
    public GameObject[] reveal = Array.Empty<GameObject>();
    public GameObject[] conceal = Array.Empty<GameObject>();
    public Renderer indicator;
    public Material readyMaterial;
    public bool IsSet { get; private set; }
    private uint syncId;

    private void Awake() => syncId = MpSync.Register(this);
    private void OnDestroy() => MpSync.Unregister(syncId, this);

    private void OnTriggerEnter(Collider other) => TrySet(other);
    private void OnTriggerStay(Collider other) => TrySet(other);

    private void TrySet(Collider other)
    {
        if (IsSet || MpSync.IsClient || other.isTrigger || !AllSet(requires)) return;
        var shape = other.GetComponentInParent<PlayerShapeIdentity>();
        if (shape == null || shape.Kind != kind) return;
        Set();
        MpSync.Publish(syncId, 0, 1);
    }

    public void OnMpValue(int channel, float value)
    {
        if (channel == 0 && value > 0.5f) Set();
    }

    private void Set()
    {
        if (IsSet) return;
        IsSet = true;
        if (indicator != null && readyMaterial != null) indicator.sharedMaterial = readyMaterial;
        foreach (GameObject target in conceal)
            if (target != null) target.SetActive(false);
        foreach (GameObject target in reveal)
            if (target != null) target.SetActive(true);
    }

    internal static bool AllSet(Map1CoopSwitch[] switches)
    {
        foreach (Map1CoopSwitch trigger in switches)
            if (trigger == null || !trigger.IsSet) return false;
        return true;
    }
}
