using UnityEditor;
using UnityEngine;

/// <summary>
/// CH7 회로 시험용 데이터(ENTRY / POWER_1~3) 에셋을 만든다. 정답 쌍은 docs/PRD/WiringPanel.md "CH7 회로 데이터"
/// 표(미확정 콘텐츠 구성안)를 그대로 옮긴 것이고, 9/25 송원석 회신에 따라 최종 콘텐츠·난도가 아니라
/// 플레이테스트용이다. 정답 쌍과 회로 순서는 <see cref="PowerMaintenanceController.circuits"/> 배열과 이 에셋을
/// 바꿔서 교체한다(랜덤 회로 생성은 범위 밖).
///
/// 이미 있는 에셋은 덮어쓰지 않는다 — 기획이 콘텐츠를 고친 뒤 이 메뉴를 다시 눌러도 수정분이 사라지지 않게 한다.
///
/// 실행: 메뉴 Tools &gt; Server Room Power &gt; Create Test Circuit Data, 또는 배치모드
/// `-batchmode -nographic -quit -executeMethod ServerRoomPowerTestData.CreateFromCommandLine`.
/// </summary>
public static class ServerRoomPowerTestData
{
    public const string Folder = "Assets/ServerRoomPowerSystem/Data";

    private struct Spec
    {
        public string id;
        public string rule;
        public string[] pairs; // "A-2" 형식

        public Spec(string id, string rule, params string[] pairs)
        {
            this.id = id;
            this.rule = rule;
            this.pairs = pairs;
        }
    }

    // docs/PRD/WiringPanel.md §"CH7 회로 데이터" 표 그대로.
    private static readonly Spec[] Specs =
    {
        new Spec("ENTRY", "A는 2번, B는 3번, C는 1번 단자에 연결", "A-2", "B-3", "C-1"),
        new Spec("POWER_1", "출력 라벨과 입력 번호가 A-1/B-2/C-3으로 대응", "A-1", "B-2", "C-3"),
        new Spec("POWER_2", "출력 라벨과 입력 번호가 A-3/B-1/C-2로 대응", "A-3", "B-1", "C-2"),
        new Spec("POWER_3", "출력 라벨과 입력 번호가 A-2/B-3/C-1로 대응", "A-2", "B-3", "C-1")
    };

    public static string PathOf(string circuitId) => $"{Folder}/{circuitId}.asset";

    [MenuItem("Tools/Server Room Power/Create Test Circuit Data")]
    public static void CreateFromMenu()
    {
        EnsureAssets();
    }

    // -executeMethod ServerRoomPowerTestData.CreateFromCommandLine
    public static void CreateFromCommandLine()
    {
        EnsureAssets();
        EditorApplication.Exit(0);
    }

    /// <summary>없는 회로만 만든다. 만든 개수를 돌려준다.</summary>
    public static int EnsureAssets()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ServerRoomPowerSystem", "Data");

        int created = 0;
        foreach (Spec spec in Specs)
        {
            string path = PathOf(spec.id);
            if (AssetDatabase.LoadAssetAtPath<WiringCircuitData>(path) != null)
            {
                Debug.Log($"[PowerData] '{spec.id}' 이미 있음 — 덮어쓰지 않는다.");
                continue;
            }

            WiringCircuitData data = ScriptableObject.CreateInstance<WiringCircuitData>();
            data.circuitId = spec.id;
            data.outputIds = new[] { "A", "B", "C" };
            data.inputIds = new[] { "1", "2", "3" };
            data.ruleText = spec.rule;

            var pairs = new WiringCircuitData.PortPair[spec.pairs.Length];
            for (int i = 0; i < spec.pairs.Length; i++)
            {
                string[] parts = spec.pairs[i].Split('-');
                pairs[i] = new WiringCircuitData.PortPair { outputId = parts[0], inputId = parts[1] };
            }
            data.pairs = pairs;

            if (!data.Validate(out string error))
            {
                Debug.LogError($"[PowerData] '{spec.id}' 콘텐츠 검증 실패 — {error}");
                Object.DestroyImmediate(data);
                continue;
            }

            AssetDatabase.CreateAsset(data, path);
            created++;
            Debug.Log($"[PowerData] '{spec.id}' 생성: {path}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return created;
    }
}
