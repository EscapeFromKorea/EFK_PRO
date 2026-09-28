using UnityEngine;

public sealed class HubProgressDisplay : MonoBehaviour
{
    [Tooltip("WindUpSpring, PowerGear, MelodyCylinder order.")]
    public Renderer[] itemSlots = new Renderer[3];
    [Tooltip("Branch entrance beacons in the same order.")]
    public Renderer[] branchBeacons = new Renderer[3];
    public Color incompleteColor = new Color(0.12f, 0.12f, 0.16f);
    public Color completeColor = new Color(0.2f, 1f, 0.4f);

    private MaterialPropertyBlock block;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake() => block = new MaterialPropertyBlock();

    public void Refresh(ToyWorldLevelDirector director)
    {
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < 3; i++)
        {
            bool complete = director != null && director.HasItem((ToyWorldRepairItemType)i);
            SetColor(itemSlots, i, complete ? completeColor : incompleteColor);
            SetColor(branchBeacons, i, complete ? completeColor : incompleteColor);
        }
    }

    private void SetColor(Renderer[] renderers, int index, Color color)
    {
        if (renderers == null || index >= renderers.Length || renderers[index] == null) return;
        Renderer target = renderers[index];
        target.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        target.SetPropertyBlock(block);
    }
}
