using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BBI.Core;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Network;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Commands;
using BBI.Game.Data;
using BBI.Game.Network;
using BBI.Game.Replay;
using BBI.Game.Simulation;
using BBI.Game.Utility;
using BBI.Unity.Core.DLC;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.DLC;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.Utility;
using BBI.Unity.Game.World;
using UnityEngine;

[assembly: AssemblyVersion("1.0.0.0")]
namespace KhaanephCruiser.Testing
{
    // Local, disposable offline film driver. Never part of the release DLLs.
    public static class LiveProbe
    {
        static volatile int Stage;
        static int ResearchStep, Frames, PriorPhase;
        static volatile int Phase;
        static volatile bool Failed, Finished;
        static bool ResearchPending, ProductionPending, MoveIssued, AttackIssued;
        static float Deadline, NextCapture;
        static uint StartFrame, ProductionFrame, MoveFrame, AttackFrame;
        static object Commander, Carrier, Cruiser, Enemy;
        static Vector2r MoveStart, MoveDirection;
        static Entity CarrierID, CruiserID, EnemyID;
        static UnitFactionDLCPack KPack;
        static DLCPackID KID;
        static LevelManager Levels;
        static DLCManager DLC;
        static Camera FilmCamera;
        static RenderTexture FilmTarget;
        static Texture2D FilmImage;
        static Vector3 CameraCenter;
        static string Output;
        static readonly string[] Research = {"Tier_1_G_Sandskimmers", "Tier_1_G_Catamaran", Runtime.Fabrication};

