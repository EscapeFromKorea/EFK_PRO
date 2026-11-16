using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

public static class NetProtoMenu
{
    const string Src = "Assets/Scenes/Map4_Laboratory/Players/Base/Player_Sphere_Base.prefab";
    const string Dst = "Assets/NetworkPrototype/Resources/NetProtoPlayer.prefab";
    const string EnableMenu = "Tools/NetProto/Enable (Play Mode)";

    [MenuItem("Tools/NetProto/Create Player Prefab")]
    public static void CreatePrefab() => MakePlayer(Src, Dst);

    // 도형 3종(구는 위 Dst, 정육면체·정사면체는 별도 프리팹) — 같은 구성
    public static void CreateShapePrefabs()
    {
        MakePlayer(Src, Dst);
        MakePlayer("Assets/Scenes/Map4_Laboratory/Players/Base/Player_Cube_Base.prefab", "Assets/NetworkPrototype/Resources/NetProtoPlayer_Cube.prefab");
        MakePlayer("Assets/Scenes/Map4_Laboratory/Players/Base/Player_Tetrahedron_Base.prefab", "Assets/NetworkPrototype/Resources/NetProtoPlayer_Tetra.prefab");
    }

    static void MakePlayer(string src, string dst)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(src));
        inst.AddComponent<NetworkObject>();
        var nt = inst.AddComponent<NetProtoTransform>(); // 권한은 -ownerauth 인자로 런타임 결정
        nt.Interpolate = true;
        inst.AddComponent<NetworkRigidbody>();
        inst.AddComponent<NetProtoPlayer>();
        PrefabUtility.SaveAsPrefabAsset(inst, dst);
        Object.DestroyImmediate(inst);
        Debug.Log("[NetProto] 프리팹 저장: " + dst + " 존재=" + (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null));
    }

    // 서버 권한 키네마틱 이동 발판(10×0.5×10)
    public static void CreatePlatformPrefab()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "NetProtoPlatform";
        go.transform.localScale = new Vector3(10f, 0.5f, 10f);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        go.AddComponent<NetworkObject>();
        var nt = go.AddComponent<NetworkTransform>(); // 서버 권한
        nt.Interpolate = true;
        go.AddComponent<NetworkRigidbody>();
        go.AddComponent<NetProtoPlatform>();
        PrefabUtility.SaveAsPrefabAsset(go, "Assets/NetworkPrototype/Resources/NetProtoPlatform.prefab");
        Object.DestroyImmediate(go);
        Debug.Log("[NetProto] 발판 프리팹 저장");
    }

    // 진짜 RailCart(+좌석·탑승 컴포넌트)를 서버 권한 네트워크 프리팹으로. 레일은 서버가 런타임에 만든다.
    public static void CreateCartPrefab()
    {
        var cart = RailCartMenuItem.CreateRailCartAt(Vector3.zero);
        foreach (var r in cart.scene.GetRootGameObjects()) if (r != cart) Object.DestroyImmediate(r); // 에디터가 만든 레일 경로는 프리팹에 넣지 않는다
        cart.GetComponent<RailCart>().path = null;
        cart.AddComponent<NetworkObject>();
        var nt = cart.AddComponent<NetworkTransform>(); // 서버 권한(기본)
        nt.Interpolate = true;
        cart.AddComponent<NetworkRigidbody>();
        cart.AddComponent<NetProtoCart>();
        PrefabUtility.SaveAsPrefabAsset(cart, "Assets/NetworkPrototype/Resources/NetProtoCart.prefab");
        Object.DestroyImmediate(cart);
        Debug.Log("[NetProto] 카트 프리팹 저장 존재=" + (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NetworkPrototype/Resources/NetProtoCart.prefab") != null));
    }

    [MenuItem(EnableMenu)]
    static void Toggle() => EditorPrefs.SetBool("NetProto.Enabled", !EditorPrefs.GetBool("NetProto.Enabled", false));

    [MenuItem(EnableMenu, true)]
    static bool ToggleCheck()
    {
        Menu.SetChecked(EnableMenu, EditorPrefs.GetBool("NetProto.Enabled", false));
        return true;
    }
}
