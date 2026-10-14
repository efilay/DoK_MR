using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Network;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
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
    // A temporary offline test driver. Main-thread work uses native scene APIs;
    // simulation work runs on the simulation thread at a native tick boundary.
    public static class LiveProbe
    {
        static readonly object LogMutex = new object();
        static volatile bool Failed, SimDone;
        static readonly HashSet<string> HUDResearch = new HashSet<string>();
        static bool HUDOpened, TooltipChecked, VoiceChecked, ModelChecked, SelectionRequested, TooltipRequested;
        static float VoiceDeadline;
        static bool ResearchVoiceChecked, AntiAirVoiceChecked, ModelGeometryChecked;
        static readonly HashSet<string> PlayedAssaultClips = new HashSet<string>();
        static volatile int Stage;
        static int SimStage, Round;
        static float Deadline;
        static uint LastLogFrame, DamageFrame;
        static object Commander, Cruiser, Health;
        static string PendingResearch;
        static int DamagedHP;
        static UnitFactionDLCPack KPack, SPack;
        static DLCPackID KID, SID;
        static LevelManager Levels;
        static DLCManager DLC;
        static GameObject AnimationInstance;
        static Animator AnimationDriver;
        static SkinnedMeshRenderer AnimationCore;
        static Mesh AnimationBake;
        static Vector3[] AnimationBefore;
        static Quaternion SteeringBefore;
        static int AnimationPhase;
        static float AnimationDeadline;
        static readonly string[] ResearchChain = { "Tier_1_G_Sandskimmers", "Tier_1_G_Catamaran", Runtime.Fabrication, Runtime.AntiAir };

        public static void Tick(object main)
        {
            if (Failed) { Application.Quit(); return; }
            if (Stage == 99) return;
            try
            {
                if (Stage == 0)
                {
                    if ((int)R.Get(typeof(Runtime), "Registration") < 1) return;
                    Levels = (LevelManager)R.Get(main, "mLevelManager");
                    DLC = (DLCManager)R.Get(typeof(ShipbreakersMain), "sDLCManager");
                    if (Levels == null || DLC == null) return;
                    Deadline = Time.realtimeSinceStartup + 90;
                    Log("Started live probe; waiting for native preview content"); Stage = 1;
                }
                if (Stage == 1 || Stage == 6)
                {
                    if (Time.realtimeSinceStartup > Deadline) throw new Exception("Native preview did not finish loading");
                    PlayerCustomizationPanel panel = (PlayerCustomizationPanel)R.Get(typeof(PlayerCustomizationPanel), "sPanel");
                    object content = R.Get(panel, "mContentManager"), scene = R.Get(content, "Scene"), dlc = R.Get(content, "DLC");
                    if (!(bool)R.Get(scene, "IsLoaded") || !(bool)R.Get(dlc, "IsLoaded") || R.Get(scene, "mLoadingCoroutine") != null) return;
                    Camera previewCamera = (Camera)R.Get(scene, "mPreviewCamera");
                    Check(previewCamera != null && previewCamera.targetTexture != null, "Native preview camera has a render target");
                    DLCManager previewDLC = (DLCManager)R.Get(dlc, "Manager");
                    KPack = SPack = null;
                    foreach (DLCAssetBundleBase header in previewDLC.GetAllLoadedHeadersOfType(DLCType.Faction))
                    {
                        UnitFactionDLCPack pack = header as UnitFactionDLCPack;
                        if (pack != null && pack.name == "DLC0002_UnitFactionDLCPack") KPack = pack;
                        if (pack != null && pack.name == "DLC0001_UnitFactionDLCPack") SPack = pack;
                    }
                    Check(KPack != null && previewDLC.IsPackOwned(KPack.DLCApplicationID), "Native Khaaneph entitlement");
                    KID = KPack.DLCApplicationID; SID = SPack == null ? DLCPackID.kInvalidID : SPack.DLCApplicationID;
                    R.Call(scene, "SetModelIndex", KPack.CustomizesFaction);
                    R.Call(scene, "SetFactionPack", KPack);
                    UnitAssetContainer unit = PackUnit(KPack, Runtime.UnitKey);
                    object view = R.Get(R.Get(unit, "m_View"), "Data");
                    GameObject model = ((UnitViewAttributes)view).ModelPrefab;
                    bool found = false;
                    foreach (GameObject preview in (IEnumerable)R.Get(scene, "mInstancedPreviews")) if (preview.name == model.name + "(Clone)") found = true;
                    Check(found && ((UnitAttributes)R.Get(R.Get(unit, "m_Attributes"), "Data")).MaxHealth == 3650, "Actual preview instantiates Assault model with current unit data");
                    foreach(GameObject preview in (IEnumerable)R.Get(scene,"mInstancedPreviews"))
                        if(preview.name==model.name+"(Clone)") CaptureModel(preview,previewCamera,"preview-round-"+Round);
                    if (!ModelGeometryChecked) AuditModelAPI(model);
                    Log("Preview round " + Round + " PASS: K instance=" + unit.GetInstanceID() + "; instantiated model=" + model.name + "; native camera render target present");
                    if (Stage == 6 && Round == 3)
                    {
                        Check(TooltipChecked && VoiceChecked && ResearchVoiceChecked && AntiAirVoiceChecked && ModelChecked, "Native tooltip, recorded unit/fabrication/AA speech and custom model API checks completed");
                        AuditCampaignRegistration(main);
                        Log("PASS: two Khaaneph match cycles, native HUD/AA/repair, no-Khaaneph match, preview reloads and campaign registration restoration");
                        Stage = 99; Application.Quit(); return;
                    }
                    PlayerCustomizationPanel.UnloadPreviewContent();
                    Deadline = Time.realtimeSinceStartup + 90; Stage = 2; return;
                }
                if (Stage == 2)
                {
                    if (Round < 2) { Check(DLC.IsPackOwned(KID), "Match Khaaneph entitlement"); DLC.LoadDLC(KID); }
                    if (Round == 1 && SID != DLCPackID.kInvalidID)
                    {
                        Check(DLC.IsPackOwned(SID), "Match Soban entitlement"); DLC.LoadDLC(SID);
                    }
                    Stage = 3; return;
                }
                if (Stage == 3)
                {
                    if (Time.realtimeSinceStartup > Deadline) throw new Exception("Native match DLC loading timed out");
                    if ((Round < 2 && !DLC.IsPackLoaded(KID)) || (Round == 1 && SID != DLCPackID.kInvalidID && !DLC.IsPackLoaded(SID))) return;
                    if (Round < 2) KPack = (UnitFactionDLCPack)DLC.GetDLCPackHeader(KID);
                    if (Round == 1 && SID != DLCPackID.kInvalidID) SPack = (UnitFactionDLCPack)DLC.GetDLCPackHeader(SID);
                    StartMatch(); Deadline = Time.realtimeSinceStartup + 90; Stage = 4; return;
                }
                if (Stage == 4)
                {
                    if (Time.realtimeSinceStartup > Deadline && Levels.ManagerState != LevelManager.State.Game) throw new Exception("Native skirmish failed to start");
                    if (Levels.ManagerState != LevelManager.State.Game) return;
                    SimController controller = (SimController)R.Get(main, "mSimController");
                    controller.TimeScale = Fixed64.FromInt(4);
                    if (Round < 2) { AuditResearchHUD(); if (!TooltipChecked) AuditTooltip(); }
                    if (Round < 2 && Cruiser != null)
                    {
                        if (!VoiceChecked) AuditVoice(main);
                        if (!ModelChecked) AuditGameAnimation();
                    }
                    if (Round < 2 && !ModelChecked) return;
                    if (!SimDone) return;
                    if (Round < 2) foreach (string research in ResearchChain) Check(HUDResearch.Contains(research), "Native HUD created research button: " + research);
                    Log("Simulation round " + Round + " PASS; returning through native level unload");
                    Levels.StartExitLevel(); Stage = 5; Deadline = Time.realtimeSinceStartup + 90; return;
                }
                if (Stage == 5)
                {
                    if (Levels.ManagerState != LevelManager.State.Startup)
                    {
                        if (Time.realtimeSinceStartup > Deadline) throw new Exception("Native return to menu timed out: " + Levels.ManagerState); return;
                    }
                    Round++; SimStage = 0; SimDone = false; Commander = Cruiser = Health = null; PendingResearch = null;
                    HUDResearch.Clear(); HUDOpened = false;
                    // LevelManager.UnloadLevel(ExitGame) already requests preview loading before
                    // returning to Startup. A second request here starts duplicate scene coroutines.
                    Stage = 6; Deadline = Time.realtimeSinceStartup + 90;
                }
            }
            catch (Exception ex) { Fail("main stage " + Stage, ex); }
        }

        static UnitAssetContainer PackUnit(UnitFactionDLCPack pack, string key)
        {
            foreach (UnitFactionDLCPack.ReplacementMapping item in pack.NewEntities) if (item.NewEntityType.name == key) return (UnitAssetContainer)item.NewEntityType;
            throw new Exception("Pack unit missing " + key);
        }
        static void AuditTooltip()
        {
            object view = ShipbreakersMain.GetEntityTypeAttributes<AbilityViewAttributes>("Ability_Produce_K_HonorGuard");
            if (view == null) return;
            string key = (string)R.Get(view, "LocalizedLongDescriptionStringID");
            if (!TooltipRequested)
            {
                AbilityViewAttributes attributes = (AbilityViewAttributes)view;
                CommandPanelController.AbilityTooltipData data = new CommandPanelController.AbilityTooltipData();
                data.LocalizedTitleStringID = attributes.LocalizedTitleStringID;
                data.LocalizedShortDescriptionStringID = attributes.LocalizedShortDescriptionStringID;
                data.LocalizedLongDescriptionStringID = key;
                UISystem.ShowTooltip(new object[] { data }, attributes.Tooltip, null, null);
                TooltipRequested = true;
            }
            foreach (TooltipController controller in Resources.FindObjectsOfTypeAll(typeof(TooltipController)))
            {
                object loc = R.Get(controller, "mLocMan");
                UILabel label = (UILabel)R.Get(controller, "m_LongDescLabel");
                if (loc == null || label == null || !controller.gameObject.activeInHierarchy) continue;
                Check(label.gameObject.activeSelf && label.text.Contains("48 HP") && label.text.Contains("10 seconds") && !label.text.Contains("DOK_TRANSFER"), "Native production tooltip label translates the real description token");
                Log("Native tooltip PASS: actual UILabel text=" + label.text);
                foreach (TooltipManager manager in Resources.FindObjectsOfTypeAll(typeof(TooltipManager)))
                    if (object.ReferenceEquals(R.Get(manager, "mActiveTooltip"), controller)) manager.HideTooltip();
                TooltipChecked = true; return;
            }
        }
        static void AuditVoice(object main)
        {
            if (ShipbreakersMain.CurrentSimFrame == null) return;
            object voice = R.Get(main, "mVOController");
            Check((string)R.Get(R.Get(ShipbreakersMain.GetEntityTypeAttributes<UnitAudioAttributes>(Runtime.UnitKey), "AudioHookups"), "UnitCode") == Runtime.SpeechCode, "Transferred cruiser uses Assault speech identity");
            object loader = R.Get(Levels, "mAudioAssetBundleLoader");
            IDictionary clips = (IDictionary)R.Get(loader, "mClipDictionary");
            bool bank = false;
            foreach (AudioClip clip in clips.Values) if (clip!=null && clip.name=="vo_khaaneph_cruiser_sel_cbtgengengen_t1_9503") bank = true;
            Check(bank, "Native Khaaneph cruiser clips loaded");
            if (!SelectionRequested)
            {
                lock (PlayedAssaultClips) PlayedAssaultClips.Clear();
                VoiceOverEvent ev = new VoiceOverEvent(1, "sel", DialogueGroup.UnitEvent, null, "cruiser", Runtime.SpeechCode, (Entity)R.Get(Cruiser, "Entity"));
                Check((bool)R.Call(voice, "ExecuteVO", ev, false), "Native Fabric accepts recorded Assault Cruiser selection event");
                SelectionRequested = true; VoiceDeadline = Time.realtimeSinceStartup + 8; return;
            }
            bool played = false;
            lock (PlayedAssaultClips) foreach (string path in PlayedAssaultClips) if (path.Contains("Cruiser_Assault/") || path.ToLowerInvariant().Contains("cruiser_assault/")) played = true;
            if (!played) { Check(Time.realtimeSinceStartup < VoiceDeadline, "Native audio loader serves an actual Assault Cruiser clip"); return; }
            Log("Native voice PASS: original event semantics retained, actual Khaaneph recording served"); VoiceChecked = true;
        }
        public static void ClipLoaded(string path, object clip)
        {
            if (clip == null || path == null) return;
            string lower = path.ToLowerInvariant();
            if (!lower.Contains("coalition/") || (!lower.Contains("cas") && !lower.Contains("opsaca"))) return;
            if(NativeVoices.Candidates(path)==null)return;
            Check(((AudioClip)clip).name.StartsWith("vo_khaaneph_"),"Assault alias resolves an approved Khaaneph recording");
            if (lower.Contains("_rsh_opscas")) ResearchVoiceChecked = true;
            if (lower.Contains("_rsh_opsaca") || lower.Contains("_upg_opsaca"))
            {
                AntiAirVoiceChecked = true;
                Log("Native AA voice PASS: Khaaneph generic upgrade response served: " + ((AudioClip)clip).name);
            }
            lock (PlayedAssaultClips) if (PlayedAssaultClips.Add(path)) Log("Native Assault audio clip: " + path+" -> "+((AudioClip)clip).name);
        }
        static void AuditModelAPI(GameObject original)
        {
            SkinnedMeshRenderer[] renderers=original.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(renderers.Length==15,"Five custom parts at each of three LODs");
            foreach(SkinnedMeshRenderer renderer in renderers)
                Check(renderer.sharedMesh.name.StartsWith("KAC_") && renderer.bones.Length==57 && renderer.sharedMesh.bindposes.Length==57,"Native rig bindings retained");
            Check(original.GetComponent<LODGroup>().GetLODs().Length==3,"Native three-LOD group");
            GameObject instance=(GameObject)UnityEngine.Object.Instantiate(original);
            instance.transform.position=new Vector3(0,-1000,0);instance.SetActive(true);
            Animator animator=instance.GetComponent<Animator>();
            Check(animator!=null && animator.runtimeAnimatorController!=null,"Native controller retained");
            animator.enabled=false;
            SkinnedMeshRenderer core=Core(instance); Mesh bake=new Mesh();
            Clip(animator,"Closed_MissilePodDoors").SampleAnimation(instance,0);
            core.BakeMesh(bake);Vector3[] closed=bake.vertices;
            Clip(animator,"Opened_MissilePodDoors").SampleAnimation(instance,0);
            core.BakeMesh(bake);float doors=MaxDelta(closed,bake.vertices);
            Check(doors>.1f,"Native missile-door clip deforms new mesh");
            Clip(animator,"CannonsIdle").SampleAnimation(instance,0);core.BakeMesh(bake);Vector3[] idle=bake.vertices;
            Clip(animator,"CannonsRecoil").SampleAnimation(instance,.2f);core.BakeMesh(bake);float recoil=MaxDelta(idle,bake.vertices);
            Check(recoil>.02f,"Native recoil clip deforms new mesh");
            Transform fork=Bone(instance,"RIG_BattleCruiser_Susp_FLT");
            Clip(animator,"Anim_G_Battle_Cruiser_TurnLeft").SampleAnimation(instance,3f);Quaternion left=fork.localRotation;
            Clip(animator,"Anim_G_Battle_Cruiser_TurnRight").SampleAnimation(instance,3f);float turn=Quaternion.Angle(left,fork.localRotation);
            Check(turn>.1f,"Original steering clips drive armored stabilizer bone");
            Log("Native clip skinning PASS: doors="+doors+"; recoil="+recoil+"; steering angle="+turn+"; 15 renderers and 57 bindings");
            UnityEngine.Object.Destroy(instance);UnityEngine.Object.Destroy(bake);ModelGeometryChecked=true;
        }
        static AnimationClip Clip(Animator animator,string name)
        {
            foreach(AnimationClip clip in animator.runtimeAnimatorController.animationClips)if(clip.name==name)return clip;
            throw new Exception("Native clip missing "+name);
        }
        static void CaptureModel(GameObject model,Camera source,string label)
        {
            List<GameObject> inactive=new List<GameObject>();
            for(Transform node=model.transform;node!=null;node=node.parent)if(!node.gameObject.activeSelf){inactive.Add(node.gameObject);node.gameObject.SetActive(true);}
            string root=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(LiveProbe).Assembly.Location)),"KhaanephCruiser.Captures");
            Directory.CreateDirectory(root);
            Transform[] nodes=model.GetComponentsInChildren<Transform>(true);int[] layers=new int[nodes.Length];
            for(int i=0;i<nodes.Length;i++){layers[i]=nodes[i].gameObject.layer;nodes[i].gameObject.layer=28;}
            Bounds bounds=new Bounds(model.transform.position,Vector3.zero);
            foreach(SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bounds.Encapsulate(renderer.bounds);
                if(renderer.name.EndsWith("LOD0"))foreach(Material material in renderer.sharedMaterials)
                {
                    Log("Render material "+material.name+" shader="+(material.shader==null?"NULL":material.shader.name)+" supported="+(material.shader!=null && material.shader.isSupported)+" passes="+material.passCount+" keywords="+string.Join(",",material.shaderKeywords)+" texture="+(material.mainTexture==null?"NULL":material.mainTexture.name));
                    Check(material.shader!=null && material.shader.isSupported && material.shader.name!="Hidden/InternalErrorShader","Custom part retains supported shader after DLC lifecycle transitions");
                }
            }
            GameObject cameraObject=new GameObject("KAC capture camera");Camera camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(source);
            camera.enabled=false;camera.cullingMask=1<<28;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.22f,.22f,.22f,1);
            float radius=bounds.extents.magnitude;
            camera.transform.position=bounds.center+new Vector3(-1.4f,.95f,1.8f)*radius;camera.transform.LookAt(bounds.center);
            camera.orthographic=true;camera.orthographicSize=radius*.9f;camera.nearClipPlane=.01f;camera.farClipPlane=radius*10f;
            RenderTexture target=new RenderTexture(1200,900,24);camera.targetTexture=target;
            LODGroup lod=model.GetComponent<LODGroup>();lod.ForceLOD(0);camera.Render();
            RenderTexture prior=RenderTexture.active;RenderTexture.active=target;
            Texture2D image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(root,label+".png"),image.EncodeToPNG());RenderTexture.active=prior;lod.ForceLOD(-1);
            for(int i=0;i<nodes.Length;i++)nodes[i].gameObject.layer=layers[i];
            foreach(GameObject node in inactive)node.SetActive(false);
            UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(cameraObject);
            Log("Native render saved: "+label+"; bounds="+bounds);
        }
        static SkinnedMeshRenderer Core(GameObject model)
        {
            foreach(SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if(renderer.name.Contains("original weapons") && renderer.name.EndsWith("LOD0"))return renderer;
            throw new Exception("Custom weapon mesh missing");
        }
        static Transform Bone(GameObject model,string name)
        {
            foreach(Transform node in model.GetComponentsInChildren<Transform>(true))if(node.name==name)return node;
            throw new Exception("Native bone missing "+name);
        }
        static void AuditGameAnimation()
        {
            if(AnimationPhase==0)
            {
                foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                {
                    if(view.EntityID!=(Entity)R.Get(Cruiser,"Entity"))continue;
                    Animator original=view.GetComponent<Animator>();
                    CaptureModel(view.gameObject,Camera.main,"match-round-"+Round);
                    Log("Live cruiser animator: initialized="+original.isInitialized+" active="+original.gameObject.activeInHierarchy+" enabled="+original.enabled+" speed="+original.speed+" timescale="+Time.timeScale+" avatar="+(original.avatar==null?"null":original.avatar.name));
                    AnimationInstance=(GameObject)UnityEngine.Object.Instantiate(view.gameObject);
                    AnimationInstance.transform.position=new Vector3(0,-1000,0);
                    foreach(Collider collider in AnimationInstance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                    Rigidbody body=AnimationInstance.GetComponent<Rigidbody>();if(body!=null)body.isKinematic=true;
                    AnimationDriver=AnimationInstance.GetComponent<Animator>();AnimationDriver.enabled=true;AnimationDriver.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    AnimationCore=Core(AnimationInstance);AnimationBake=new Mesh();
                    AnimationDriver.SetBool("MissilePodL",false);AnimationDriver.SetBool("MissilePodR",false);
                    AnimationDeadline=Time.realtimeSinceStartup+.5f;AnimationPhase=1;return;
                }
                return;
            }
            if(Time.realtimeSinceStartup<AnimationDeadline)return;
            if(AnimationPhase==1)
            {
                Log("Live clone initialized="+AnimationDriver.isInitialized+" norm="+AnimationDriver.GetCurrentAnimatorStateInfo(1).normalizedTime);
                AnimationCore.BakeMesh(AnimationBake);AnimationBefore=AnimationBake.vertices;
                AnimationDriver.SetBool("MissilePodL",true);AnimationDriver.SetBool("MissilePodR",true);
                AnimationDeadline=Time.realtimeSinceStartup+1.25f;AnimationPhase=2;return;
            }
            if(AnimationPhase==2)
            {
                AnimationCore.BakeMesh(AnimationBake);float delta=MaxDelta(AnimationBefore,AnimationBake.vertices);
                Log("Live missile controller delta="+delta+" state="+AnimationDriver.GetCurrentAnimatorStateInfo(1).fullPathHash+" norm="+AnimationDriver.GetCurrentAnimatorStateInfo(1).normalizedTime);
                Check(delta>.1f,"Live native controller opens custom missile doors");
                AnimationDriver.SetBool("FireFwd",false);AnimationDriver.Play("Cannon_Front.Idle",3,0);
                AnimationDeadline=Time.realtimeSinceStartup+.25f;AnimationPhase=3;return;
            }
            if(AnimationPhase==3)
            {
                AnimationCore.BakeMesh(AnimationBake);AnimationBefore=AnimationBake.vertices;
                AnimationDriver.SetBool("FireFwd",true);AnimationDriver.Play("Cannon_Front.CannonFire",3,0);
                AnimationDeadline=Time.realtimeSinceStartup+.1f;AnimationPhase=4;return;
            }
            if(AnimationPhase==4)
            {
                AnimationCore.BakeMesh(AnimationBake);float delta=MaxDelta(AnimationBefore,AnimationBake.vertices);
                if(delta<=.02f && Time.realtimeSinceStartup<AnimationDeadline+.5f)return;
                Log("Live cannon controller delta="+delta);Check(delta>.02f,"Live native controller recoils custom cannon");
                AnimationDriver.SetFloat("SteeringVectorX",-1f);AnimationDriver.Play("Turning.Turning",5,0);
                AnimationDeadline=Time.realtimeSinceStartup+.25f;AnimationPhase=5;return;
            }
            if(AnimationPhase==5)
            {
                SteeringBefore=Bone(AnimationInstance,"RIG_BattleCruiser_Susp_FLT").localRotation;
                AnimationDriver.SetFloat("SteeringVectorX",1f);AnimationDeadline=Time.realtimeSinceStartup+.25f;AnimationPhase=6;return;
            }
            float angle=Quaternion.Angle(SteeringBefore,Bone(AnimationInstance,"RIG_BattleCruiser_Susp_FLT").localRotation);
            Log("Live stabilizer steering angle="+angle);Check(angle>.1f,"Live native controller drives new jaw stabilizer");
            UnityEngine.Object.Destroy(AnimationInstance);UnityEngine.Object.Destroy(AnimationBake);ModelChecked=true;
            Log("Model animation PASS: native match initialization, live controller missile doors, cannon recoil and stabilizer steering");
        }
        static float MaxDelta(Vector3[] before,Vector3[] after)
        {
            Check(before.Length==after.Length,"Animated mesh vertex count is stable");
            float result=0;for(int i=0;i<before.Length;i++)result=Mathf.Max(result,(before[i]-after[i]).magnitude);return result;
        }
        static void AuditResearchHUD()
        {
            foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll(typeof(ResearchPanelsController)))
            {
                object engineering = R.Get(item, "mEngineeringController"), upgrades = R.Get(item, "mUpgradesController");
                object state = R.Get(item, "mMostRecentLocalCommanderState");
                if (engineering == null || upgrades == null || state == null) continue;
                if (!HUDOpened)
                {
                    R.Call(item, "ShowHidePanels", true); R.Call(item, "UpdatePanels", state, true); HUDOpened = true;
                }
                foreach (object button in (IEnumerable)R.Get(engineering, "mResearchButtons")) AuditButton(button);
                foreach (object group in (IEnumerable)R.Get(upgrades, "mUpgradeGroups"))
                    foreach (object button in (IEnumerable)R.Get(group, "ResearchButtons")) AuditButton(button);
            }
        }
        static void AuditButton(object button)
        {
            ResearchItemAttributes research = R.Get(button, "ResearchItem") as ResearchItemAttributes;
            if (research == null) return;
            Check(research.Name != "Tier_3_G_HonorCruiser" && research.Name != "Research_Buff_G_HonorCruiser_Defenses", "K HUD has no Honor Guard research");
            if (Array.IndexOf(ResearchChain, research.Name) >= 0 && HUDResearch.Add(research.Name)) Log("Native HUD research button PASS: " + research.Name);
        }
        static void AuditCampaignRegistration(object main)
        {
            R.Set(main, "mCurrentGameMode", GameMode.SinglePlayer);
            R.Call(main, "ResetEntityManager");
            EntityTypeCollection entities = (EntityTypeCollection)R.Get(typeof(ShipbreakersMain), "sEntityTypes");
            Check(entities.GetEntityType("G_Battlecruiser_MP").Get<UnitAttributes>().MaxHealth == 2700, "Original Assault HP restored for campaign registration");
            Check(DataPlan.ResearchSummary(entities.GetEntityType("Coalition").Get<TechTreeAttributes>()).Contains("Tier_3_C_AssaultCruiser"), "Original Coalition Assault research restored for campaign registration");
            Check(entities.GetEntityType("C_Carrier_MP").Get<AbilityAttributes>("Ability_G_ProduceBattlecruiser_MP") != null, "Original Coalition Assault production restored for campaign registration");
            Log("Campaign registration PASS: original Coalition research/production and HP restored (no campaign mission played)");
        }
        static void StartMatch()
        {
            NullSession session = new NullSession();
            List<CommanderDescription> descriptions = new List<CommanderDescription>();
            descriptions.Add(new CommanderDescription("Live probe", new CommanderID(1), session.LocalPlayerID, 0, Round < 2 ? (int)KPack.CustomizesFaction : 0, new TeamID(1), PlayerType.Human, UnitColors.kDefault, Difficulty.None, Round < 2 ? KID : DLCPackID.kInvalidID, false));
            descriptions.Add(new CommanderDescription("Opponent", new CommanderID(2), NetworkPlayerID.kInvalidID, 1, Round < 2 ? 0 : 1, new TeamID(1), PlayerType.AI, UnitColors.kDefault, Difficulty.None, DLCPackID.kInvalidID, false));
            if (Round == 1)
            {
                descriptions.Add(new CommanderDescription("Gaalsien", new CommanderID(3), NetworkPlayerID.kInvalidID, 2, 1, new TeamID(1), PlayerType.AI, UnitColors.kDefault, Difficulty.None, DLCPackID.kInvalidID, false));
                Check(SPack != null, "Soban pack available for four-faction regression");
                descriptions.Add(new CommanderDescription("Soban", new CommanderID(4), NetworkPlayerID.kInvalidID, 3, (int)SPack.CustomizesFaction, new TeamID(1), PlayerType.AI, UnitColors.kDefault, Difficulty.None, SPack.DLCApplicationID, false));
            }
            LevelDefinition map = null;
            foreach (LevelDefinition candidate in Levels.LevelEntriesMP) if (candidate != null && candidate.MaxPlayers >= descriptions.Count) { map = candidate; break; }
            Check(map != null, "Native skirmish map");
            Dictionary<CommanderID, TeamID> teams = new Dictionary<CommanderID, TeamID>();
            foreach (CommanderDescription description in descriptions) teams.Add(description.CommanderID, description.TeamID);
            List<CommanderSpawnInfo> spawns;
            Check(SpawnAssigner.GenerateSpawnPoints(map, teams, TeamSetting.FFA, out spawns), "Native spawn assignment");
            // Keep factions allied in this test so AI combat cannot contaminate the repair measurements.
            for (int i = 0; i < spawns.Count; i++) { CommanderSpawnInfo spawn = spawns[i]; spawns[i] = new CommanderSpawnInfo(spawn.CommanderID, new TeamID(1), spawn.SpawnIndex); }
            Dictionary<CommanderID, PlayerSelection> selections = SpawnAssigner.GeneratePlayerSelections(Levels.CommanderAttributes, descriptions, spawns, DLC);
            GameStartSettings settings = new GameStartSettings(selections, VictoryConditions.CarrierAnnihilation, TeamSetting.FFA, GameModeSettings.kDefaultSettings, 3650, true, false, AutomatchSizeType.ThreeVSThree);
            Levels.StartLoadLevel(GameMode.AISkirmish, ReplayMode.ReplaysDisabled, map, new DependencyContainer<GameStartSettings, SessionBase>(settings, session));
            Log("Loading native skirmish round " + Round + ": map=" + map.SceneName + "; commanders=" + descriptions.Count);
        }

        public static void SimTick(object sim)
        {
            if (Failed || SimDone || Stage != 4) return;
            try
            {
                uint frame = Convert.ToUInt32(R.Get(R.Get(sim, "GlobalFrameCount"), "FrameNumber"));
                if (frame < 5) return;
                if (Round == 2)
                {
                    Check(EntityTypes(sim).GetEntityType(Runtime.UnitKey) == null, "No stale Khaaneph entity in Coalition/Gaalsien match");
                    Check(R.Get(typeof(Runtime), "KUnit") == null && R.Get(typeof(Runtime), "TransferredFields") == null, "No stale Khaaneph transfer cache");
                    Check(!DataPlan.ResearchSummary(EntityTypes(sim).GetEntityType("Coalition").Get<TechTreeAttributes>()).Contains("AssaultCruiser"), "Coalition-only match has patched roster");
                    Log("No-Khaaneph match PASS: stale DLC references cleared; Coalition removal retained"); SimDone = true; return;
                }
                if (frame - LastLogFrame >= 80) { Log("Sim round=" + Round + " stage=" + SimStage + " frame=" + frame + " pending=" + PendingResearch); LastLogFrame = frame; }
                if (SimStage == 0)
                {
                    object manager = R.Get(sim, "CommanderManager");
                    Commander = R.Call(manager, "GetCommanderFromID", new CommanderID(1));
                    Check((string)R.Get(Commander, "TechTreeTypeName") == "Khaaneph", "Actual commander uses Khaaneph tech tree");
                    foreach (object commander in ((IDictionary)R.Get(manager, "mCommanders")).Values)
                    {
                        string name = (string)R.Get(commander, "TechTreeTypeName");
                        TechTreeAttributes tree = EntityTypes(sim).GetEntityType(name).Get<TechTreeAttributes>();
                        string summary = DataPlan.ResearchSummary(tree);
                        Check(name == "Khaaneph" ? summary.Contains(Runtime.Fabrication) : !summary.Contains("AssaultCruiser"), "Actual commander tree " + name);
                        Log("Commander tree PASS: " + name + "; branches=" + tree.TechTrees.Length);
                    }
                    object storage = R.Get(Commander, "Storage");
                    R.Call(storage, "SetCurrentAmount", "Resource1", 30000); R.Call(storage, "SetCurrentAmount", "Resource2", 30000);
                    Check(!CanResearch(sim, Runtime.Fabrication), "Assault fabrication locked before predecessors");
                    SimStage = 1;
                }
                if (SimStage >= 1 && SimStage <= 3)
                {
                    string name = ResearchChain[SimStage - 1];
                    if (PendingResearch == null)
                    {
                        Check(CanResearch(sim, name), "Native research available: " + name);
                        Check((bool)R.Call(Commander, "StartResearch", EntityTypes(sim).GetEntityType(name).Get<ResearchItemAttributes>()), "Native research starts: " + name);
                        PendingResearch = name; Log("Native research START: " + name + "; frame=" + frame); return;
                    }
                    if (((IList)R.Get(Commander, "CompletedResearch")).Contains(name))
                    {
                        Log("Native research COMPLETE: " + name + "; frame=" + frame);
                        PendingResearch = null; SimStage++; return;
                    }
                    return;
                }
                if (SimStage == 4)
                {
                    if (PendingResearch == null)
                    {
                        object carrier = FindUnit(sim, "K_Carrier_MP");
                        Check(carrier != null, "Native Khaaneph carrier");
                        UseAbility(sim, R.Get(carrier, "Entity"), "Ability_Produce_K_HonorGuard");
                        Log("Native carrier production requested"); PendingResearch = "Production"; DamageFrame = frame; return;
                    }
                    Cruiser = FindUnit(sim, Runtime.UnitKey);
                    if (Cruiser == null) { Check(frame - DamageFrame < 1600, "Native production finishes within 200 seconds"); return; }
                    object aa = AbilityInstance(R.Get(Cruiser, "Entity"), Runtime.AntiAirAbility);
                    // RequiredResearchIsLocked denotes a permanently unavailable research item,
                    // not an available upgrade that the commander has not completed yet.
                    Check(!((IList)R.Get(Commander, "CompletedResearch")).Contains(Runtime.AntiAir) && !(bool)R.Get(aa, "PassiveActivated"), "AA passive inactive before upgrade");
                    Check(!AirWeaponEnabled(sim, (Entity)R.Get(Cruiser, "Entity")), "Air weapon disabled before upgrade");
                    Log("Native AA prerequisite PASS: produced cruiser air weapon disabled before upgrade");
                    SimStage = 5; PendingResearch = null; return;
                }
                if (SimStage == 5)
                {
                    if (PendingResearch == null)
                    {
                        Check(CanResearch(sim, Runtime.AntiAir), "Native AA research available");
                        Check((bool)R.Call(Commander, "StartResearch", EntityTypes(sim).GetEntityType(Runtime.AntiAir).Get<ResearchItemAttributes>()), "Native AA research starts");
                        PendingResearch = Runtime.AntiAir; Log("Native research START: " + Runtime.AntiAir + "; frame=" + frame); return;
                    }
                    if (!((IList)R.Get(Commander, "CompletedResearch")).Contains(Runtime.AntiAir)) return;
                    Log("Native research COMPLETE: " + Runtime.AntiAir + "; frame=" + frame); PendingResearch = null; SimStage = 6; return;
                }
                if (SimStage == 6)
                {
                    Cruiser = FindUnit(sim, Runtime.UnitKey);
                    if (Cruiser == null) { Check(frame - DamageFrame < 1600, "Native production finishes within 200 seconds"); return; }
                    Entity entity = (Entity)R.Get(Cruiser, "Entity");
                    Health = Component(sim, entity, 7, "BBI.Game.Simulation.Health");
                    Check((int)R.Get(Health, "MaxHealth") == 3650, "Produced cruiser HP3650");
                    Check((CommanderID)R.Call(typeof(Sim), "GetEntityCommanderID", entity) == new CommanderID(1), "Produced cruiser belongs to Khaaneph player");
                    AbilityAttributes aa = EntityTypes(sim).GetEntityType(Runtime.UnitKey).Get<AbilityAttributes>(Runtime.AntiAirAbility);
                    Check(aa != null && aa.ResearchDependencies.RequiredResearch[0] == Runtime.AntiAir, "Produced cruiser AA research gate");
                    Log("Produced cruiser PASS: HP=" + R.Get(Health, "MaxHealth") + "; owner=1; checking passive AA activation");
                    R.Set(Health, "CurrentHealth", 2000); DamageFrame = frame; DamagedHP = (int)R.Get(Health, "CurrentHealth"); SimStage = 7; return;
                }
                if (SimStage == 7)
                {
                    if (frame - DamageFrame < 72) return;
                    object aa = AbilityInstance(R.Get(Cruiser, "Entity"), Runtime.AntiAirAbility);
                    Check(!(bool)R.Get(aa, "RequiredResearchIsLocked") && (bool)R.Get(aa, "PassiveActivated"), "Native cruiser AA passive activates after its upgrade");
                    Check(AirWeaponEnabled(sim, (Entity)R.Get(Cruiser, "Entity")), "Existing cruiser air weapon enabled by upgrade");
                    Log("Native AA upgrade PASS: existing cruiser passive active and AA_RIGHT weapon enabled");
                    Check((int)R.Get(Health, "CurrentHealth") == DamagedHP, "Passive repair waits at least nine seconds");
                    Log("Passive repair PASS: no healing after nine seconds"); SimStage = 8; return;
                }
                if (SimStage == 8)
                {
                    if (frame - DamageFrame < 104) return;
                    int hp = (int)R.Get(Health, "CurrentHealth");
                    Check(hp == DamagedHP + 144, "Passive repair heals three ticks of 48 HP after ten-second delay");
                    Log("Passive repair PASS: frame=" + frame + "; damaged=" + DamagedHP + "; now=" + hp);
                    R.Set(Health, "CurrentHealth", hp - 400); DamagedHP = hp - 400; DamageFrame = frame; SimStage = 9; return;
                }
                if (SimStage == 9)
                {
                    if (frame - DamageFrame < 72) return;
                    Check((int)R.Get(Health, "CurrentHealth") == DamagedHP, "New damage interrupts passive healing"); SimStage = 10; return;
                }
                if (SimStage == 10 && frame - DamageFrame >= 104)
                {
                    Check((int)R.Get(Health, "CurrentHealth") == DamagedHP + 144, "Passive repair resumes at 48 HP/s after second delay");
                    Log("PASS simulation: actual research/HUD chain, carrier production, HP, ownership, existing-cruiser AA weapon unlock, passive delay/interruption/resume"); SimDone = true;
                }
            }
            catch (Exception ex) { Fail("sim stage " + SimStage, ex); }
        }

        static EntityTypeCollection EntityTypes(object sim) { return (EntityTypeCollection)R.Get(R.Get(sim, "Settings"), "EntityTypes"); }
        static bool CanResearch(object sim, string name)
        {
            Type helper = typeof(Sim).Assembly.GetType("BBI.Game.Simulation.ResearchHelper", true);
            return (bool)R.Call(helper, "CanResearchItem", Commander, EntityTypes(sim).GetEntityType(name).Get<ResearchItemAttributes>());
        }
        static object FindUnit(object sim, string name)
        {
            foreach (object unit in (IEnumerable)R.Get(R.Get(sim, "UnitManager"), "Units")) if ((string)R.Get(unit, "TypeName") == name) return unit;
            return null;
        }
        static object Component(object sim, Entity entity, int id, string type)
        { return Component(sim, entity, id, typeof(Sim).Assembly.GetType(type, true)); }
        static object Component(object sim, Entity entity, int id, Type type)
        {
            object system = R.Get(sim, "EntitySystem");
            foreach (MethodInfo method in system.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
                    return method.MakeGenericMethod(type).Invoke(system, new object[] { entity, id });
            throw new Exception("Native component accessor missing");
        }
        static object AbilityInstance(object entity, string name)
        { return R.Call(typeof(Sim).Assembly.GetType("BBI.Game.Simulation.AbilityHelper", true), "GetAbilityInstanceForUnit", entity, name); }
        static bool AirWeaponEnabled(object sim, Entity entity)
        {
            Type weapon = typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponFireOperation", true);
            foreach (object operation in (IEnumerable)Component(sim, entity, 6, typeof(List<>).MakeGenericType(weapon)))
                if (((WeaponBinding)R.Get(operation, "WeaponBinding")).WeaponID == "AA_RIGHT") return (bool)R.Get(operation, "Enabled");
            throw new Exception("Native AA_RIGHT weapon binding missing");
        }
        static void UseAbility(object sim, object entity, string name)
        {
            Type command = typeof(Sim).Assembly.GetType("BBI.Game.Commands.UseAbilityCommand", true);
            Type addMode = typeof(Sim).Assembly.GetType("BBI.Game.Simulation.SelectedUnitManager+AddMode", true);
            ConstructorInfo constructor = null;
            foreach (ConstructorInfo candidate in command.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) if (candidate.GetParameters().Length == 9) constructor = candidate;
            object instance = constructor.Invoke(new object[] { new CommanderID(1), name, AbilityAction.Activate, new Entity[] { (Entity)entity }, null, Vector2r.Zero, AbilityTargetingType.Self, Enum.Parse(addMode, "NewSet"), false });
            Check((bool)R.Call(instance, "RunCommand", sim, R.Get(sim, "InteractionProvider"), R.Get(sim, "GlobalFrameCount")), "Native ability command accepted: " + name);
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Probe assertion: " + message); }
        static void Fail(string stage, Exception error) { Failed = true; Log("FAIL " + stage + ": " + error); }
        static void Log(string message)
        {
            lock (LogMutex)
            {
                string path = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(typeof(LiveProbe).Assembly.Location)), "KhaanephCruiser.LiveProbe.log");
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
        }
    }
    static class R
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        public static object Get(object value, string name)
        {
            Type type = value as Type ?? value.GetType(); object instance = value is Type ? null : value;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, Flags); if (field != null) return field.GetValue(instance);
                PropertyInfo property = current.GetProperty(name, Flags); if (property != null) return property.GetValue(instance, null);
            }
            throw new MissingMemberException(type.FullName, name);
        }
        public static void Set(object value, string name, object replacement)
        {
            for (Type current = value.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, Flags); if (field != null) { field.SetValue(value, replacement); return; }
                PropertyInfo property = current.GetProperty(name, Flags); if (property != null) { property.SetValue(value, replacement, null); return; }
            }
            throw new MissingMemberException(value.GetType().FullName, name);
        }
        public static object Call(object value, string name, params object[] args)
        {
            Type type = value as Type ?? value.GetType(); object instance = value is Type ? null : value;
            for (Type current = type; current != null; current = current.BaseType)
                foreach (MethodInfo method in current.GetMethods(Flags))
                {
                    if (method.Name != name || method.IsGenericMethodDefinition || method.GetParameters().Length != args.Length) continue;
                    bool match = true; ParameterInfo[] parameters = method.GetParameters();
                    for (int i = 0; i < args.Length; i++) if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i])) match = false;
                    if (match) return method.Invoke(instance, args);
                }
            throw new MissingMethodException(type.FullName, name + " (" + args.Length + " arguments)");
        }
    }
}
