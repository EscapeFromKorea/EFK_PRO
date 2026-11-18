using UnityEngine;

/// <summary>
/// [멀티 M2] 임시 홈·로비 화면(IMGUI). 연결·규칙은 MpNet/MpLobby가 맡고 여기는 그리기만 한다 —
/// 정식 메인 화면이 정해지면 이 파일만 갈아끼운다(H-05).
/// </summary>
public class MpMenu : MonoBehaviour
{
    enum V { Home, Join, Busy, Lobby }
    V v = V.Home;
    string code = "", msg;

    void Start()
    {
        MpNet.Ensure();
        msg = MpNet.Message; MpNet.Message = null; // H-04: 오류로 돌아왔으면 메시지 표시
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // 시작 전 로비는 홈 씬 위라 Leave가 씬을 다시 불러오지 않는다 — 세션이 없어졌으면(나가기·방장 이탈·끊김) 홈 화면으로 되돌린다.
    // OnGUI 안에서 바꾸면 한 프레임의 Layout/Repaint 두 패스가 서로 다른 버튼을 그려 IMGUI ArgumentException이 난다 → Update에서 바꾼다.
    void Update()
    {
        if (v != V.Lobby || MpNet.I == null || MpNet.I.Session != null) return;
        v = V.Home;
        msg = MpNet.Message; MpNet.Message = null;
    }

    async void Run(System.Threading.Tasks.Task<string> t, V okView)
    {
        v = V.Busy; msg = null;
        string err = await t;
        if (this == null) return;
        if (err == null) v = okView; else { v = V.Home; msg = err; }
    }

    void OnGUI()
    {
        GUI.skin.label.richText = true;
        float s = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
        GUILayout.BeginArea(new Rect(40, 40, 560, 640));
        GUILayout.Label("<size=28><b>EFK</b></size>");
        if (!string.IsNullOrEmpty(msg)) GUILayout.Label("<color=#ff6b6b>" + msg + "</color>");
        switch (v)
        {
            case V.Home: Home(); break;
            case V.Join: Join(); break;
            case V.Busy: GUILayout.Label("연결 중…"); break;
            case V.Lobby: Lobby(); break;
        }
        GUILayout.EndArea();
    }

    void Home()
    {
        if (GUILayout.Button("방 만들기", GUILayout.Height(40))) Run(MpNet.I.CreateRoom(), V.Lobby);
        if (GUILayout.Button("방 입장하기", GUILayout.Height(40))) { v = V.Join; msg = null; }
        GUI.enabled = false;
        GUILayout.Button("설정 (M5)", GUILayout.Height(40));
        GUI.enabled = true;
        if (GUILayout.Button("게임 종료", GUILayout.Height(40)))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void Join()
    {
        GUILayout.Label("참가 코드");
        code = GUILayout.TextField(code, 6, GUILayout.Height(32)).ToUpperInvariant();
        GUI.enabled = code.Length >= 6;
        if (GUILayout.Button("입장", GUILayout.Height(40))) Run(MpNet.I.JoinRoom(code), V.Lobby);
        GUI.enabled = true;
        if (GUILayout.Button("뒤로", GUILayout.Height(32))) { v = V.Home; msg = null; }
    }

    void Lobby()
    {
        var L = MpLobby.I;
        var mp = MpNet.I;
        if (L == null || L.Owners.Count < 3) // 클라이언트: 로비 오브젝트가 도착할 때까지
        {
            GUILayout.Label("로비 불러오는 중…");
            if (GUILayout.Button("나가기")) _ = mp.Leave(null);
            return;
        }
        bool host = mp.NM.IsServer;
        ulong me = mp.NM.LocalClientId;
        int mine = L.SlotOf(me);

        GUILayout.BeginHorizontal();
        GUILayout.Label("참가 코드: <b><size=22>" + mp.Session.Code + "</size></b>");
        if (GUILayout.Button("복사", GUILayout.Width(60))) GUIUtility.systemCopyBuffer = mp.Session.Code;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("시작 맵" + (host ? "" : " (방장이 고른다)"));
        GUILayout.BeginHorizontal();
        for (int i = 0; i < MpLobby.MapNames.Length; i++)
        {
            GUI.enabled = host;
            bool on = L.Map.Value == i;
            if (GUILayout.Toggle(on, MpLobby.MapNames[i], "Button", GUILayout.Height(32)) && !on) L.SetMapRpc(i);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.Label("도형 선택");
        for (int i = 0; i < 3; i++)
        {
            ulong o = L.Owners[i];
            string who = o == MpLobby.Empty ? "비어 있음" : (o == me ? "나" : "다른 플레이어");
            GUILayout.BeginHorizontal();
            GUILayout.Label(MpLobby.ShapeNames[i], GUILayout.Width(90));
            GUILayout.Label(who + (o != MpLobby.Empty && L.Ready[i] ? "  ✔ 준비" : ""), GUILayout.Width(190));
            if (o == me)
            {
                if (GUILayout.Button(L.Ready[i] ? "준비 해제" : "준비", GUILayout.Height(30))) L.SetReadyRpc(!L.Ready[i]);
            }
            else
            {
                GUI.enabled = o == MpLobby.Empty && (mine < 0 || !L.Ready[mine]); // 이미 점유된 슬롯 선택 불가, 슬롯 변경은 준비 해제 후
                if (GUILayout.Button("선택", GUILayout.Height(30))) L.ClaimSlotRpc(i);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(12);
        if (host)
        {
            GUI.enabled = L.CanStart;
            if (GUILayout.Button("시작", GUILayout.Height(44))) L.StartRpc();
            GUI.enabled = true;
            if (!L.CanStart) GUILayout.Label("<size=12>3명이 서로 다른 도형을 고르고 모두 준비하면 시작할 수 있습니다</size>");
        }
        else GUILayout.Label("방장이 시작하기를 기다리는 중…");
        if (GUILayout.Button("나가기", GUILayout.Height(32))) _ = mp.Leave(null);
    }
}
