using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class ToyWorldInstallSocket : MonoBehaviour
{
    public ToyWorldRepairItemType itemType;
    public ToyWorldLevelDirector director;
    public Renderer socketRenderer;
    public Color waitingColor = new Color(0.12f, 0.12f, 0.18f);
    public Color installedColor = new Color(0.2f, 1f, 0.35f);

    private MaterialPropertyBlock colorBlock;
    private readonly Dictionary<PlayerMover, int> playerContacts = new Dictionary<PlayerMover, int>();
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        colorBlock = new MaterialPropertyBlock();
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogError("[ToyWorld] Install socket collider must be a trigger.", this);
    }

    private void Start() => Refresh();

    private void OnTriggerEnter(Collider other)
    {
        PlayerMover player = other.GetComponentInParent<PlayerMover>();
        if (player == null) return;

        bool wasEmpty = playerContacts.Count == 0;
        playerContacts.TryGetValue(player, out int contactCount);
        playerContacts[player] = contactCount + 1;

        if (!wasEmpty) return;
        ResolveDirector();
        if (director != null) director.SetInstallPadPressed(itemType, true);
        Refresh();
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMover player = other.GetComponentInParent<PlayerMover>();
        if (player == null || !playerContacts.TryGetValue(player, out int contactCount)) return;

        if (contactCount > 1)
            playerContacts[player] = contactCount - 1;
        else
            playerContacts.Remove(player);

        if (playerContacts.Count != 0) return;
        ReleasePad();
    }

    private void OnDisable()
    {
        if (playerContacts.Count == 0) return;
        playerContacts.Clear();
        ReleasePad();
    }

    private void ReleasePad()
    {
        ResolveDirector();
        if (director != null) director.SetInstallPadPressed(itemType, false);
        Refresh();
    }

    public void Refresh()
    {
        if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
        ResolveDirector();
        bool installed = director != null && director.IsInstalled(itemType);
        if (socketRenderer == null) return;
        socketRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorId, installed ? installedColor : waitingColor);
        socketRenderer.SetPropertyBlock(colorBlock);
    }

    private void ResolveDirector()
    {
        if (director == null) director = ToyWorldLevelDirector.Instance;
    }
}
