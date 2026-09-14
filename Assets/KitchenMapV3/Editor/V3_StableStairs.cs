#if UNITY_EDITOR
using UnityEngine;

// Local trial 2026-09-15. Keeps both destinations at 9.6m; no player/team code changes.
public static class V3StableStairs
{
    public static void Island(GameObject g)
    {
        // Three 4m-wide flights, 2m treads, 0.8m rises. Turns are solid, not gaps.
        for (int i = 0; i < 4; i++)
        {
            V3.Box(g, $"Crate_StableStep_{i+1:00}", 38, 46+2*i, 0, 42, 48+2*i, .8f*(i+1), "Grain");
            V3.Box(g, $"Crate_StableStep_{i+5:00}", 42, 52-2*i, 0, 46, 54-2*i, .8f*(i+5), "Grain");
            V3.Box(g, $"Crate_StableStep_{i+9:00}", 46, 46+2*i, 0, 50, 48+2*i, .8f*(i+9), "Grain");
        }
        V3.Box(g, "Crate_StableLanding_1", 38, 54, 0, 46, 57, 3.2f, "Grain");
        V3.Box(g, "Crate_StableLanding_2", 42, 43, 0, 50, 46, 6.4f, "Grain");
        V3.Box(g, "Crate_StableLanding_Exit", 46, 54, 0, 50, 58, 9.6f, "Grain");
    }

    public static void Counter(GameObject g)
    {
        // Preserve the existing 2.4m tread and 3.9m width; add two lower steps to the east.
        for (int i = 0; i < 12; i++)
            V3.Box(g, $"Box_Step_{i+1}", 10.4f+2.4f*i, 70.5f, 0, 12.8f+2.4f*i, 74.4f, .8f*(i+1), "Grain");
    }
}
#endif
