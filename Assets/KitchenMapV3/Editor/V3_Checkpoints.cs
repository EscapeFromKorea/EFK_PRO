#if UNITY_EDITOR
using System;
using UnityEngine;
public static class V3Checkpoints {
 // Scene volume placement only. Team controller values and zone registration logic are unchanged.
 static readonly Vector3[] Points={new Vector3(63,0,13),new Vector3(39,0,43),new Vector3(39.5f,9.6f,68),new Vector3(86.5f,9.6f,81),new Vector3(31,9.6f,77),new Vector3(14.5f,10.2f,82.6f),new Vector3(55,22.2f,77),new Vector3(93.5f,20,55),new Vector3(92,24,80),new Vector3(19.5f,0,93),new Vector3(30,0,91.5f),new Vector3(57,0,102)};
 public static bool PlaceChecked(){
  if(!V3.EnsureOwnedScene("Team Checkpoints"))return false;
  try {
   var parent=V3.Group("V3_Checkpoints");
   if(parent.transform.childCount>0)throw new Exception("Create a new Empty scene before rebuilding checkpoints.");
   if(UnityEngine.Object.FindObjectOfType<RespawnController>()==null)V3Gimmicks.Create<RespawnController>("Tools/Respawn/Create Respawn Controller",null);
   for(int i=0;i<Points.Length;i++){
    var zone=V3Gimmicks.Create<RespawnZone>("Tools/Respawn/Create Checkpoint Pole",parent.transform);
    zone.name="CP_"+(i+1).ToString("00");zone.transform.position=Points[i];
    // Local trigger size avoids overlapping floors; no gating or respawn-height overrides.
    var box=zone.GetComponent<BoxCollider>();box.size=new Vector3(6,3,6);box.center=new Vector3(0,1.5f,0);
   }
   return true;
  }catch(Exception e){Debug.LogException(e);return false;}
 }
}
#endif
