using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public sealed class ToyWorldGate : MonoBehaviour
{
    public Vector3 openOffset = new Vector3(0f, 6f, 0f);
    public float moveSpeed = 3f;
    public Renderer statusRenderer;
    public Color lockedColor = new Color(0.35f, 0.08f, 0.08f);
    public Color openColor = new Color(0.15f, 0.9f, 0.35f);

    private Rigidbody body;
    private Vector3 closedPosition;
    private bool initialized;
    private bool isOpen;
    private MaterialPropertyBlock colorBlock;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public bool IsOpen => isOpen;

    private void Awake() => EnsureInitialized();

    private void FixedUpdate()
    {
        EnsureInitialized();
        Vector3 target = closedPosition + (isOpen ? openOffset : Vector3.zero);
        body.MovePosition(Vector3.MoveTowards(body.position, target, moveSpeed * Time.fixedDeltaTime));
    }

    public void SetOpen(bool open)
    {
        EnsureInitialized();
        isOpen = open;
        UpdateColor();
    }

    public void SetOpenImmediately(bool open)
    {
        SetOpen(open);
        Vector3 target = closedPosition + (open ? openOffset : Vector3.zero);
        body.position = target;
        transform.position = target;
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        closedPosition = body.position;
        colorBlock = new MaterialPropertyBlock();
        initialized = true;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (statusRenderer == null || colorBlock == null) return;
        statusRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorId, isOpen ? openColor : lockedColor);
        statusRenderer.SetPropertyBlock(colorBlock);
    }
}
