#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
public static class V3YardBridgeLayout {
 public static void Apply(){
  if(!V3.EnsureOwnedScene("Yard bridge geometry"))throw new System.InvalidOperationException("Not an owned kitchen scene");
  var root=GameObject.Find(V3.RootName);if(root==null)return;
  foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("DryRack_")||t.name=="Yard_Bridge_Approaches").ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
  var bridge=root.GetComponentsInChildren<ThreadBridge>(true).First(b=>b.name=="Yard_Clothesline");
  bridge.anchorA.transform.position=new Vector3(8,4.7f,106);
  bridge.anchorB.transform.position=new Vector3(17.5f,4.7f,106);
  var group=new GameObject("Yard_Bridge_Approaches");group.transform.SetParent(root.transform,false);V3.MarkOwned(group,"Yard bridge geometry");
  foreach(float x in new[]{8f,17.5f}){
   V3.Box(group,"YardBridge_Landing_"+x,x==8?x-1.5f:x,104,4.2f,x==8?x:x+1.5f,107.5f,4.5f,"Grain");
   var a=new Vector3(x,0,96);var b=new Vector3(x,4.8f,104.5f);var d=b-a;var q=Quaternion.LookRotation(d.normalized,Vector3.up);
   var ramp=GameObject.CreatePrimitive(PrimitiveType.Cube);ramp.name="YardBridge_Ramp_"+x;ramp.transform.SetParent(group.transform,false);ramp.transform.rotation=q;ramp.transform.position=(a+b)*.5f-q*Vector3.up*.15f;ramp.transform.localScale=new Vector3(3,.3f,d.magnitude);ramp.GetComponent<Renderer>().sharedMaterial=V3.Mat("Grain");V3.MarkOwned(ramp,"Yard bridge approach");
   var edgeA=new Vector3(x+(x==8?1.3f:-1.3f),3.5f,105.75f);var edgeB=new Vector3(x+(x==8?-.2f:.2f),4.5f,105.75f);var ed=edgeB-edgeA;var eq=Quaternion.LookRotation(ed.normalized,Vector3.up);
   var lip=GameObject.CreatePrimitive(PrimitiveType.Cube);lip.name="YardBridge_Bevel_"+x;lip.transform.SetParent(group.transform,false);lip.transform.rotation=eq;lip.transform.position=(edgeA+edgeB)*.5f-eq*Vector3.up*.1f;lip.transform.localScale=new Vector3(3.5f,.2f,ed.magnitude);lip.GetComponent<Renderer>().sharedMaterial=V3.Mat("Grain");V3.MarkOwned(lip,"Bridge landing beveled edge");
  }
 }
}
#endif
