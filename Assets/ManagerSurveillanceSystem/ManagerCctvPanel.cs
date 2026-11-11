using UnityEngine;

/// <summary>
/// 감시 화면 — RoleSlot(roleId "Monitor")에 붙는 패널. 씬의 CCTV 카메라가 RenderTexture로 그린 영상을 OnGUI에
/// 띄우고 ←/→로 카메라를 바꾼다(PRD ManagerSurveillance §3, 확정 2026-09-26).
///
/// [정보 격리] 화면을 연 감시 담당에게만 그린다(RoleSlot.CanShowTo). 보는 동안 이동은 RoleAssignmentManager가
/// 잠근다(단독 사물 공통 규칙) — 감시 담당은 콘솔 앞에 서서 말로 전달한다.
/// [렌더 비용] 카메라는 패널이 열려 있을 때 선택된 한 대만 켠다. 닫혀 있으면 전부 enabled = false.
/// </summary>
public class ManagerCctvPanel : MonoBehaviour
{
    public RoleSlot slot;

    [Tooltip("CCTV 카메라들. 화면으로 직접 그리지 않고 이 패널의 RenderTexture에만 그린다.")]
    public Camera[] cameras = new Camera[0];

    public int textureWidth = 640;
    public int textureHeight = 360;

    public KeyCode prevKey = KeyCode.LeftArrow;
    public KeyCode nextKey = KeyCode.RightArrow;
    [Tooltip("보조 전환 키. 패널을 보는 동안 이동이 잠겨 A/D가 비어 있다.")]
    public KeyCode altPrevKey = KeyCode.A;
    public KeyCode altNextKey = KeyCode.D;

    public int CameraIndex { get; private set; }

    private RenderTexture rt;
    private bool bound;

    private void Awake()
    {
        rt = new RenderTexture(textureWidth, textureHeight, 16);
        foreach (Camera c in cameras)
            if (c != null) c.targetTexture = rt;
        Apply();
    }

    private void OnEnable()
    {
        if (bound || slot == null) return;
        bound = true;
        slot.PanelOpened += OnPanelChanged;
        slot.PanelClosed += OnPanelChanged;
        Apply();
    }

    private void OnDisable()
    {
        if (bound && slot != null)
        {
            slot.PanelOpened -= OnPanelChanged;
            slot.PanelClosed -= OnPanelChanged;
        }
        bound = false;
        foreach (Camera c in cameras)
            if (c != null) c.enabled = false;
    }

    private void OnDestroy()
    {
        if (rt != null) rt.Release();
    }

    private void OnPanelChanged(PlayerMover p)
    {
        Apply();
        LokiTelemetry.Event("ch8_cctv_panel", $"open={slot.IsPanelOpen} by={(p != null ? p.name : "null")} cam={CameraIndex}");
    }

    public void Step(int delta)
    {
        if (cameras.Length == 0) return;
        CameraIndex = (CameraIndex + delta + cameras.Length) % cameras.Length;
        Apply();
        LokiTelemetry.Event("ch8_cctv_switch", $"cam={CameraIndex}");
    }

    private void Apply()
    {
        bool open = slot != null && slot.IsPanelOpen;
        for (int i = 0; i < cameras.Length; i++)
            if (cameras[i] != null) cameras[i].enabled = open && i == CameraIndex;
    }

    private bool ShownToLocal()
    {
        return slot != null && slot.manager != null && slot.CanShowTo(slot.manager.ControlledPlayer());
    }

    private void Update()
    {
        if (!ShownToLocal()) return;
        if (Input.GetKeyDown(nextKey) || Input.GetKeyDown(altNextKey)) Step(1);
        else if (Input.GetKeyDown(prevKey) || Input.GetKeyDown(altPrevKey)) Step(-1);
    }

    private void OnGUI()
    {
        if (!ShownToLocal()) return;

        float w = Mathf.Min(680f, Screen.width - 40f);
        float h = w * textureHeight / Mathf.Max(1f, textureWidth);
        Rect box = new Rect((Screen.width - w - 24f) * 0.5f, (Screen.height - h - 64f) * 0.5f, w + 24f, h + 64f);
        GUI.Box(box, $"감시 화면 — {slot.roleId}");

        Rect view = new Rect(box.x + 12f, box.y + 26f, w, h);
        if (cameras.Length == 0) GUI.Label(view, "(CCTV 카메라 없음)");
        else GUI.DrawTexture(view, rt, ScaleMode.ScaleToFit);

        string footer = cameras.Length > 0
            ? $"CAM {CameraIndex + 1} / {cameras.Length}    ←/→ 또는 A/D: 전환    Esc: 사용 종료"
            : "Esc: 사용 종료";
        GUI.Label(new Rect(box.x + 12f, box.yMax - 30f, w, 22f), footer);
    }
}
