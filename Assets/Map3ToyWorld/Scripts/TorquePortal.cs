using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class TorquePortal : MonoBehaviour
{
    public TorquePortalMode mode = TorquePortalMode.Enable;
    public Renderer statusRenderer;
    public Color enableColor = new Color(1f, 0.45f, 0.08f);
    public Color disableColor = new Color(0.2f, 0.65f, 1f);

    private readonly HashSet<TorqueStateController> processed = new HashSet<TorqueStateController>();
    private MaterialPropertyBlock colorBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogError("[ToyWorld] Torque portal collider must be a trigger.", this);
        colorBlock = new MaterialPropertyBlock();
        RefreshColor();
    }

    private void OnTriggerEnter(Collider other)
    {
        TorqueStateController state = other.GetComponentInParent<TorqueStateController>();
        if (state == null || !processed.Add(state)) return;

        switch (mode)
        {
            case TorquePortalMode.Enable: state.SetTorqueEnabled(true); break;
            case TorquePortalMode.Disable: state.SetTorqueEnabled(false); break;
            default: state.ToggleTorque(); break;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        TorqueStateController state = other.GetComponentInParent<TorqueStateController>();
        if (state != null) processed.Remove(state);
    }

    private void RefreshColor()
    {
        if (statusRenderer == null) return;
        statusRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorId, mode == TorquePortalMode.Disable ? disableColor : enableColor);
        statusRenderer.SetPropertyBlock(colorBlock);
    }
}
