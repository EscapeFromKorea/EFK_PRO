#if UNITY_EDITOR
using UnityEngine;
public static class V3StableStairs {
 static void Ramp(GameObject p,string n,Vector3 a,Vector3 b,float width){b+=Vector3.up*.35f;var d=b-a;var q=Quaternion.LookRotation(d.normalized,Vector3.up);var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(p.transform,false);g.transform.rotation=q;g.transform.position=(a+b)*.5f-q*Vector3.up*.15f;g.transform.localScale=new Vector3(width,.3f,d.magnitude);g.GetComponent<Renderer>().sharedMaterial=V3.Mat("Grain");V3.MarkOwned(g,"Stable ramp geometry");}
 public static void Island(GameObject g){
 Ramp(g,"Crate_Ramp_01",new Vector3(40,0,46),new Vector3(40,3.2f,54),4);
 Ramp(g,"Crate_Ramp_02",new Vector3(44,3.2f,54),new Vector3(44,6.4f,46),4);
 Ramp(g,"Crate_Ramp_03",new Vector3(48,6.4f,46),new Vector3(48,9.6f,54),4);
 V3.Box(g,"Crate_StableLanding_1",38,54,0,46,57,3.2f,"Grain");
 V3.Box(g,"Crate_StableLanding_2",42,43,0,50,46,6.4f,"Grain");
 V3.Box(g,"Crate_StableLanding_Exit",46,54,0,50,58,9.6f,"Grain");
 }
 public static void Counter(GameObject g){
 Ramp(g,"Counter_Entry_Ramp",new Vector3(11.6f,0,67.7f),new Vector3(11.6f,.8f,70.5f),2.4f);
 V3.Box(g,"Counter_Entry_Landing",10.4f,70.5f,0,12.8f,74.4f,.8f,"Grain");
 Ramp(g,"Counter_Ramp_01",new Vector3(12.8f,.8f,72.45f),new Vector3(20.8f,4,72.45f),3.9f);
 V3.Box(g,"Counter_Rest_01",20.8f,70.5f,0,23.2f,74.4f,4,"Grain");
 Ramp(g,"Counter_Ramp_02",new Vector3(23.2f,4,72.45f),new Vector3(29.2f,6.4f,72.45f),3.9f);
 V3.Box(g,"Counter_Rest_02",29.2f,70.5f,0,31.2f,74.4f,6.4f,"Grain");
 Ramp(g,"Counter_Ramp_03",new Vector3(31.2f,6.4f,72.45f),new Vector3(39.2f,9.6f,72.45f),3.9f);
 V3.Box(g,"Counter_Exit_Landing",39.2f,70.5f,9.3f,44.8f,76,9.6f,"Grain");
 }
}
#endif

