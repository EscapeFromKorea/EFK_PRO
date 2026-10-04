using System;
using UnityEngine;

/// <summary>
/// 맵4 레벨 머티리얼 팔레트(F1-3). Standard 셰이더 머티리얼을 코드로 찍어내는 데 쓰는 데이터
/// 에셋 — 손으로 만든 머티리얼·팀 머티리얼 수정 금지. Editor/Build/Map4Build.GetMaterial()이
/// 이 팔레트를 읽어 Materials/Generated/에 실제 .mat 에셋을 생성/재사용한다.
/// </summary>
[CreateAssetMenu(fileName = "Map4Palette", menuName = "Laboratory Map4/Palette")]
public class Map4Palette : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string paletteName;
        public Color color = Color.white;
        [Range(0f, 1f)] public float smoothness = 0.3f;
        [Range(0f, 1f)] public float metallic = 0f;
    }

    [Tooltip("초기값은 블록아웃 단계의 임의 배색 — [추정], texture-artist가 자연화 단계에서 교체 대상.")]
    public Entry[] entries = new Entry[]
    {
        new Entry { paletteName = "Floor",  color = new Color(0.20f, 0.22f, 0.24f), smoothness = 0.25f },
        new Entry { paletteName = "Wall",   color = new Color(0.55f, 0.58f, 0.60f), smoothness = 0.20f },
        new Entry { paletteName = "Accent", color = new Color(0.10f, 0.65f, 0.60f), smoothness = 0.40f },
        new Entry { paletteName = "Door",   color = new Color(0.35f, 0.36f, 0.38f), smoothness = 0.35f },
    };

    public Entry Get(string name)
    {
        foreach (Entry e in entries)
            if (string.Equals(e.paletteName, name, StringComparison.OrdinalIgnoreCase))
                return e;
        return null;
    }
}
