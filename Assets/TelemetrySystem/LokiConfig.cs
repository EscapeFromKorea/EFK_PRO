using UnityEngine;

public class LokiConfig : ScriptableObject
{
    /// <summary>1순위: 로컬 docker Loki(인증 없음). 꺼져있으면 아래 클라우드로 우회한다.</summary>
    public string localUrl;
    /// <summary>2순위(폴백): Grafana Cloud Loki. Basic Auth 필요.</summary>
    public string url;
    public string user;
    public string token;
}
