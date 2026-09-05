using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ToyWorldArtKit;

// Art-only pass over the existing functional scene. Never edits a Collider, Joint or gimmick setting.
public static class ToyWorldArtDirector
{
    private const string ArtName="Art_Stylized";
    private static readonly List<Transform> batches=new List<Transform>();

    [MenuItem("Tools/The Axiom/Art/Apply ToyWorld Low Poly Art")]
    public static void ApplyMenu()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before applying art.");
        GameObject root=GameObject.Find("Map3_ToyWorld_Root");
        if(root==null) throw new InvalidOperationException("Open the Map3 ToyWorld scene first.");
        Apply(root.transform.Find("Generated"));
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        AssetDatabase.SaveAssets();
    }

    public static void Apply(Transform generated)
    {
        if(generated==null) throw new ArgumentNullException(nameof(generated));
        string physicsBefore=PhysicsSignature(generated);
        // Only our generated art subtrees are replaced. Manual and all gameplay roots stay intact.
        foreach(Transform t in generated.GetComponentsInChildren<Transform>(true))
            if(t!=null && t.name==ArtName) UnityEngine.Object.DestroyImmediate(t.gameObject);
        batches.Clear(); Prepare();
        BoxCollider[] boxes=generated.GetComponentsInChildren<BoxCollider>(true);
        foreach(BoxCollider box in boxes)
        {
            if(box.isTrigger || box.GetComponent<PlayerMover>()!=null) continue;
            DressSolid(box);
        }
        DressMechanisms(generated);
        DressLandmarks(generated);
        DressLighting(generated);
        foreach(Transform batch in batches)
        {
            string key=Hash128.Compute(HierarchyPath(batch)).ToString();
            Bake(batch,Folder+"/Baked/Dress_"+key+".asset");
        }
        if(physicsBefore!=PhysicsSignature(generated))
            throw new InvalidOperationException("Art pass changed gameplay physics configuration.");
        AssetDatabase.SaveAssets();
        Debug.Log("[ToyWorldArt] ART_PASS: gameplay Collider/Rigidbody/Joint fingerprint unchanged. " +
            "11 modular prefabs, 7 base meshes; visual-only low-poly dressing applied.");
    }

    private static Transform Art(Transform owner)
    {
        Transform root=owner.Find(ArtName);
        if(root==null) root=Node(ArtName,owner);
        return root;
    }
    private static Transform Geometry(Transform owner)
    {
        Transform art=Art(owner); Transform geometry=art.Find("BatchedGeometry");
        if(geometry==null) { geometry=Node("BatchedGeometry",art); batches.Add(geometry); }
        return geometry;
    }
    private static string HierarchyPath(Transform t)
    {
        string result=t.name+"_"+t.GetSiblingIndex();
        while(t.parent!=null) {t=t.parent; result=t.name+"/"+result;} return result;
    }

    private static void Skin(Transform owner,Vector3 size,string material,string mesh="Slab")
    {
        Transform visual=owner.Find("VisualMesh");
        if(visual==null || visual.GetComponent<MeshFilter>()==null) return;
        visual.GetComponent<MeshFilter>().sharedMesh=Shape(mesh);
        visual.GetComponent<Renderer>().sharedMaterial=Mat(material);
        visual.localScale=size;
    }

    private static void DressSolid(BoxCollider box)
    {
        Transform t=box.transform; string n=t.name; Vector3 s=box.size;
        if(n.StartsWith("RESET_") || n.StartsWith("CP_") || n.StartsWith("TRG_")) return;
        Transform g=Geometry(t);
        bool doll=HierarchyPath(t).Contains("DollHouse");
        bool toy=n.Contains("ToyBox")||n.Contains("BrokenShelf");
        bool floor=n.Contains("Floor")||n.Contains("Bank")||n=="GEO_DollHouseBase";
        bool walkway=n.StartsWith("GEO_Path_")&&!n.Contains("VisibleRail");
        bool wall=n.Contains("Wall")||n.Contains("Barrier")||n.Contains("Partition")||n.Contains("Lintel")||n.Contains("BrokenShelf");
        bool level=n.StartsWith("GEO_")&&(n.Contains("DollLevel")||n.Contains("DollAttic")||n.Contains("ExitPlatform")||n.Contains("Battlement"));
        if(floor||walkway||level)
        {
            Skin(t,s,toy||doll?"WoodDark":"Mortar");
            TileTop(g,s,toy||doll,level?1.8f:2.6f);
            if(floor) Foundation(g,s,toy||doll?"Wood":"Stone");
            if(level) EdgeBand(g,s,doll?"Rose":"Gold");
        }
        else if(wall)
        {
            Skin(t,s,toy?"WoodDark":doll?"Rose":"Mortar");
            WallSurface(g,s,toy,doll);
        }
        else if(n.Contains("VisibleRail"))
        {
            Skin(t,s,"TealDark");
            Part("Handrail",g,new Vector3(0,s.y*.5f,0),new Vector3(s.x+.12f,.16f,s.z),"Gold");
            for(float z=-s.z*.5f+.4f;z<s.z*.5f;z+=2.5f)
                Part("Baluster",g,new Vector3(0,0,z),new Vector3(s.x+.06f,s.y,.13f),"Ivory");
        }
        else if(n.StartsWith("RAIL_"))
        {
            Skin(t,s,"Slate");
            Part("PolishedRail",g,new Vector3(0,s.y*.5f,0),new Vector3(s.x,.055f,s.z),"Gold");
        }
        else if(t.GetComponent<SnapBlock>()!=null)
        {
            Skin(t,s,"Teal","Bevel"); EdgeBand(g,s,"TealLight");
            for(int side=-1;side<=1;side+=2)
            {
                Part("SnapSocket",g,new Vector3(side*(s.x*.5f+.025f),0,0),new Vector3(.45f,.07f,.45f),"TealDark","Cylinder",Quaternion.Euler(0,0,90));
                Part("SnapPin",g,new Vector3(side*(s.x*.5f+.065f),0,0),new Vector3(.23f,.06f,.23f),"Gold","Cylinder",Quaternion.Euler(0,0,90));
            }
            Part("Inset",g,new Vector3(0,0,-s.z*.5f-.015f),new Vector3(s.x*.63f,s.y*.62f,.035f),"TealLight");
            Part("Star",g,new Vector3(0,0,-s.z*.5f-.042f),new Vector3(.43f,.43f,.03f),"Ivory","Star");
        }
        else if(t.GetComponent<JumpPad>()!=null || t.GetComponent<AccelPad>()!=null)
        {
            Skin(t,s,"Slate","Bevel");
            Part("JumpCushion",g,new Vector3(0,s.y*.5f-.015f,0),new Vector3(s.x*.86f,.08f,s.z*.86f),"Lilac");
            Chevrons(g,s.y*.5f+.04f,s.z,.7f,"Ivory");
            CornerBolts(g,s,"Gold");
        }
        else if(t.GetComponent<RotatingPlate>()!=null)
        {
            Skin(t,s,n.Contains("Doll")?"WoodLight":"Teal");
            EdgeBand(g,s,"Gold");
            Part("PlayableInset",g,new Vector3(0,s.y*.5f-.022f,0),new Vector3(s.x*.85f,.06f,s.z*.91f),
                n.Contains("DollBed")?"Blue":n.Contains("Doll")?"TealLight":"TealLight");
            CornerBolts(g,s,"Gold");
            if(n.Contains("DollBed"))
            {
                // Quilt/pillow relief is shallow: the original flat board remains the walkable surface.
                for(int i=0;i<5;i++) Part("QuiltSeam",g,new Vector3(0,s.y*.5f+.005f,-s.z*.35f+i*s.z*.14f),new Vector3(s.x*.8f,.008f,.04f),"Ivory");
                Part("PillowInlay",g,new Vector3(0,s.y*.5f+.005f,s.z*.32f),new Vector3(s.x*.67f,.01f,s.z*.14f),"Ivory");
            }
            // Axles stay below the physical board, not on a new blocking support.
            Part("Axle",g,new Vector3(0,-s.y*.5f-.1f,0),new Vector3(.45f,s.x+.3f,.45f),"Gold","Cylinder",Quaternion.Euler(0,0,90));
        }
        else if(t.GetComponent<LiftPlatform>()!=null || t.GetComponent<CloudTrampoline>()!=null)
        {
            Skin(t,s,"Teal"); EdgeBand(g,s,"Gold"); CornerBolts(g,s,"GoldLight");
            Part("Deck",g,new Vector3(0,s.y*.5f-.025f,0),new Vector3(s.x*.89f,.08f,s.z*.87f),"TealLight");
            for(int i=-1;i<=1;i++) Part("DeckSeam",g,new Vector3(i*s.x*.22f,s.y*.5f+.02f,0),new Vector3(.035f,.015f,s.z*.8f),"TealDark");
            if(t.GetComponent<CloudTrampoline>()!=null)
                for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
                {
                    Part("Wheel",g,new Vector3(x*s.x*.32f,-.35f,z*s.z*.42f),new Vector3(.7f,.22f,.7f),"Slate","Cylinder",Quaternion.Euler(90,0,0));
                    Part("Hub",g,new Vector3(x*s.x*.32f,-.35f,z*(s.z*.42f+.13f)),new Vector3(.3f,.08f,.3f),"Gold","Cylinder",Quaternion.Euler(90,0,0));
                }
        }
        else if(t.GetComponent<doorPhysics>()!=null)
        {
            Skin(t,s,"TealDark");
            for(int i=-2;i<=2;i++) Part("DoorPanel",g,new Vector3(i*s.x*.18f,0,-s.z*.5f-.025f),new Vector3(s.x*.16f,s.y*.9f,.1f),"Teal");
            for(int sign=-1;sign<=1;sign+=2)
                Part("Strap",g,new Vector3(0,sign*s.y*.32f,-s.z*.5f-.11f),new Vector3(s.x*.95f,.22f,.12f),"Gold");
            Place("ClockworkMedallion",Art(t),new Vector3(0,0,-s.z*.5f-.16f),Vector3.one*.7f);
        }
        else if(t.GetComponent<StickerSurface>()!=null)
        {
            Skin(t,s,"Navy"); EdgeBand(g,s,"TealLight"); Chevrons(g,s.y*.5f+.002f,s.z,1,"TealLight");
        }
        else if(n.Contains("MusicBoxSilhouette"))
        {
            Skin(t,s,"TealDark"); EdgeBand(g,s,"Gold");
            for(int x=-1;x<=1;x++) Place("ClockworkMedallion",Art(t),new Vector3(x*3.5f,-.5f,-1.05f),Vector3.one*1.25f);
            Sign(Art(t),"THE TOYMAKER'S ATELIER",new Vector3(0,2.2f,-1.1f),9,.65f);
            Place("ToyKey",Art(t),new Vector3(0,s.y*.5f,0),Vector3.one*1.3f);
        }
        else Skin(t,s,n.Contains("Lever")?"Gold":"StoneWarm");
    }

    private static void TileTop(Transform g,Vector3 s,bool wooden,float tile)
    {
        int nx=Mathf.Max(1,Mathf.CeilToInt(s.x/tile)); int nz=Mathf.Max(1,Mathf.CeilToInt(s.z/(wooden?1.35f:tile)));
        float dx=s.x/nx,dz=s.z/nz;
        string[] colors=wooden?new[]{"Wood","WoodLight","WoodLight","Wood"}:new[]{"Stone","StoneLight","StoneWarm","StoneLight"};
        for(int x=0;x<nx;x++) for(int z=0;z<nz;z++)
            Part("Tile",g,new Vector3(-s.x*.5f+(x+.5f)*dx,s.y*.5f-.025f,-s.z*.5f+(z+.5f)*dz),
                new Vector3(dx-.045f,.07f,dz-.045f),colors[(x*13+z*7)%4]);
        EdgeBand(g,s,wooden?"WoodDark":"StoneLight");
    }

    private static void EdgeBand(Transform g,Vector3 s,string color)
    {
        float y=s.y*.5f-.05f;
        for(int side=-1;side<=1;side+=2)
        {
            Part("Rim",g,new Vector3(side*(s.x*.5f-.08f),y,0),new Vector3(.16f,.12f,s.z),color);
            Part("Rim",g,new Vector3(0,y,side*(s.z*.5f-.08f)),new Vector3(s.x,.12f,.16f),color);
        }
    }

    private static void Foundation(Transform g,Vector3 s,string color)
    {
        // Below the level, never a replacement for the original playable floor or fall volume.
        Part("FloatingPlinth",g,new Vector3(0,-1.8f,0),new Vector3(s.x-.15f,2.8f,s.z-.15f),color);
        Part("LowerMoulding",g,new Vector3(0,-3.15f,0),new Vector3(s.x+.05f,.3f,s.z+.05f),"Slate");
        for(int side=-1;side<=1;side+=2)
        {
            for(float x=-s.x*.5f+1;x<s.x*.5f;x+=3)
                Part("FoundationBlock",g,new Vector3(x,-1.65f,side*(s.z*.5f-.01f)),new Vector3(2.85f,2.4f,.12f),"StoneWarm");
            for(float z=-s.z*.5f+1;z<s.z*.5f;z+=3)
                Part("FoundationBlock",g,new Vector3(side*(s.x*.5f-.01f),-1.65f,z),new Vector3(.12f,2.4f,2.85f),"Stone");
        }
    }

    private static void WallSurface(Transform g,Vector3 s,bool wood,bool doll)
    {
        bool alongX=s.x>s.z; float length=alongX?s.x:s.z; float depth=alongX?s.z:s.x;
        int rows=Mathf.Max(1,Mathf.RoundToInt(s.y/(doll?3f:wood?1.5f:1.1f)));
        float height=s.y/rows;
        for(int side=-1;side<=1;side+=2)
        {
            Transform face=Node("WallFace",g,new Vector3(alongX?0:side*(depth*.5f-.035f),0,alongX?side*(depth*.5f-.035f):0));
            if(!alongX) face.localRotation=Quaternion.Euler(0,90,0);
            int cols=Mathf.Max(1,Mathf.CeilToInt(length/(doll?2.8f:wood?4f:2.2f)));
            float dx=length/cols;
            for(int y=0;y<rows;y++) for(int x=0;x<cols;x++)
            {
                string c=wood?((x+y)%3==0?"Wood":"WoodLight"):doll?(y%2==0?"Rose":"Ivory"):((x+y)%3==0?"StoneWarm":"Stone");
                Part("Panel",face,new Vector3(-length*.5f+(x+.5f)*dx,-s.y*.5f+(y+.5f)*height,0),new Vector3(dx-.055f,height-.055f,.13f),c);
            }
            for(int y=0;y<2;y++) Part("Moulding",face,new Vector3(0,(y==0?-1:1)*(s.y*.5f-.1f),side*.08f),new Vector3(length,.22f,.22f),wood?"WoodDark":"Ivory");
            if(doll) for(float x=-length*.5f+2;x<length*.5f;x+=4)
                Part("WallpaperStar",face,new Vector3(x,0,side*.095f),new Vector3(.55f,.55f,.055f),"Gold","Star");
        }
        Part("WallCap",g,new Vector3(0,s.y*.5f,0),new Vector3(s.x+.1f,.2f,s.z+.1f),wood?"WoodDark":"Ivory");
        if(!wood&&!doll && s.y>4)
        {
            for(float p=-length*.5f+.5f;p<length*.5f;p+=2)
                Part("Battlement",g,new Vector3(alongX?p:0,s.y*.5f+.42f,alongX?0:p),new Vector3(alongX?.85f:s.x,.75f,alongX?s.z:.85f),"StoneLight");
        }
    }

    private static void CornerBolts(Transform g,Vector3 s,string color)
    {
        for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
            Part("Bolt",g,new Vector3(x*s.x*.39f,s.y*.5f+.035f,z*s.z*.39f),new Vector3(.14f,.055f,.14f),color,"Cylinder");
    }
    private static void Chevrons(Transform g,float y,float length,float width,string color)
    {
        for(int i=-1;i<=1;i++) for(int side=-1;side<=1;side+=2)
            Part("Arrow",g,new Vector3(side*width*.22f,y,i*length*.24f),new Vector3(width*.7f,.025f,.11f),color,"Bevel",Quaternion.Euler(0,side*40,0));
    }

    private static void Sign(Transform parent,string text,Vector3 position,float width,float textHeight,Quaternion? rotation=null)
    {
        Transform sign=Node("Sign_"+text,parent,position); sign.localRotation=rotation??Quaternion.identity;
        Part("Frame",sign,Vector3.zero,new Vector3(width,textHeight*2,.16f),"Gold");
        Part("Enamel",sign,new Vector3(0,0,-.1f),new Vector3(width-.16f,textHeight*2-.13f,.08f),"TealDark");
        Label(sign,text,new Vector3(0,0,-.155f),textHeight,Color.white);
    }
    private static void Label(Transform parent,string text,Vector3 position,float size,Color color,Quaternion? rotation=null)
    {
        Transform t=Node("Lettering",parent,position); t.localRotation=rotation??Quaternion.identity;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=Lettering(text,size);
        MeshRenderer renderer=t.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial=Mat("Ivory");
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void DressMechanisms(Transform generated)
    {
        foreach(Portal p in generated.GetComponentsInChildren<Portal>(true))
        {
            foreach(Renderer r in p.GetComponentsInChildren<Renderer>()) r.enabled=false;
            Transform art=Art(p.transform);
            Place("PortalFrame",art,new Vector3(0,-2,0),new Vector3(1,.84f,1));
            Sign(art,p.action==Portal.PortalAction.Enable?"ROLL ON":"ROLL OFF",new Vector3(0,2.35f,-.2f),2.8f,.35f);
        }
        foreach(LiftPad p in generated.GetComponentsInChildren<LiftPad>(true))
        {
            BoxCollider b=p.GetComponent<BoxCollider>(); Skin(p.transform,b.size,"Gold");
            Transform g=Geometry(p.transform);
            Part("PressureFace",g,new Vector3(0,.14f,0),new Vector3(2.5f,.05f,2.5f),"TealDark");
            Part("WeightEmblem",g,new Vector3(0,.18f,0),new Vector3(1,1,.035f),"Gold","Star",Quaternion.Euler(90,0,0));
        }
        foreach(AccelPad p in generated.GetComponentsInChildren<AccelPad>(true))
        {
            BoxCollider b=p.GetComponent<BoxCollider>(); Skin(p.transform,b.size,"TealDark");
            Chevrons(Geometry(p.transform),b.size.y*.5f+.02f,b.size.z,1.2f,"TealLight");
        }
        foreach(ToyWorldRepairItem item in generated.GetComponentsInChildren<ToyWorldRepairItem>(true))
        {
            // Keep the existing collection visualRoot: its activation state is gameplay-owned.
            foreach(Renderer r in item.visualRoot.GetComponentsInChildren<Renderer>()) r.enabled=false;
            Transform g=Geometry(item.visualRoot.transform);
            string color=item.itemType==ToyWorldRepairItemType.WindUpSpring?"Gold":item.itemType==ToyWorldRepairItemType.PowerGear?"TealLight":"Rose";
            Part("CoreCage",g,Vector3.zero,new Vector3(1.45f,1.3f,1.45f),"Gold","Gear");
            Part("Core",g,Vector3.zero,new Vector3(1.05f,1.55f,1.05f),"Lime","Cylinder");
            for(int y=-1;y<=1;y+=2) Part("Band",g,new Vector3(0,y*.6f,0),new Vector3(1.3f,.18f,1.3f),color,"Cylinder");
            Part("Crest",g,new Vector3(0,0,-.75f),new Vector3(.65f,.65f,.1f),color,"Star");
            Place("CorePedestal",Art(item.transform),new Vector3(0,-1.15f,0),Vector3.one);
        }
        foreach(ThreadAnchor anchor in generated.GetComponentsInChildren<ThreadAnchor>(true))
        {
            // Anchor roots have non-unit scale: compensating only on the visual child preserves connectRange.
            Transform art=Art(anchor.transform); Vector3 scale=anchor.transform.lossyScale;
            art.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            Part("AnchorRing",Geometry(anchor.transform),Vector3.zero,new Vector3(1,1,.16f),"Gold","Ring");
        }
        foreach(ToyWorldInstallSocket socket in generated.GetComponentsInChildren<ToyWorldInstallSocket>(true))
        {
            Transform art=Art(socket.transform);
            Place("CorePedestal",art,new Vector3(0,-.25f,0),Vector3.one*1.1f);
            Label(art,((int)socket.itemType+1).ToString(),new Vector3(0,.36f,-.65f),.5f,Color.white,Quaternion.Euler(90,0,0));
        }
        foreach(RespawnZone cp in generated.GetComponentsInChildren<RespawnZone>(true))
        {
            Transform g=Geometry(cp.transform);
            Part("CheckpointMedal",g,new Vector3(-1.3f,-1.9f,0),new Vector3(1.2f,.16f,1.2f),"Gold","Cylinder");
            Part("CheckpointStar",g,new Vector3(-.65f,.4f,-.07f),new Vector3(.43f,.43f,.06f),"Ivory","Star");
        }
        HubProgressDisplay hub=generated.GetComponentInChildren<HubProgressDisplay>();
        for(int i=0;i<3;i++)
        {
            Transform slot=hub.itemSlots[i].transform.parent;
            Skin(slot,new Vector3(1.55f,.55f,1.55f),"Slate","Cylinder");
            Place("CorePedestal",Art(slot),new Vector3(0,-.3f,0),Vector3.one*1.1f);
            Label(Art(slot),new[]{"SPRING","GEAR","MELODY"}[i],new Vector3(0,.42f,-.55f),.23f,Color.white,Quaternion.Euler(90,0,0));
            Transform beacon=hub.branchBeacons[i].transform.parent;
            Skin(beacon,new Vector3(.8f,3.3f,.8f),"Teal","Cylinder");
            Transform bg=Geometry(beacon);
            Part("BeaconFoot",bg,new Vector3(0,-1.9f,0),new Vector3(1.25f,.2f,1.25f),"Slate","Cylinder");
            Part("BeaconCrown",bg,new Vector3(0,1.8f,0),new Vector3(1,.4f,1),"Gold","Cylinder");
        }
        Transform exit=generated.GetComponentInChildren<ToyWorldExitTrigger>().transform;
        exit.GetComponentInChildren<Renderer>().enabled=false;
        Place("PortalFrame",Art(exit),new Vector3(0,-1.5f,0),new Vector3(1.3f,1,1));
        Sign(Art(exit),"EXIT",new Vector3(0,3.6f,-.3f),3,.6f);
    }

    private static void DressLandmarks(Transform generated)
    {
        Transform areas=generated.Find("Areas");
        Transform toy=areas.Find("ToyBox_Entrance");
        Sign(Art(toy),"01 / TOY BOX",new Vector3(0,5.6f,-52.37f),10,1,Quaternion.Euler(0,180,0));
        for(int s=-1;s<=1;s+=2)
        {
            Place("ClockworkMedallion",Art(toy),new Vector3(s*9,4.7f,-52.35f),Vector3.one,Quaternion.Euler(0,180,0));
            Part("CornerBrace",Geometry(toy),new Vector3(s*13.55f,3,-52.35f),new Vector3(.4f,6,.5f),"Gold");
        }
        Sign(Art(toy),"LEAVE A LIGHT SHAPE ON THE GOLD PAD",new Vector3(-7,1.9f,-43.2f),5.5f,.25f,Quaternion.Euler(0,180,0));
        Transform fort=areas.Find("Branch_BlockFort");
        for(int z=7;z<=29;z+=22)
        {
            Place("CastleTurret",Art(fort),new Vector3(-37,5.5f,z),new Vector3(.85f,.8f,.85f));
            Place("ToyBanner",Art(fort),new Vector3(-37,8.2f,z),Vector3.one*.7f);
        }
        Sign(Art(fort),"03 / BLOCK FORT",new Vector3(-35.85f,3.7f,18),7,.65f,Quaternion.Euler(0,-90,0));
        // Perimeter edging is low and outside the existing playable floor, leaving bypasses open.
        for(float z=5;z<32;z+=3)
            Part("FortEdgeStone",Geometry(fort),new Vector3(-55.25f,.35f,z),new Vector3(.45f,.7f,2.8f),"StoneWarm");
        Transform train=areas.Find("Branch_TrainYard");
        Transform canyon=train.Find("VIS_TrainCanyonDanger");
        Skin(canyon,new Vector3(9,.4f,26),"Slate");
        for(int side=-1;side<=1;side+=2)
            for(float z=6;z<31;z+=1.4f)
                Part("GapWarning",Geometry(train),new Vector3(42+side*4.25f,.055f,z),new Vector3(.42f,.025f,.72f),"Coral","Bevel",Quaternion.Euler(0,side*25,0));
        for(float x=35;x<57;x+=1.1f)
            if(x<=40.5f||x>=44) Place("RailSleeper",Art(train),new Vector3(x,.05f,18),Vector3.one);
        Sign(Art(train),"04 / CLOCKWORK YARD",new Vector3(31,4.1f,30.8f),10,.7f);
        for(int x=0;x<2;x++)
        {
            float px=x==0?30:56;
            Part("CablePost",Geometry(train),new Vector3(px,4.5f,30.6f),new Vector3(.3f,9,.3f),"WoodDark");
            Part("Finial",Geometry(train),new Vector3(px,9,30.6f),new Vector3(.65f,.65f,.65f),"Gold","Cylinder");
            if(x==0) Cable(Geometry(train),new Vector3(px,8.9f,30.6f),new Vector3(56,8.9f,30.6f));
        }
        for(int x=0;x<3;x++) Place("ToyCrate",Art(train),new Vector3(26+x*1.8f,0,30),Vector3.one*.65f);
        Transform doll=areas.Find("Branch_DollHouse");
        Sign(Art(doll),"05 / DOLL HOUSE",new Vector3(34,21.1f,-45.4f),14,1,Quaternion.Euler(0,180,0));
        // Furniture is recessed into the back wall. It never occupies a traversal landing.
        Place("Bookcase",Art(doll),new Vector3(26,6,-44.8f),new Vector3(1.25f,1.2f,.55f),Quaternion.Euler(0,180,0));
        Place("DollDresser",Art(doll),new Vector3(41,6,-44.8f),new Vector3(1.1f,1,.55f),Quaternion.Euler(0,180,0));
        for(int i=0;i<2;i++) Place("MobileStar",Art(doll),new Vector3(34,10+i*7,-34),Vector3.one*1.1f);
        for(int side=-1;side<=1;side+=2)
        {
            float x=34+side*11.6f;
            Part("DollCornerPost",Geometry(doll),new Vector3(x,10,-45.5f),new Vector3(.38f,20,.38f),"WoodDark");
            for(int level=0;level<3;level++) Window(Geometry(doll),new Vector3(x-side*.18f,3.2f+level*6,-38),Quaternion.Euler(0,side*90,0));
        }
        for(int i=0;i<12;i++)
            Part("RoofScallop",Geometry(doll),new Vector3(23+i*2,20.4f,-46),new Vector3(2.05f,.5f,1.1f),i%2==0?"Rose":"Ivory");
        Transform plaza=areas.Find("ToyPlaza_Hub");
        Transform pg=Geometry(plaza);
        Part("PlazaRing",pg,new Vector3(0,.026f,0),new Vector3(23,23,.025f),"TealDark","Ring",Quaternion.Euler(90,0,0));
        // Thin inset, not a new central obstacle.
        Part("Compass",pg,new Vector3(0,.05f,-3.4f),new Vector3(4,4,.02f),"Gold","Star",Quaternion.Euler(90,0,0));
        Sign(Art(plaza),"02 / TOY PLAZA",new Vector3(0,.06f,-7),8,.55f,Quaternion.Euler(90,0,0));
        for(int i=0;i<3;i++)
        {
            Vector3 p=new[]{new Vector3(-12,3,5),new Vector3(12,3,5),new Vector3(10,3,-12)}[i];
            Sign(Art(plaza),new[]{"FORT / SPRING","YARD / GEAR","HOUSE / MELODY"}[i],p,4.3f,.3f);
        }
        Transform final=areas.Find("Final_BrokenMusicBox");
        Sign(Art(final),"06 / THE BROKEN MUSIC BOX",new Vector3(0,9.3f,27.5f),17,1);
        for(int s=-1;s<=1;s+=2)
        {
            Place("ClockworkMedallion",Art(final),new Vector3(s*9,4.4f,26.65f),Vector3.one*2.5f);
            for(int j=0;j<3;j++)
                Place("ClockworkMedallion",Art(final),new Vector3(s*14.4f,3+j*2.8f,42+j*3),Vector3.one*(1.3f+j*.3f),Quaternion.Euler(0,s*90,0));
            Place("ToyKey",Art(final),new Vector3(s*10,8.7f,27.5f),Vector3.one*1.25f);
        }
        Sign(Art(final),"ALL THREE PARTS REQUIRED",new Vector3(0,6.6f,26.6f),8,.45f);
        Sign(Art(final),"1 SPRING   >   2 GEAR   >   3 MELODY",new Vector3(0,4.9f,35.5f),12,.5f);
        // Backdrop/pipes are on existing walls; no fake deployed staircase or new wind-up logic.
        for(int i=0;i<7;i++)
            Part("OrganPipe",Geometry(final),new Vector3(-14.35f,2.5f+i*.35f,38+i*1.6f),new Vector3(.35f,4+i*.7f,.45f),i%2==0?"Gold":"Teal","Cylinder");
    }

    private static void Window(Transform g,Vector3 pos,Quaternion rotation)
    {
        Transform w=Node("PaintedWindow",g,pos); w.localRotation=rotation;
        Part("Frame",w,Vector3.zero,new Vector3(2.5f,3,.12f),"WoodDark");
        Part("Glass",w,new Vector3(0,0,-.08f),new Vector3(2.15f,2.65f,.06f),"Navy");
        Part("Mullion",w,new Vector3(0,0,-.14f),new Vector3(.12f,2.7f,.09f),"Ivory");
        Part("Crossbar",w,new Vector3(0,0,-.14f),new Vector3(2.2f,.12f,.09f),"Ivory");
    }
    private static void Cable(Transform g,Vector3 a,Vector3 b)
    {
        for(int i=0;i<12;i++)
        {
            float u=i/12f,v=(i+1)/12f;
            Vector3 p=Vector3.Lerp(a,b,u)+Vector3.down*(Mathf.Sin(u*Mathf.PI)*1.3f);
            Vector3 q=Vector3.Lerp(a,b,v)+Vector3.down*(Mathf.Sin(v*Mathf.PI)*1.3f);
            Part("Cable",g,(p+q)*.5f,new Vector3(.045f,.045f,Vector3.Distance(p,q)),"Slate","Bevel",Quaternion.LookRotation(q-p));
        }
    }
    private static void DressLighting(Transform generated)
    {
        foreach(Light l in generated.GetComponentsInChildren<Light>())
        {
            if(l.type!=LightType.Directional) continue;
            l.transform.rotation=Quaternion.Euler(48,-28,0); l.intensity=1.18f;
            l.color=new Color(1,.94f,.82f); l.shadowStrength=.65f; l.shadowBias=.035f;
        }
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.64f,.73f,.78f);
        RenderSettings.ambientEquatorColor=new Color(.48f,.52f,.54f);
        RenderSettings.ambientGroundColor=new Color(.32f,.3f,.32f);
        UnityEngine.Rendering.SphericalHarmonicsL2 ambient=new UnityEngine.Rendering.SphericalHarmonicsL2();
        ambient.AddAmbientLight(new Color(.42f,.46f,.49f));
        RenderSettings.ambientProbe=ambient;
        RenderSettings.fog=false; RenderSettings.skybox=null;
        foreach(Camera camera in generated.GetComponentsInChildren<Camera>())
        { camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.72f,.78f,.77f); }
    }

    public static string PhysicsSignature(Transform generated)
    {
        // Full serialized physics components and their world transforms, sorted independently of art children.
        List<string> records=new List<string>();
        foreach(Component c in generated.GetComponentsInChildren<Component>(true))
        {
            if(!(c is Collider)&&!(c is Rigidbody)&&!(c is Joint)) continue;
            Transform t=c.transform;
            string path=t.name; for(Transform p=t.parent;p!=null;p=p.parent) path=p.name+"/"+path;
            records.Add(path+"|"+c.GetType().Name+"|"+t.position.ToString("R")+"|"+t.rotation.ToString("R")+
                "|"+t.lossyScale.ToString("R")+"|"+EditorJsonUtility.ToJson(c));
        }
        records.Sort(StringComparer.Ordinal);
        return Hash128.Compute(string.Join("\n",records)).ToString();
    }
}
