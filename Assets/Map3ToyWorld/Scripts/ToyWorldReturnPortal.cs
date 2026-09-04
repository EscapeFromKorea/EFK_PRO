using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyWorldReturnPortal : MonoBehaviour
{
    public Transform destination;
    public Renderer statusRenderer;
    public Color lockedColor = new Color(0.2f, 0.08f, 0.08f);
    public Color unlockedColor = new Color(0.1f, 0.9f, 0.45f);

    private readonly HashSet<PlayerMover> inside = new HashSet<PlayerMover>();
    private bool unlocked;
    private MaterialPropertyBlock colorBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public bool IsUnlocked => unlocked;

    private void Awake()
    {
        if (!GetComponent<Collider>().isTrigger)
            Debug.LogError("[ToyWorld] Return portal collider must be a trigger.", this);
        colorBlock = new MaterialPropertyBlock();
        RefreshColor();
    }

    public void Unlock()
    {
        unlocked = true;
        RefreshColor();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!unlocked || destination == null) return;
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover == null || !inside.Add(mover)) return;

        Rigidbody body = mover.GetComponent<Rigidbody>();
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = destination.position + Vector3.up * 0.6f;
        body.rotation = destination.rotation;
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover mover = other.GetComponentInParent<PlayerMover>();
        if (mover != null) inside.Remove(mover);
    }

    private void RefreshColor()
    {
        if (statusRenderer == null || colorBlock == null) return;
        statusRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorId, unlocked ? unlockedColor : lockedColor);
        statusRenderer.SetPropertyBlock(colorBlock);
    }
}
