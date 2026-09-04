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
        if (other.GetComponentInParent<PlayerMover>() == null) return;
        ResolveDirector();
        if (director != null && director.TryInstallItem(itemType)) Refresh();
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
