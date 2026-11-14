using UnityEngine;
using UnityEngine.Rendering;

/// <summary>진행 판정은 유지하고 현재 조작 도형이 있는 챕터의 안내만 표시한다.</summary>
[DefaultExecutionOrder(300)]
public sealed class Lab_MapPresentation : MonoBehaviour
{
    private Map4Director director;
    private IsolationRescueHud[] isolation;
    private PowerGauge[] gauges;
    private WiringRuleDisplay[] rules;
    private RoleAssignmentManager[] roles;
    private bool[] originalRoleOverlay;
    private bool initialized;
    private SectorController[] sectors;
    private RoleSlot[] slots;
    private Lab_FinalExitFlow finalExit;
    private GUIStyle hintStyle;
    private GUIStyle warningStyle;
    private Lab_AxeReveal[] axeReveals;
    public int CurrentSectorId { get; private set; }
    public bool AmbientFallbackApplied { get; private set; }

    private static readonly string[] Objectives =
    {
        "", "Tab으로 도형을 바꾸며 전진하세요. 카트 두 대와 세 도형을 출구로 모으세요.",
        "안팎의 단서를 번갈아 확인해 레버 순서를 맞히세요. 바깥 전원을 유지한 채 안쪽에서 해제합니다.",
        "발판과 다리를 건너 전진하세요. 중간 체크포인트를 밟으면 낙하 뒤 돌아올 곳이 바뀝니다.",
        "반중력 버블과 계단을 이용해 위층 출구로 향하세요. 보안 장치의 예고를 확인하세요.",
        "천장 아래 경고등과 함정 움직임을 확인하며 건너세요. 진입하면 숨겨진 함정이 나타납니다.",
        "포탈을 연결해 위층 출구에 도달하세요. 바닥 포탈은 중앙에서 멈춰 떨어집니다. 세 도형을 차례로 이동합니다.",
        "입구 회로를 연결하고 책·전력·컴퓨터 역할을 나눠 맡으세요. 컴퓨터에서 Space로 실험을 시작합니다.",
        "책의 단서대로 약물을 조합하고 관리자에게 사용하세요. 열쇠로 출구를 열고 탈출합니다."
    };

    private void LateUpdate()
    {
        if (!initialized)
        {
            if (director == null) director = FindObjectOfType<Map4Director>();
            if (director == null || director.LoadedSectorCount < 8) return;
            isolation = FindObjectsOfType<IsolationRescueHud>(true);
            gauges = FindObjectsOfType<PowerGauge>(true);
            rules = FindObjectsOfType<WiringRuleDisplay>(true);
            roles = FindObjectsOfType<RoleAssignmentManager>(true);
            slots = FindObjectsOfType<RoleSlot>(true);
            finalExit = FindObjectOfType<Lab_FinalExitFlow>(true);
            axeReveals = FindObjectsOfType<Lab_AxeReveal>(true);
            sectors = FindObjectsOfType<SectorController>();
            originalRoleOverlay = new bool[roles.Length];
            for (int i = 0; i < roles.Length; i++) originalRoleOverlay[i] = roles[i].showStatusOverlay;
            // 생성 사본은 Skybox 환경광 probe가 0인 채 로드될 수 있다. 실내 도형이 완전 검정이
            // 되는 경우에만 최소 환경광을 제공하며, 계산/베이크된 probe가 있으면 보존한다.
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            if (Mathf.Abs(probe[0, 0]) + Mathf.Abs(probe[1, 0]) + Mathf.Abs(probe[2, 0]) < 0.0001f)
            {
                Color fill = new Color(0.30f, 0.32f, 0.36f, 1f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = fill;
                SphericalHarmonicsL2 fallback = new SphericalHarmonicsL2();
                fallback.AddAmbientLight(fill);
                RenderSettings.ambientProbe = fallback;
                AmbientFallbackApplied = true;
                Debug.Log("[Lab_MapPresentation] Empty ambient probe: map4 greybox visibility fallback applied.");
            }
            initialized = true;
        }
        CurrentSectorId = 0;
        foreach (SectorController sector in sectors)
            if (IsCurrentSector(sector)) { CurrentSectorId = sector.sectorId; break; }
        foreach (IsolationRescueHud hud in isolation) if (hud != null) hud.enabled = IsCurrentSector(hud);
        foreach (PowerGauge gauge in gauges) if (gauge != null) gauge.enabled = IsCurrentSector(gauge);
        foreach (WiringRuleDisplay rule in rules) if (rule != null) rule.enabled = IsCurrentSector(rule);
        for (int i = 0; i < roles.Length; i++)
            if (roles[i] != null) roles[i].showStatusOverlay = originalRoleOverlay[i] && IsCurrentSector(roles[i]);
    }

    private void OnGUI()
    {
        if (!initialized || CurrentSectorId < 1 || CurrentSectorId > 8 || finalExit != null && finalExit.Completed) return;
        foreach (RoleSlot slot in slots) if (slot != null && slot.IsPanelOpen) return;
        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft,
                wordWrap = true, fontSize = 18, padding = new RectOffset(12, 12, 8, 8) };
        }
        float width = Mathf.Min(420f, Screen.width * 0.42f);
        GUI.Box(new Rect(Screen.width - width - 12f, 70f, width, 112f),
            "CH" + CurrentSectorId + "\n" + Objectives[CurrentSectorId], hintStyle);
        // 기본 옆 시점에서 천장 경고등이 화면 밖이어도 첫 등장 경고를 읽을 수 있다.
        if (CurrentSectorId == 5 && PlayerControlSwitcher.ActiveTarget != null)
            foreach (Lab_AxeReveal axe in axeReveals)
            {
                if (axe == null || !axe.isActiveAndEnabled || axe.Phase == Lab_AxeReveal.RevealPhase.Hidden ||
                    axe.Phase == Lab_AxeReveal.RevealPhase.Running ||
                    Vector3.Distance(PlayerControlSwitcher.ActiveTarget.position, axe.RestPosition) > 12f) continue;
                if (warningStyle == null)
                {
                    warningStyle = new GUIStyle(GUI.skin.box) { fontSize = 22, wordWrap = true,
                        alignment = TextAnchor.MiddleCenter };
                    warningStyle.normal.textColor = new Color(1f, 0.75f, 0.25f);
                }
                float warningWidth = Mathf.Min(620f, Screen.width - 24f);
                GUI.Box(new Rect((Screen.width - warningWidth) * 0.5f, Screen.height - 80f, warningWidth, 60f),
                    "도끼가 내려옵니다. 잠시 멈추고 움직임을 확인하세요.", warningStyle);
                break;
            }
    }

    public static bool IsCurrentSector(Component source)
    {
        SectorController sector = source != null ? source.GetComponentInParent<SectorController>() : null;
        if (sector == null) return true;
        Transform viewer = PlayerControlSwitcher.ActiveTarget;
        if (viewer == null) return false;
        Vector3 local = sector.transform.InverseTransformPoint(viewer.position);
        return Mathf.Abs(local.x) <= sector.width * 0.5f + 1f &&
            local.z >= -1f && local.z <= sector.length + 1f && Mathf.Abs(local.y) <= 30f;
    }
}
