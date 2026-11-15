#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// S5 팬트리의 막힌 내부 선반 등반을 대체하는 외곽 경사 동선.
/// 기존 가구·다른 섹터·CP07 이후 앵커 구간은 변경하지 않는다.
/// </summary>
public static class V3Progression
{
    public static void Build()
    {
        GameObject g = V3.Group("S5_Pantry_PlayableRoute");

        Ramp(g, "S5_Ramp_01", V3.Doc(16f, 27f, 0f), V3.Doc(16f, 37f, 4.8f), 4f);
        Landing(g, "S5_Landing_01", 14f, 37f, 4.5f, 20f, 40f, 4.8f);

        Ramp(g, "S5_Ramp_02", V3.Doc(18f, 40f, 4.8f), V3.Doc(18f, 50f, 9.6f), 4f);
        Landing(g, "S5_Landing_02", 14f, 50f, 9.3f, 20f, 53f, 9.6f);

        Ramp(g, "S5_Ramp_03", V3.Doc(16f, 53f, 9.6f), V3.Doc(16f, 63f, 14.4f), 4f);
        Landing(g, "S5_Landing_03", 14f, 63f, 14.1f, 20f, 66f, 14.4f);

        Ramp(g, "S5_Ramp_04", V3.Doc(18f, 66f, 14.4f), V3.Doc(18f, 76f, 19.2f), 4f);
        Landing(g, "S5_Landing_04", 14f, 76f, 18.9f, 20f, 81f, 19.2f);

        // Cab_Upper_A 상면에 직접 연결한다. 이후 CurtainBox→Cab_Upper_B→CP07→기존 앵커 순서다.
        Ramp(g, "S5_Ramp_05_ToRidge", V3.Doc(18f, 78.5f, 19.2f), V3.Doc(10f, 78.5f, 22.2f), 4f);
        Landing(g, "S5_Ridge_Connector", 9f, 76.5f, 21.9f, 12f, 80.5f, 22.2f);
    }

    static void Landing(GameObject parent, string name,
        float x0, float y0, float z0, float x1, float y1, float z1)
    {
        V3.Box(parent, name, x0, y0, z0, x1, y1, z1, "Grain");
    }

    static void Ramp(GameObject parent, string name, Vector3 from, Vector3 to, float width)
    {
        to += Vector3.up * .2f;
        Vector3 delta = to - from;
        Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = name;
        ramp.transform.SetParent(parent.transform, false);
        ramp.transform.rotation = rotation;
        ramp.transform.position = (from + to) * .5f - rotation * Vector3.up * .15f;
        ramp.transform.localScale = new Vector3(width, .3f, delta.magnitude);
        ramp.GetComponent<Renderer>().sharedMaterial = V3.Mat("Grain");
        V3.MarkOwned(ramp, "S5 playable pantry route");
    }
}
#endif
