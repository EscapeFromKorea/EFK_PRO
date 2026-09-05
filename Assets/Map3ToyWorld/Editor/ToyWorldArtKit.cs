using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Local, deterministic 3D authoring. No downloaded models, textures, shaders or gameplay scripts.
public static class ToyWorldArtKit
{
    public const string Folder = "Assets/Map3ToyWorld/Art";
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    public static Material Mat(string key) => materials[key];
    public static Mesh Shape(string key) => meshes[key];

    public static void Prepare()
    {
        Ensure(Folder); Ensure(Folder + "/Materials"); Ensure(Folder + "/Meshes");
        Ensure(Folder + "/Prefabs"); Ensure(Folder + "/Baked");
        Ensure(Folder + "/Lettering");
        materials.Clear(); meshes.Clear();
        Palette("Stone", "C5BEAA"); Palette("StoneLight", "CFC7B4"); Palette("StoneWarm", "C7B9A0");
        Palette("Mortar", "777C79"); Palette("Ivory", "EEE3C6"); Palette("Slate", "384953");
        Palette("Teal", "299B9E"); Palette("TealLight", "68C9BD"); Palette("TealDark", "206571");
        Palette("Gold", "E4AF44", .25f); Palette("GoldLight", "FFE0A0", .15f);
        Palette("Wood", "BE946B"); Palette("WoodLight", "CFA779"); Palette("WoodDark", "725546");
        Palette("Rose", "C87F8D"); Palette("Lilac", "9D8BBB"); Palette("Navy", "3F5E82");
        Palette("Blue", "519CC9"); Palette("Mint", "9AC9A6"); Palette("Coral", "DA7859");
        Palette("Lime", "C9E866", 0, .12f); Palette("Ink", "26363E");
        meshes["Bevel"] = SaveMesh(BeveledCube(), Folder + "/Meshes/BeveledBlock.asset");
        meshes["Slab"] = SaveMesh(BeveledCube(.495f), Folder + "/Meshes/SubtleBeveledSlab.asset");
        meshes["Cylinder"] = SaveMesh(Prism(12, false), Folder + "/Meshes/FacetedCylinder.asset");
        meshes["Gear"] = SaveMesh(Prism(48, true), Folder + "/Meshes/TwelveToothGear.asset");
        meshes["Ring"] = SaveMesh(Ring(24, 360f, .36f), Folder + "/Meshes/FacetedRing.asset");
        meshes["Arch"] = SaveMesh(Ring(16, 180f, .36f), Folder + "/Meshes/ArchCrown.asset");
        meshes["Star"] = SaveMesh(Star(), Folder + "/Meshes/FivePointStar.asset");
        BuildPrefabs();
    }

