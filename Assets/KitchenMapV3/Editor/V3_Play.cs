#if UNITY_EDITOR
using System;
using UnityEngine;
public static class V3Play {
 public static bool SetupPlay() {
  if(!V3.EnsureOwnedScene("Team Setup Play"))return false;
  foreach(Camera camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
   if(!V3.IsOwned(camera.gameObject)){Debug.LogError("Team Setup: Empty 전용 씬에서 실행하세요. 기존 카메라는 변경하지 않습니다.");return false;}
  try {
   // The team factory attaches its camera script to an existing scene Camera.
   if(Camera.main==null){var c=new GameObject("Main Camera");c.tag="MainCamera";c.AddComponent<Camera>();c.AddComponent<AudioListener>();V3.MarkOwned(c,"TeamSetup Camera host");}
   string[] shapes={"Sphere","Cube","Tetrahedron"};
   Vector3[] pos={new Vector3(62,1,6),new Vector3(65,1,6),new Vector3(63.5f,1,8)};
   for(int i=0;i<shapes.Length;i++) {
    GameObject go=V3.FindInActiveScene("Player_"+shapes[i],true);
    if(go==null)go=V3Gimmicks.Create<PlayerMover>("Tools/PlayerSystem/Create Player/"+shapes[i],null).gameObject;
    go.transform.position=pos[i];
   }
   if(UnityEngine.Object.FindObjectOfType<PlayerFollowCamera>()==null)throw new Exception("Team PlayerFollowCamera missing");
   if(UnityEngine.Object.FindObjectOfType<Light>()==null){var g=new GameObject("Kitchen_Sun");V3.MarkOwned(g,"TeamSetup");var l=g.AddComponent<Light>();l.type=LightType.Directional;l.intensity=1.1f;g.transform.rotation=Quaternion.Euler(55,-30,0);}
   return true;
  }catch(Exception e){Debug.LogException(e);return false;}
 }
}
#endif
