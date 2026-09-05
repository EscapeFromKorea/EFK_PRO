using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ToyWorldArtValidation
{
    [MenuItem("Tools/The Axiom/Art/Validate ToyWorld Art")]
    public static void Validate()
    {
        if(ToyWorldPrototypeValidator.ValidateScene(false)!=0) throw new InvalidOperationException("Gameplay validation failed.");
        Transform root=GameObject.Find("Map3_ToyWorld_Root").transform;
        int triangles=0,renderers=0,artRoots=0;
        foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if(t.name!="Art_Stylized") continue;
            artRoots++;
            if(t.GetComponentsInChildren<Collider>(true).Length!=0 || t.GetComponentsInChildren<Rigidbody>(true).Length!=0)
                throw new InvalidOperationException("Art subtree contains gameplay physics: "+t.parent.name);
        }
        foreach(MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(f.sharedMesh==null) throw new InvalidOperationException("Missing mesh: "+f.name);
            MeshRenderer r=f.GetComponent<MeshRenderer>();
            if(r==null) throw new InvalidOperationException("Missing renderer: "+f.name);
            foreach(Material m in r.sharedMaterials)
                if(m==null || m.shader==null || !m.shader.isSupported || m.shader.name=="Hidden/InternalErrorShader")
                    throw new InvalidOperationException("Invalid material/shader: "+f.name);
            if(r.enabled) { renderers++; triangles+=f.sharedMesh.triangles.Length/3; }
        }
        if(artRoots<50) throw new InvalidOperationException("Art dressing is incomplete.");
        string[] prefabs=AssetDatabase.FindAssets("t:Prefab",new[]{ToyWorldArtKit.Folder+"/Prefabs"});
        foreach(string guid in prefabs)
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if(prefab.GetComponentsInChildren<Collider>(true).Length!=0 || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length!=0)
                throw new InvalidOperationException("Visual kit prefab unexpectedly contains gameplay logic.");
        }
        Debug.Log($"[ToyWorldArt] ART_VALIDATION_PASS roots={artRoots}, prefabs={prefabs.Length}, visibleMeshRenderers={renderers}, triangles={triangles}.");
    }

    public static void BuildAndCapture()
    {
        ToyWorldPrototypeBuilder.BuildFromCommandLine();
        Validate();
        Transform generated=GameObject.Find("Map3_ToyWorld_Root").transform.Find("Generated");
        int count=generated.GetComponentsInChildren<Transform>(true).Length;
        ToyWorldArtDirector.Apply(generated);
        if(count!=generated.GetComponentsInChildren<Transform>(true).Length)
            throw new InvalidOperationException("Art reapplication duplicated or lost scene objects.");
        Validate();
        EditorSceneManager.SaveScene(generated.gameObject.scene);
        Debug.Log("[ToyWorldArt] ART_REAPPLY_PASS: identical object count after second art pass.");
        Capture();
    }

    public static void Capture()
    {
        string folder=Path.GetFullPath("Assets/Map3ToyWorld/Validation/ArtPreviews");
        Directory.CreateDirectory(folder);
        GameObject go=new GameObject("Temporary_ArtReviewCamera");
        Camera camera=go.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.77f,.81f,.8f);
        camera.farClipPlane=500; camera.nearClipPlane=.1f; camera.orthographic=true;
        float oldShadowDistance=QualitySettings.shadowDistance;
        QualitySettings.shadowDistance=350f;
        Shot(camera,folder,"01_WholeMap",new Vector3(-65,168,-126),new Vector3(3,0,0),73);
        Shot(camera,folder,"02_ToyBox",new Vector3(-24,27,-16),new Vector3(0,1,-43),22);
        Shot(camera,folder,"03_BlockFort",new Vector3(-8,28,-9),new Vector3(-40,1,18),23);
        Shot(camera,folder,"04_TrainYard",new Vector3(20,29,-15),new Vector3(42,1,18),24);
        Shot(camera,folder,"05_DollHouse",new Vector3(8,29,1),new Vector3(34,10,-35),23);
        Shot(camera,folder,"06_MusicBox",new Vector3(-31,33,9),new Vector3(0,4,41),24);
        Shot(camera,folder,"07_Plaza",new Vector3(-19,26,-27),new Vector3(0,1,1),23);
        QualitySettings.shadowDistance=oldShadowDistance;
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.Refresh();
        Debug.Log("[ToyWorldArt] ART_CAPTURE_PASS: "+folder);
    }
    private static void Shot(Camera camera,string folder,string name,Vector3 position,Vector3 look,float size)
    {
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(look-position));
        camera.orthographicSize=size;
        RenderTexture target=new RenderTexture(1600,1200,24) {antiAliasing=4};
        RenderTexture previous=RenderTexture.active;
        camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
        Texture2D texture=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1600,1200),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());
        camera.targetTexture=null; RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
    }
}
