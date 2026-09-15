#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class V3Gimmicks {
 // Team menu factory. Own only roots created synchronously by this call; retain all generated links.
 public static T Create<T>(string menu,Transform parent) where T:Component {
  var before=new HashSet<int>(UnityEngine.Object.FindObjectsOfType<T>(true).Select(x=>x.GetInstanceID()));
  var roots=new HashSet<int>(SceneManager.GetActiveScene().GetRootGameObjects().Select(x=>x.GetInstanceID()));
  if(!EditorApplication.ExecuteMenuItem(menu))throw new Exception("Missing team menu: "+menu);
  var added=UnityEngine.Object.FindObjectsOfType<T>(true).Where(x=>!before.Contains(x.GetInstanceID())).ToArray();
  foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())if(!roots.Contains(root.GetInstanceID())){V3.MarkOwned(root,"Team menu: "+menu);if(parent!=null)root.transform.SetParent(parent,true);}
  if(added.Length!=1)throw new Exception(menu+": expected one "+typeof(T).Name+", got "+added.Length);
  return added[0];
 }
 static GameObject Find(string prefix){return V3.Root().GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.StartsWith(prefix,StringComparison.Ordinal))?.gameObject;}
 static void RemoveVisual(string prefix){var g=Find(prefix);if(g!=null)UnityEngine.Object.DestroyImmediate(g);}
 static void WeightDoor(Transform parent,string name,Vector3 platePosition,Vector3 doorPosition){
  var plate=Create<ExitWeightPlate>("Tools/DoorSystem/Create Exit Weight Plate (+ Door)",parent);
  plate.name=name+"_WeightPlate";plate.transform.position=platePosition;
  if(plate.targetDoor==null)throw new Exception(name+" door is not linked");
  plate.targetDoor.name=name+"_Door";plate.targetDoor.transform.position=doorPosition;
 }
 static ThreadAnchor Anchor(Transform parent,string name,Vector3 pos){var a=Create<ThreadAnchor>("Tools/DreamThread/Create Anchor",parent);a.name=name;a.transform.position=pos;return a;}
 public static void EnsureExitLanding(){
  if(!V3.EnsureOwnedScene("Exit landing"))throw new Exception("Not an owned kitchen scene");
  var root=V3.Root();if(root.transform.Find("Team_GoalLanding")!=null)return;
  var g=new GameObject("Team_GoalLanding");g.transform.SetParent(root.transform,false);V3.MarkOwned(g,"Safe exit geometry; no win logic");
  V3.Box(g,"Goal_SafeLanding",44,109.5f,-.4f,54,115,0,"Grain");
  V3.Box(g,"Goal_LandingRail_L",43.7f,110,0,44,115,1.2f,"Gray");
  V3.Box(g,"Goal_LandingRail_R",54,110,0,54.3f,115,1.2f,"Gray");
  V3.Box(g,"Goal_LandingRail_End",44,114.7f,0,54,115,1.2f,"Gray");
 }
 static void Bridge(Transform parent,string name,Vector3 a,Vector3 b){var bridge=Create<ThreadBridge>("Tools/DreamThread/Create Rope Bridge",parent);bridge.name=name;bridge.anchorA.transform.position=a;bridge.anchorB.transform.position=b;}
 public static bool WireChecked(){
  if(!V3.EnsureOwnedScene("Team Gimmicks"))return false;
  try{
   var group=V3.Group("V3_Gimmicks");if(group.transform.childCount>0)throw new Exception("Create a new scene to rebuild team gimmicks.");
   // Existing team door mechanics and default weight/latch. No custom P5 timer/sensors.
   RemoveVisual("StartMat");
   WeightDoor(group.transform,"Start",new Vector3(63,.08f,13),new Vector3(63,3.936f,18));
   RemoveVisual("GOAL_Gate");RemoveVisual("PlateA");RemoveVisual("PlateB");RemoveVisual("PlateC");
   WeightDoor(group.transform,"Goal",new Vector3(49,.08f,104),new Vector3(49,3.936f,109));
   EnsureExitLanding();
   // Six genuine swing anchors. Team controller is generated once by its own factory.
   for(int i=1;i<=6;i++){var visual=Find("SwingRing_"+i+"_");if(visual==null)throw new Exception("Swing visual missing");Anchor(group.transform,"Swing_"+i,visual.transform.position);}
   // Replace the old dummy clothesline with the team bridge. Layout helper relocates
   // it to the former decorative drying-rack area with ground-access ramps.
   RemoveVisual("Clothesline_");
   Bridge(group.transform,"Yard_Clothesline",new Vector3(61,1.2f,97),new Vector3(73,1.2f,97));
   V3YardBridgeLayout.Apply();
   // Team bridge replaces the dummy cart which previously filled the island gap.
   RemoveVisual("Trolley_카트");RemoveVisual("BrakePlate");
   Bridge(group.transform,"Island_Crossing",new Vector3(49,9.8f,69),new Vector3(49,9.8f,76));
   // Genuine rail cart + winding axle, on clear floor east of dining area. No teleport docking.
   var cartGo=RailCartMenuItem.CreateRailCartAt(new Vector3(85,.02f,42));
   V3.MarkOwned(cartGo,"Team RailCart");cartGo.transform.SetParent(group.transform,true);
   var cart=cartGo.GetComponent<RailCart>();V3.MarkOwned(cart.path.gameObject,"Team RailPath");cart.path.transform.SetParent(group.transform,true);
   var axle=Create<WindupAxle>("Tools/WindupAxleSystem/Create Windup Axle",group.transform);axle.transform.position=new Vector3(81,0,42);cart.axle=axle;
   // A real lever/door set in the yard; retain factory links.
   var leverSet=new GameObject("Team_LeverStation");leverSet.transform.SetParent(group.transform,false);
   Create<LeverHead>("Tools/DoorSystem/Create Door Set (Door + Lever + Pad)",leverSet.transform);
   leverSet.transform.position=new Vector3(18,3.936f,18);
   foreach(var r in group.GetComponentsInChildren<Renderer>())if(r.sharedMaterial==null)throw new Exception("Missing team material");
   return true;
  }catch(Exception e){Debug.LogException(e);return false;}
 }
}
#endif