        static string Root { get { return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(LiveProbe).Assembly.Location)), "KhaanephCruiser.Showcase"); } }
        static void Log(string text) { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root,"showcase.log"),DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine); }
        public static void ClipLoaded(string path, object clip) { }
        public static void Tick(object main)
        {
            if (Failed) { Application.Quit(); return; }
            try
            {
                if (Stage==0)
                {
                    Application.runInBackground=true;
                    if ((int)R.Get(typeof(Runtime),"Registration")<1) return;
                    Levels=(LevelManager)R.Get(main,"mLevelManager");
                    DLC=(DLCManager)R.Get(typeof(ShipbreakersMain),"sDLCManager");
                    if(Levels==null||DLC==null)return;
                    Output=Path.Combine(Root,"frames");Directory.CreateDirectory(Output);
                    File.WriteAllText(Path.Combine(Root,"frames.csv"),"frame,phase,realtime\n");
                    Deadline=Time.realtimeSinceStartup+150;Stage=1;Log("Waiting for native preview initialization");
                }
                if(Stage==1)
                {
                    if(Time.realtimeSinceStartup>Deadline)throw new Exception("Preview initialization timed out");
                    object panel=R.Get(typeof(PlayerCustomizationPanel),"sPanel");if(panel==null)return;
                    object content=R.Get(panel,"mContentManager");if(content==null)return;
                    object scene=R.Get(content,"Scene"), dlc=R.Get(content,"DLC");if(scene==null||dlc==null)return;
                    if(!(bool)R.Get(scene,"IsLoaded")||!(bool)R.Get(dlc,"IsLoaded")||R.Get(scene,"mLoadingCoroutine")!=null)return;
                    DLCManager preview=(DLCManager)R.Get(dlc,"Manager");
                    foreach(DLCAssetBundleBase header in preview.GetAllLoadedHeadersOfType(DLCType.Faction))
                        if(header.name=="DLC0002_UnitFactionDLCPack")KPack=(UnitFactionDLCPack)header;
                    if(KPack==null)throw new Exception("Khaaneph preview pack missing");
                    KID=KPack.DLCApplicationID;
                    if(!preview.IsPackOwned(KID))throw new Exception("Khaaneph DLC is not owned");
                    PlayerCustomizationPanel.UnloadPreviewContent();DLC.LoadDLC(KID);
                    Stage=2;Deadline=Time.realtimeSinceStartup+90;return;
                }
                if(Stage==2)
                {
                    if(Time.realtimeSinceStartup>Deadline)throw new Exception("Match DLC load timed out");
                    if(!DLC.IsPackLoaded(KID))return;
                    KPack=(UnitFactionDLCPack)DLC.GetDLCPackHeader(KID);
                    StartMatch();Stage=3;Deadline=Time.realtimeSinceStartup+240;return;
                }
                if(Stage==3)
                {
                    if(Time.realtimeSinceStartup>Deadline)throw new Exception("Showcase scenario timed out");
                    if(Levels.ManagerState!=LevelManager.State.Game)return;
                    ((SimController)R.Get(main,"mSimController")).TimeScale=Fixed64.FromInt(Phase<2?8:1);
                    if(Phase>0 && Time.realtimeSinceStartup>=NextCapture && !Finished)
                    {
                        Capture();NextCapture=Time.realtimeSinceStartup+1f/16f;
                    }
                    if(Finished)
                    {
                        Log("CAPTURE COMPLETE frames="+Frames+"; native production, movement and damage observed");
                        Stage=99;Application.Quit();
                    }
                }
            }
            catch(Exception ex){Failed=true;Log("FAIL main: "+ex);}
        }

        static void StartMatch()
        {
            NullSession session=new NullSession();
            List<CommanderDescription> players=new List<CommanderDescription>();
            players.Add(new CommanderDescription("Khaaneph",new CommanderID(1),session.LocalPlayerID,0,(int)KPack.CustomizesFaction,new TeamID(1),PlayerType.Human,UnitColors.kDefault,Difficulty.None,KID,false));
            players.Add(new CommanderDescription("Target",new CommanderID(2),NetworkPlayerID.kInvalidID,1,0,new TeamID(2),PlayerType.AI,UnitColors.kDefault,Difficulty.None,DLCPackID.kInvalidID,false));
            LevelDefinition map=null;
            foreach(LevelDefinition item in Levels.LevelEntriesMP)if(item!=null&&item.MaxPlayers>=2){map=item;break;}
            if(map==null)throw new Exception("No native skirmish map");
            Dictionary<CommanderID,TeamID> teams=new Dictionary<CommanderID,TeamID>();
            teams.Add(new CommanderID(1),new TeamID(1));teams.Add(new CommanderID(2),new TeamID(2));
            List<CommanderSpawnInfo> spawns;
            if(!SpawnAssigner.GenerateSpawnPoints(map,teams,TeamSetting.FFA,out spawns))throw new Exception("No spawn points");
            Dictionary<CommanderID,PlayerSelection> selections=SpawnAssigner.GeneratePlayerSelections(Levels.CommanderAttributes,players,spawns,DLC);
            GameStartSettings settings=new GameStartSettings(selections,VictoryConditions.CarrierAnnihilation,TeamSetting.FFA,GameModeSettings.kDefaultSettings,3650,true,false,AutomatchSizeType.ThreeVSThree);
            Levels.StartLoadLevel(GameMode.AISkirmish,ReplayMode.ReplaysDisabled,map,new DependencyContainer<GameStartSettings,SessionBase>(settings,session));
            Log("Loading native offline match: "+map.SceneName);
        }

        public static void SimTick(object sim)
        {
            if(Failed||Finished||Stage!=3)return;
            try
            {
                uint frame=Convert.ToUInt32(R.Get(R.Get(sim,"GlobalFrameCount"),"FrameNumber"));if(frame<5)return;
                if(Commander==null)
                {
                    Commander=R.Call(R.Get(sim,"CommanderManager"),"GetCommanderFromID",new CommanderID(1));
                    if(Commander==null)return;
                    object storage=R.Get(Commander,"Storage");R.Call(storage,"SetCurrentAmount","Resource1",30000);R.Call(storage,"SetCurrentAmount","Resource2",30000);
                    Carrier=Find(sim,"K_Carrier_MP");CarrierID=(Entity)R.Get(Carrier,"Entity");
                    object fow=Create("BBI.Game.Commands.ToggleFogOfWarCommand",new object[]{new CommanderID(1),true});Run(fow,sim);
                    Log("Offline staging: resources supplied, fog disabled; native research and fabrication used");
                }
                if(ResearchStep<Research.Length)
                {
                    string name=Research[ResearchStep];
                    if(!ResearchPending)
                    {
                        object attributes=((EntityTypeCollection)R.Get(R.Get(sim,"Settings"),"EntityTypes")).GetEntityType(name).Get<ResearchItemAttributes>();
                        if(!(bool)R.Call(Commander,"StartResearch",attributes))throw new Exception("Research rejected: "+name);
                        ResearchPending=true;Log("Research started "+name);return;
                    }
                    if(((IList)R.Get(Commander,"CompletedResearch")).Contains(name))
                    {ResearchStep++;ResearchPending=false;Log("Research completed "+name);}
                    return;
                }
                if(!ProductionPending)
                {
                    Type add=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.SelectedUnitManager+AddMode",true);
                    object command=Create("BBI.Game.Commands.UseAbilityCommand",new object[]{new CommanderID(1),"Ability_Produce_K_HonorGuard",AbilityAction.Activate,new Entity[]{CarrierID},null,Vector2r.Zero,AbilityTargetingType.Self,Enum.Parse(add,"NewSet"),false});
                    Run(command,sim);ProductionPending=true;StartFrame=frame;Phase=1;
                    Log("BUILD: carrier production queued, 8x simulation time");return;
                }
                if(Cruiser==null)
                {
                    object candidate=Find(sim,Runtime.UnitKey);
                    if(candidate==null||!(bool)R.Get(candidate,"HasBeenSpawnedIntoWorldBefore"))return;
                    Cruiser=candidate;CruiserID=(Entity)R.Get(Cruiser,"Entity");ProductionFrame=frame;Phase=2;
                    Log("BUILD COMPLETE: native produced cruiser, HP="+R.Get(Component(sim,CruiserID,7,"BBI.Game.Simulation.Health"),"MaxHealth")+"; production sim frames="+(frame-StartFrame));
                }
                if(!MoveIssued)
                {
                    if(frame-ProductionFrame<24)return;
                    MoveStart=Position(sim,CruiserID);
                    object other=Find(sim,"C_Carrier_MP");
                    MoveDirection=(Position(sim,(Entity)R.Get(other,"Entity"))-MoveStart).Normalized;
                    Vector2r destination=MoveStart+Fixed64.FromInt(440)*MoveDirection;
                    object move=Create("BBI.Game.Commands.UnitGroupMoveToCommand",new object[]{new CommanderID(1),UnitGroupMoveType.GroupMove,new Entity[]{CruiserID},destination,false,false});
                    Run(move,sim);MoveIssued=true;MoveFrame=frame;Log("MOVE: native movement command accepted");return;
                }
                if(!AttackIssued)
                {
                    if(frame-MoveFrame<72)return;
                    Fixed64 distance=(Position(sim,CruiserID)-MoveStart).Length;
                    Log("MOVE COMPLETE: displacement="+distance);
                    if(distance<Fixed64.FromInt(40))throw new Exception("Cruiser did not move far enough");
                    Vector2r target=Position(sim,CruiserID)+Fixed64.FromInt(240)*MoveDirection;
                    object spawn=Create("BBI.Game.Commands.CreateNewUnitsCommand",new object[]{new CommanderID(2),new string[]{"C_SupportCruiser_MP"},null,target,Orientation2.LocalForward});
                    Run(spawn,sim);Enemy=Find(sim,"C_SupportCruiser_MP");if(Enemy==null)throw new Exception("Target unit did not spawn");
                    EnemyID=(Entity)R.Get(Enemy,"Entity");object health=Component(sim,EnemyID,7,"BBI.Game.Simulation.Health");
                    R.Set(health,"MaxHealth",20000);R.Set(health,"CurrentHealth",20000);
                    Type add=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.SelectedUnitManager+AddMode",true);
                    object attack=Create("BBI.Game.Commands.AssignTargetCommand",new object[]{new CommanderID(1),new Entity[]{CruiserID},new Entity[]{EnemyID},Enum.Parse(add,"NewSet"),true});
                    Run(attack,sim);AttackIssued=true;AttackFrame=frame;Phase=3;
                    Log("FIRE: native attack command accepted; durable offline target staged");return;
                }
                if(frame-AttackFrame>=80)
                {
                    int remaining=(int)R.Get(Component(sim,EnemyID,7,"BBI.Game.Simulation.Health"),"CurrentHealth");
                    if(remaining>=20000)throw new Exception("No native weapon damage was observed");
                    Log("FIRE COMPLETE: target damage="+(20000-remaining));Finished=true;
                }
            }
            catch(Exception ex){Failed=true;Log("FAIL simulation: "+ex);}
        }

        static void Capture()
        {
            UnitView actor=null,target=null;
            Entity wanted=Phase==1?CarrierID:CruiserID;
            foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
            {if(view.EntityID==wanted)actor=view;if(AttackIssued&&view.EntityID==EnemyID)target=view;}
            if(actor==null||Camera.main==null)return;
            if(FilmCamera==null)
            {
                FilmCamera=new GameObject("Anonymous showcase camera").AddComponent<Camera>();FilmCamera.CopyFrom(Camera.main);FilmCamera.enabled=false;
                FilmCamera.nearClipPlane=2;FilmCamera.farClipPlane=10000;FilmCamera.fieldOfView=42;FilmCamera.orthographic=false;
                FilmTarget=new RenderTexture(960,540,24);FilmCamera.targetTexture=FilmTarget;
                FilmImage=new Texture2D(960,540,TextureFormat.RGB24,false);CameraCenter=actor.Position;
            }
            Vector3 aim=actor.Position+Vector3.up*12;
            if(Phase==3&&target!=null)aim=Vector3.Lerp(aim,target.Position,.32f);
            if(PriorPhase!=Phase){CameraCenter=aim;PriorPhase=Phase;Log("CAMERA phase="+Phase+" start frame="+Frames);}
            CameraCenter=Vector3.Lerp(CameraCenter,aim,.2f);
            Vector3 offset=Phase==1?new Vector3(-420,330,470):new Vector3(-190,175,240);
            FilmCamera.transform.position=CameraCenter+offset;FilmCamera.transform.LookAt(CameraCenter);
            Camera.main.transform.position=FilmCamera.transform.position;Camera.main.transform.rotation=FilmCamera.transform.rotation;
            LODGroup lod=actor.GetComponent<LODGroup>();if(lod!=null)lod.ForceLOD(0);
            FilmCamera.Render();RenderTexture before=RenderTexture.active;RenderTexture.active=FilmTarget;
            FilmImage.ReadPixels(new Rect(0,0,960,540),0,0);FilmImage.Apply();RenderTexture.active=before;
            File.WriteAllBytes(Path.Combine(Output,"frame-"+Frames.ToString("D5")+".jpg"),FilmImage.EncodeToJPG(87));
            File.AppendAllText(Path.Combine(Root,"frames.csv"),Frames+","+Phase+","+Time.realtimeSinceStartup.ToString("F4",CultureInfo.InvariantCulture)+"\n");Frames++;
        }

        static object Find(object sim,string name)
        {foreach(object unit in (IEnumerable)R.Get(R.Get(sim,"UnitManager"),"Units"))if((string)R.Get(unit,"TypeName")==name)return unit;return null;}
        static Vector2r Position(object sim,Entity entity){return (Vector2r)R.Get(Component(sim,entity,10,"BBI.Game.Simulation.Position"),"Position2D");}
        static object Component(object sim,Entity entity,int id,string name)
        {
            object system=R.Get(sim,"EntitySystem");Type type=typeof(Sim).Assembly.GetType(name,true);
            foreach(MethodInfo method in system.GetType().GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
                if(method.Name=="GetComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2)return method.MakeGenericMethod(type).Invoke(system,new object[]{entity,id});
            throw new Exception("Component getter missing");
        }
        static object Create(string name,object[] args)
        {
            Type type=typeof(Sim).Assembly.GetType(name,true);
            foreach(ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance))
            {
                ParameterInfo[] p=ctor.GetParameters();if(p.Length!=args.Length)continue;bool match=true;
                for(int i=0;i<p.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))match=false;
                if(match)return ctor.Invoke(args);
            }
            throw new Exception("Constructor missing: "+name);
        }
        static void Run(object command,object sim)
        {if(!(bool)R.Call(command,"RunCommand",sim,R.Get(sim,"InteractionProvider"),R.Get(sim,"GlobalFrameCount")))throw new Exception("Native command rejected");}
    }
    static class R
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
        public static object Get(object value,string name)
        {
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            for(Type t=type;t!=null;t=t.BaseType){FieldInfo f=t.GetField(name,Flags);if(f!=null)return f.GetValue(instance);PropertyInfo p=t.GetProperty(name,Flags);if(p!=null)return p.GetValue(instance,null);}
            throw new MissingMemberException(type.FullName,name);
        }
        public static void Set(object value,string name,object replacement)
        {
            for(Type t=value.GetType();t!=null;t=t.BaseType){FieldInfo f=t.GetField(name,Flags);if(f!=null){f.SetValue(value,replacement);return;}PropertyInfo p=t.GetProperty(name,Flags);if(p!=null){p.SetValue(value,replacement,null);return;}}
            throw new MissingMemberException(value.GetType().FullName,name);
        }
        public static object Call(object value,string name,params object[] args)
        {
            Type type=value as Type??value.GetType();object instance=value is Type?null:value;
            for(Type t=type;t!=null;t=t.BaseType)foreach(MethodInfo m in t.GetMethods(Flags))
            {ParameterInfo[] p=m.GetParameters();if(m.Name!=name||m.IsGenericMethodDefinition||p.Length!=args.Length)continue;bool match=true;for(int i=0;i<p.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))match=false;if(match)return m.Invoke(instance,args);}
            throw new MissingMethodException(type.FullName,name);
        }
    }
}