    public static Transform Node(string name, Transform parent, Vector3 position = default(Vector3))
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false); t.localPosition = position;
        return t;
    }

    public static Transform Part(string name, Transform parent, Vector3 position, Vector3 size,
        string material, string shape = "Bevel", Quaternion? rotation = null)
    {
        Transform t = Node(name, parent, position);
        t.localScale = size; t.localRotation = rotation ?? Quaternion.identity;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = Shape(shape);
        MeshRenderer r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = Mat(material);
        r.receiveShadows = true;
        return t;
    }

    public static Transform Place(string prefab, Transform parent, Vector3 position, Vector3 scale,
        Quaternion? rotation = null)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/" + prefab + ".prefab");
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        return go.transform;
    }

    // Collapses decorative meshes into a single multi-material renderer; no Colliders or behaviours.
    public static void Bake(Transform root, string assetPath)
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) return;
        List<Material> mats = new List<Material>();
        Dictionary<Material, List<CombineInstance>> groups = new Dictionary<Material, List<CombineInstance>>();
        foreach (MeshFilter f in filters)
        {
            MeshRenderer renderer = f.GetComponent<MeshRenderer>();
            if (renderer == null || f.sharedMesh == null) continue;
            Material[] sourceMats = renderer.sharedMaterials;
            for (int s = 0; s < f.sharedMesh.subMeshCount; s++)
            {
                Material m = sourceMats[Mathf.Min(s, sourceMats.Length - 1)];
                if (!groups.ContainsKey(m)) { groups[m] = new List<CombineInstance>(); mats.Add(m); }
                groups[m].Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s,
                    transform = root.worldToLocalMatrix * f.transform.localToWorldMatrix });
            }
        }
        List<CombineInstance> final = new List<CombineInstance>();
        foreach (Material m in mats)
        {
            Mesh piece = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            piece.CombineMeshes(groups[m].ToArray(), true, true);
            final.Add(new CombineInstance { mesh = piece, transform = Matrix4x4.identity });
        }
        Mesh combined = new Mesh { name = root.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.CombineMeshes(final.ToArray(), false, false);
        foreach (CombineInstance ci in final) UnityEngine.Object.DestroyImmediate(ci.mesh);
        Mesh saved = SaveMesh(combined, assetPath);
        // Called on generated authoring nodes, before they become prefab instances.
        foreach (MeshFilter f in filters) UnityEngine.Object.DestroyImmediate(f.gameObject);
        Transform batch = Node("VisualMesh", root);
        batch.gameObject.AddComponent<MeshFilter>().sharedMesh = saved;
        batch.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
    }

    private static void BuildPrefabs()
    {
        Prefab("CastleTurret", t =>
        {
            Part("Foot", t, new Vector3(0,.15f,0), new Vector3(2.2f,.3f,2.2f), "StoneLight");
            Part("Tower", t, new Vector3(0,1.5f,0), new Vector3(1.9f,2.7f,1.9f), "Stone");
            for (int y=1; y<4; y++) Part("Course", t, new Vector3(0,y*.72f,0), new Vector3(2,.075f,2), "Mortar");
            Part("Crown", t, new Vector3(0,2.85f,0), new Vector3(2.2f,.28f,2.2f), "StoneLight");
            for (int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
                Part("Merlon",t,new Vector3(x*.78f,3.2f,z*.78f),new Vector3(.64f,.65f,.64f),"StoneWarm");
            Part("Window",t,new Vector3(0,1.7f,-.958f),new Vector3(.35f,.8f,.035f),"Slate");
        });
        Prefab("PortalFrame", t =>
        {
            for(int s=-1;s<=1;s+=2)
            {
                Part("Pillar",t,new Vector3(s*2.05f,1.25f,0),new Vector3(.6f,2.5f,.65f),"Slate");
                Part("GoldInset",t,new Vector3(s*1.91f,1.25f,-.34f),new Vector3(.13f,2.5f,.08f),"Gold");
                Part("Foot",t,new Vector3(s*2.1f,.2f,0),new Vector3(.95f,.4f,1),"StoneLight");
            }
            Part("Crown",t,new Vector3(0,2.5f,0),new Vector3(4.7f,4.7f,.65f),"Slate","Arch");
            Part("InnerLight",t,new Vector3(0,2.5f,-.34f),new Vector3(4.05f,4.05f,.1f),"Gold","Arch");
            Part("Keystone",t,new Vector3(0,4.7f,0),new Vector3(.6f,.65f,.8f),"Gold");
        });
        Prefab("ToyKey",t =>
        {
            Part("Stem",t,new Vector3(0,.7f,0),new Vector3(.24f,1.4f,.3f),"Gold");
            Part("LeftLoop",t,new Vector3(-.38f,1.5f,0),new Vector3(.95f,.9f,.3f),"Gold","Ring");
            Part("RightLoop",t,new Vector3(.38f,1.5f,0),new Vector3(.95f,.9f,.3f),"Gold","Ring");
        });
        Prefab("ClockworkMedallion", t =>
        {
            Part("Back",t,Vector3.zero,new Vector3(2.3f,.18f,2.3f),"Slate","Cylinder",Quaternion.Euler(90,0,0));
            Part("Cog",t,new Vector3(0,0,-.13f),new Vector3(1.95f,.16f,1.95f),"Gold","Gear",Quaternion.Euler(90,0,0));
            Part("Cap",t,new Vector3(0,0,-.26f),new Vector3(.65f,.2f,.65f),"Teal","Cylinder",Quaternion.Euler(90,0,0));
        });
        Prefab("Bookcase",t =>
        {
            Part("Back",t,new Vector3(0,1.7f,.35f),new Vector3(2.6f,3.4f,.18f),"WoodDark");
            for(int s=-1;s<=1;s+=2) Part("Side",t,new Vector3(s*1.28f,1.7f,0),new Vector3(.2f,3.4f,1),"Wood");
            string[] colors={"Teal","Coral","Gold","Navy","Lilac"};
            for(int level=0;level<3;level++)
            {
                Part("Shelf",t,new Vector3(0,level*1.1f+.15f,0),new Vector3(2.8f,.2f,1.1f),"WoodLight");
                for(int b=0;b<5;b++)
                {
                    float h=.6f+.1f*((level+b)%3);
                    Part("Book",t,new Vector3(-.95f+b*.44f,level*1.1f+.25f+h*.5f,-.05f),new Vector3(.33f,h,.65f),colors[(b+level)%5]);
                    Part("SpineBand",t,new Vector3(-.95f+b*.44f,level*1.1f+.4f,-.381f),new Vector3(.26f,.055f,.025f),"Ivory");
                }
            }
            Part("Cornice",t,new Vector3(0,3.4f,0),new Vector3(2.9f,.22f,1.15f),"Gold");
        });
        Prefab("DollDresser",t =>
        {
            Part("Body",t,new Vector3(0,1,0),new Vector3(2.5f,1.8f,1.2f),"Rose");
            Part("Top",t,new Vector3(0,2,0),new Vector3(2.8f,.2f,1.4f),"Ivory");
            for(int i=0;i<3;i++)
            {
                Part("Drawer",t,new Vector3(0,.5f+i*.5f,-.62f),new Vector3(2.2f,.4f,.12f),"WoodLight");
                Part("Knob",t,new Vector3(0,.5f+i*.5f,-.74f),Vector3.one*.16f,"Gold","Cylinder",Quaternion.Euler(90,0,0));
            }
        });
        Prefab("ToyBanner",t =>
        {
            Part("Pole",t,new Vector3(0,1.6f,0),new Vector3(.12f,3.2f,.12f),"Gold","Cylinder");
            Part("Flag",t,new Vector3(.6f,2.5f,0),new Vector3(1.2f,.85f,.08f),"Teal");
            Part("Star",t,new Vector3(.6f,2.5f,-.06f),new Vector3(.5f,.5f,.06f),"Gold","Star");
        });
        Prefab("RailSleeper",t =>
        {
            Part("Tie",t,Vector3.zero,new Vector3(.5f,.16f,3),"WoodDark");
            for(int s=-1;s<=1;s+=2) Part("Shoe",t,new Vector3(0,.09f,s*1.16f),new Vector3(.62f,.1f,.5f),"Gold");
        });
        Prefab("MobileStar",t =>
        {
            Part("Hoop",t,Vector3.zero,new Vector3(2.7f,2.7f,.13f),"Teal","Ring",Quaternion.Euler(90,0,0));
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*2/5; Vector3 p=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float length=.8f+(i%3)*.4f;
                Part("Cord",t,p+Vector3.down*length*.5f,new Vector3(.025f,length,.025f),"WoodDark");
                Part("Star",t,p+Vector3.down*(length+.35f),new Vector3(.65f,.65f,.13f),i%2==0?"Gold":"TealLight","Star",Quaternion.Euler(0,i*35,0));
            }
        });
        Prefab("CorePedestal",t =>
        {
            Part("Base",t,new Vector3(0,.14f,0),new Vector3(2.2f,.28f,2.2f),"Slate","Cylinder");
            Part("Ring",t,new Vector3(0,.3f,0),new Vector3(1.95f,1.95f,.12f),"Gold","Ring",Quaternion.Euler(90,0,0));
        });
        Prefab("ToyCrate",t =>
        {
            Part("Body",t,new Vector3(0,.6f,0),new Vector3(1.5f,1.2f,1.4f),"Wood");
            for(int s=-1;s<=1;s+=2) Part("Band",t,new Vector3(s*.53f,.61f,0),new Vector3(.16f,1.25f,1.46f),"Gold");
            Part("Star",t,new Vector3(0,.65f,-.72f),new Vector3(.5f,.5f,.07f),"Teal","Star");
        });
    }

    private static void Prefab(string name, Action<Transform> build)
    {
        Transform root=Node(name,null); build(root);
        Bake(root, Folder+"/Meshes/Kit_"+name+".asset");
        PrefabUtility.SaveAsPrefabAsset(root.gameObject,Folder+"/Prefabs/"+name+".prefab");
        UnityEngine.Object.DestroyImmediate(root.gameObject);
    }

    private static void Palette(string name,string hex,float metallic=0,float emission=0)
    {
        string path=Folder+"/Materials/TWArt_"+name+".mat";
        Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null) { m=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m,path); }
        Color c; ColorUtility.TryParseHtmlString("#"+hex,out c);
        m.color=c; m.SetFloat("_Metallic",metallic); m.SetFloat("_Glossiness",.22f);
        m.enableInstancing=true;
        if(emission>0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor",c*emission); }
        materials[name]=m; EditorUtility.SetDirty(m);
    }

    public static Mesh SaveMesh(Mesh mesh,string path)
    {
        Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null) { AssetDatabase.CreateAsset(mesh,path); return mesh; }
        // Explicit geometry upload also refreshes already-instantiated prefab renderers in this editor frame.
        // CopySerialized alone may leave their GPU buffers stale until the next asset reload.
        existing.Clear(); existing.indexFormat=mesh.indexFormat;
        existing.vertices=mesh.vertices; existing.normals=mesh.normals; existing.uv=mesh.uv;
        existing.subMeshCount=mesh.subMeshCount;
        for(int i=0;i<mesh.subMeshCount;i++) existing.SetTriangles(mesh.GetTriangles(i),i,false);
        existing.RecalculateBounds(); existing.UploadMeshData(false);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing); return existing;
    }

    public static void Ensure(string path)
    {
        if(AssetDatabase.IsValidFolder(path)) return;
        int slash=path.LastIndexOf('/'); Ensure(path.Substring(0,slash));
        AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }

    private sealed class MeshWriter
    {
        readonly List<Vector3> v=new List<Vector3>(); readonly List<int> t=new List<int>();
        public void Face(Vector3 outward,params Vector3[] points)
        {
            if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),outward)<0) Array.Reverse(points);
            int first=v.Count; v.AddRange(points);
            for(int i=1;i<points.Length-1;i++) { t.Add(first); t.Add(first+i); t.Add(first+i+1); }
        }
        public Mesh Finish(string name)
        {
            Mesh m=new Mesh {name=name}; m.SetVertices(v); m.SetTriangles(t,0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    private static Mesh BeveledCube(float a=.44f)
    {
        MeshWriter w=new MeshWriter(); const float h=.5f;
        Vector3[] axes={Vector3.right,Vector3.up,Vector3.forward};
        for(int axis=0;axis<3;axis++) for(int sign=-1;sign<=1;sign+=2)
        {
            Vector3 n=axes[axis]*sign,u=axes[(axis+1)%3]*a,v=axes[(axis+2)%3]*a;
            w.Face(n,n*h-u-v,n*h+u-v,n*h+u+v,n*h-u+v);
        }
        for(int axis=0;axis<3;axis++) for(int s=-1;s<=1;s+=2) for(int q=-1;q<=1;q+=2)
        {
            Vector3 u=axes[(axis+1)%3]*s,v=axes[(axis+2)%3]*q,d=axes[axis]*a;
            w.Face(u+v,u*h+v*a-d,u*a+v*h-d,u*a+v*h+d,u*h+v*a+d);
        }
        for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
            w.Face(new Vector3(x,y,z),new Vector3(x*h,y*a,z*a),new Vector3(x*a,y*h,z*a),new Vector3(x*a,y*a,z*h));
        return w.Finish("Beveled block / flat normals");
    }

    private static Mesh Prism(int sides,bool gear)
    {
        MeshWriter w=new MeshWriter();
        for(int i=0;i<sides;i++)
        {
            int j=(i+1)%sides; float r=gear&&(i%4==0||i%4==3)?.39f:.5f;
            float r2=gear&&(j%4==0||j%4==3)?.39f:.5f;
            Vector3 a=new Vector3(Mathf.Cos(i*Mathf.PI*2/sides)*r,-.5f,Mathf.Sin(i*Mathf.PI*2/sides)*r);
            Vector3 b=new Vector3(Mathf.Cos(j*Mathf.PI*2/sides)*r2,-.5f,Mathf.Sin(j*Mathf.PI*2/sides)*r2);
            w.Face((a+b)*.5f+Vector3.up*.5f,a,b,b+Vector3.up,a+Vector3.up);
            w.Face(Vector3.down,Vector3.down*.5f,b,a);
            w.Face(Vector3.up,Vector3.up*.5f,a+Vector3.up,b+Vector3.up);
        }
        return w.Finish(gear?"Twelve tooth gear":"Twelve sided cylinder");
    }

    private static Mesh Ring(int sides,float degrees,float inner)
    {
        MeshWriter w=new MeshWriter();
        for(int i=0;i<sides;i++)
        {
            float a=i*degrees*Mathf.Deg2Rad/sides,b=(i+1)*degrees*Mathf.Deg2Rad/sides;
            Vector3 u=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0),v=new Vector3(Mathf.Cos(b),Mathf.Sin(b),0);
            Vector3 z=Vector3.forward*.5f;
            w.Face(Vector3.back,u*.5f-z,v*.5f-z,v*inner-z,u*inner-z);
            w.Face(Vector3.forward,u*.5f+z,v*.5f+z,v*inner+z,u*inner+z);
            w.Face(u+v,u*.5f-z,v*.5f-z,v*.5f+z,u*.5f+z);
            w.Face(-u-v,u*inner-z,v*inner-z,v*inner+z,u*inner+z);
            if(degrees<360 && i==0) w.Face(Vector3.down,u*.5f-z,u*inner-z,u*inner+z,u*.5f+z);
            if(degrees<360 && i==sides-1) w.Face(Vector3.down,v*.5f-z,v*inner-z,v*inner+z,v*.5f+z);
        }
        return w.Finish("Faceted ring");
    }

    private static Mesh Star()
    {
        MeshWriter w=new MeshWriter();
        for(int i=0;i<10;i++)
        {
            float a=(90+i*36)*Mathf.Deg2Rad,b=(90+(i+1)*36)*Mathf.Deg2Rad;
            Vector3 u=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*(i%2==0?.5f:.23f);
            Vector3 v=new Vector3(Mathf.Cos(b),Mathf.Sin(b),0)*(i%2==0?.23f:.5f);
            Vector3 z=Vector3.forward*.5f;
            w.Face(Vector3.back,-z,u-z,v-z); w.Face(Vector3.forward,z,u+z,v+z);
            w.Face(u+v,u-z,v-z,v+z,u+z);
        }
        return w.Finish("Five pointed toy star");
    }

    // Tiny extruded-sign-style alphabet built as geometry. Unlike the built-in TextMesh font shader,
    // these letters respect scene depth, have no transient font-atlas reference and cannot show through walls.
    public static Mesh Lettering(string text,float height)
    {
        Dictionary<char,int[]> alphabet=new Dictionary<char,int[]>
        {
            {'A',new[]{14,17,17,31,17,17,17}}, {'B',new[]{30,17,17,30,17,17,30}},
            {'C',new[]{14,17,16,16,16,17,14}}, {'D',new[]{30,17,17,17,17,17,30}},
            {'E',new[]{31,16,16,30,16,16,31}}, {'F',new[]{31,16,16,30,16,16,16}},
            {'G',new[]{14,17,16,23,17,17,15}}, {'H',new[]{17,17,17,31,17,17,17}},
            {'I',new[]{14,4,4,4,4,4,14}}, {'J',new[]{7,2,2,2,2,18,12}},
            {'K',new[]{17,18,20,24,20,18,17}}, {'L',new[]{16,16,16,16,16,16,31}},
            {'M',new[]{17,27,21,21,17,17,17}}, {'N',new[]{17,25,21,19,17,17,17}},
            {'O',new[]{14,17,17,17,17,17,14}}, {'P',new[]{30,17,17,30,16,16,16}},
            {'Q',new[]{14,17,17,17,21,18,13}}, {'R',new[]{30,17,17,30,20,18,17}},
            {'S',new[]{15,16,16,14,1,1,30}}, {'T',new[]{31,4,4,4,4,4,4}},
            {'U',new[]{17,17,17,17,17,17,14}}, {'V',new[]{17,17,17,17,17,10,4}},
            {'W',new[]{17,17,17,21,21,27,17}}, {'X',new[]{17,17,10,4,10,17,17}},
            {'Y',new[]{17,17,10,4,4,4,4}}, {'Z',new[]{31,1,2,4,8,16,31}},
            {'0',new[]{14,17,19,21,25,17,14}}, {'1',new[]{4,12,4,4,4,4,14}},
            {'2',new[]{14,17,1,2,4,8,31}}, {'3',new[]{30,1,1,14,1,1,30}},
            {'4',new[]{2,6,10,18,31,2,2}}, {'5',new[]{31,16,16,30,1,1,30}},
            {'6',new[]{14,16,16,30,17,17,14}}, {'7',new[]{31,1,2,4,8,8,8}},
            {'8',new[]{14,17,17,14,17,17,14}}, {'9',new[]{14,17,17,15,1,1,14}},
            {'/',new[]{1,2,2,4,8,8,16}}, {'>',new[]{16,8,4,2,4,8,16}},
            {'-',new[]{0,0,0,31,0,0,0}}, {'\'',new[]{4,4,0,0,0,0,0}}
        };
        MeshWriter w=new MeshWriter(); float cell=height*.7f/7f;
        float left=-(text.Length*6-1)*cell*.5f;
        for(int c=0;c<text.Length;c++)
        {
            int[] rows; if(!alphabet.TryGetValue(char.ToUpperInvariant(text[c]),out rows)) continue;
            for(int row=0;row<7;row++) for(int col=0;col<5;col++)
            {
                if((rows[row]&(1<<(4-col)))==0) continue;
                float x=left+(c*6+col)*cell,y=(3-row)*cell;
                w.Face(Vector3.back,new Vector3(x,y,0),new Vector3(x+cell,y,0),
                    new Vector3(x+cell,y+cell,0),new Vector3(x,y+cell,0));
            }
        }
        return SaveMesh(w.Finish("Lettering "+text),Folder+"/Lettering/"+Hash128.Compute(text+height.ToString(System.Globalization.CultureInfo.InvariantCulture))+".asset");
    }
}
