using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KhaanephCruiser
{
    // Native prefab/controller retained; only its geometry and materials change.
    public static class ModelAssets
    {
        sealed class MeshRecord
        {
            public string Name;
            public int LOD;
            public Mesh Mesh;
            public int[] Materials;
        }
        static readonly Dictionary<GameObject, GameObject> Prefabs = new Dictionary<GameObject, GameObject>();
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static GameObject Templates;
        static string[] BoneNames;
        static Matrix4x4[] BindPoses;
        static Material[] Materials;
        static MeshRecord[] Geometry;

        public static string AssetPath
        {
            get { return Path.Combine(Directory.GetParent(Path.GetDirectoryName(typeof(Runtime).Assembly.Location)).FullName, "assets/KhaanephCruiser"); }
        }

        public static GameObject CreatePrefab(GameObject original)
        {
            GameObject cached;
            if (Prefabs.TryGetValue(original, out cached) && cached != null) return cached;
            if (Geometry == null) Load(original);
            if (Templates == null)
            {
                Templates = new GameObject("KhaanephCruiser.ModelTemplates");
                Templates.SetActive(false); Templates.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(Templates);
            }
            GameObject model = (GameObject)UnityEngine.Object.Instantiate(original);
            model.name = original.name;
            model.transform.parent = Templates.transform;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = original.transform.localRotation;
            model.transform.localScale = original.transform.localScale;
            SkinnedMeshRenderer[] previous = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (previous.Length != 3) throw new InvalidOperationException("Unsupported native cruiser renderer count");
            Transform rootBone = previous[0].rootBone;
            int layer = previous[0].gameObject.layer;
            Dictionary<string, Transform> nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true)) nodes.Add(transform.name, transform);
            Transform[] bones = new Transform[BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!nodes.TryGetValue(BoneNames[i], out bones[i])) throw new InvalidOperationException("Missing native animation bone " + BoneNames[i]);
            }
            LODGroup group = model.GetComponent<LODGroup>();
            if (group == null) throw new InvalidOperationException("Native cruiser LOD group missing");
            LOD[] oldLODs = group.GetLODs();
            if (oldLODs.Length != 3) throw new InvalidOperationException("Unsupported native cruiser LOD contract");
            foreach (SkinnedMeshRenderer renderer in previous) UnityEngine.Object.DestroyImmediate(renderer);
            List<Renderer>[] levels = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            foreach (MeshRecord record in Geometry)
            {
                GameObject part = new GameObject("KAC_" + record.Name + "_LOD" + record.LOD);
                part.layer = layer; part.transform.parent = model.transform;
                part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity; part.transform.localScale = Vector3.one;
                SkinnedMeshRenderer renderer = part.AddComponent<SkinnedMeshRenderer>();
                record.Mesh.bindposes = BindPoses;
                renderer.sharedMesh = record.Mesh; renderer.bones = bones; renderer.rootBone = rootBone;
                renderer.updateWhenOffscreen = true;
                Bounds bounds = record.Mesh.bounds; bounds.Expand(12f); renderer.localBounds = bounds;
                Material[] materials = new Material[record.Materials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = Materials[record.Materials[i]];
                renderer.sharedMaterials = materials; levels[record.LOD].Add(renderer);
            }
            LOD[] lods = new LOD[3];
            for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(oldLODs[i].screenRelativeTransitionHeight, levels[i].ToArray());
            group.SetLODs(lods); group.RecalculateBounds();
            Prefabs[original] = model;
            Runtime.Log("Kitbash model prepared: 57 native transforms, 15 skinned renderers, three LODs; original Animator and physics retained.");
            return model;
        }

        static void Load(GameObject source)
        {
            // DLC bundle shaders are destroyed by native preview unload even while
            // a cloned material survives. All donor materials use the same UNIT
            // shader contract; retain the base-game copy for every custom part.
            Shader persistentShader = FindMaterial(source, "MAT_Unit_G_BattleCruiser").shader;
            if (persistentShader == null || !persistentShader.isSupported) throw new InvalidOperationException("Native persistent unit shader unavailable");
            string file = Path.Combine(AssetPath, "model.kac");
            using (BinaryReader reader = new BinaryReader(File.OpenRead(file), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "KAC2" || reader.ReadInt32() != 2) throw new InvalidDataException("Unsupported cruiser geometry format");
                int count = Count(reader, 57);
                if (count != 57) throw new InvalidDataException("Cruiser rig contract changed");
                BoneNames = new string[count];
                for (int i = 0; i < count; i++) BoneNames[i] = Text(reader);
                BindPoses = new Matrix4x4[count];
                for (int i = 0; i < count; i++)
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++) BindPoses[i][row,column] = reader.ReadSingle();
                count = Count(reader, 32); Materials = new Material[count];
                for (int i = 0; i < count; i++)
                {
                    string name = Text(reader), template = Text(reader);
                    bool plain = reader.ReadInt32() != 0, white = reader.ReadInt32() != 0;
                    Color color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Material material = new Material(FindMaterial(source, template));
                    material.shader = persistentShader;
                    material.name = "KAC_" + name; material.hideFlags = HideFlags.HideAndDontSave;
                    int properties = Count(reader, 16);
                    for (int property = 0; property < properties; property++)
                    {
                        string propertyName = Text(reader), path = Text(reader);
                        Vector2 scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        Vector2 offset = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        if (material.HasProperty(propertyName))
                        {
                            material.SetTexture(propertyName, Texture(path));
                            material.SetTextureScale(propertyName, scale); material.SetTextureOffset(propertyName, offset);
                        }
                    }
                    if (plain)
                    {
                        material.SetTexture("_MainTex", Solid("Steel", color));
                        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", Solid("FlatNormal", new Color(.5f,.5f,1f,.5f)));
                        if (material.HasProperty("_OverlayMask")) material.SetTexture("_OverlayMask", Solid("NoTeamPaint", Color.clear));
                        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                    }
                    if (white && material.HasProperty("_OverlayMask")) material.SetTexture("_OverlayMask", Solid("NoTeamPaint", Color.clear));
                    Materials[i] = material;
                }
                count = Count(reader, 15);
                if (count != 15) throw new InvalidDataException("Expected five parts across three LODs");
                Geometry = new MeshRecord[count];
                for (int i = 0; i < count; i++)
                {
                    MeshRecord record = new MeshRecord(); record.Name = Text(reader); record.LOD = reader.ReadInt32();
                    if (record.LOD < 0 || record.LOD > 2) throw new InvalidDataException("Invalid cruiser LOD");
                    int vertices = Count(reader, 65535);
                    Vector3[] positions = new Vector3[vertices], normals = new Vector3[vertices];
                    Vector2[] uv = new Vector2[vertices]; Vector4[] tangents = new Vector4[vertices];
                    BoneWeight[] weights = new BoneWeight[vertices]; Color[] colors = new Color[vertices];
                    for (int vertex = 0; vertex < vertices; vertex++)
                    {
                        positions[vertex] = V3(reader); normals[vertex] = V3(reader);
                        uv[vertex] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                        tangents[vertex] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        BoneWeight weight = new BoneWeight();
                        weight.weight0 = reader.ReadSingle(); weight.weight1 = reader.ReadSingle(); weight.weight2 = reader.ReadSingle(); weight.weight3 = reader.ReadSingle();
                        weight.boneIndex0 = reader.ReadInt32(); weight.boneIndex1 = reader.ReadInt32(); weight.boneIndex2 = reader.ReadInt32(); weight.boneIndex3 = reader.ReadInt32();
                        weights[vertex] = weight; colors[vertex] = Color.white;
                    }
                    Mesh mesh = new Mesh(); mesh.name = "KAC_" + record.Name + "_LOD" + record.LOD; mesh.hideFlags = HideFlags.HideAndDontSave;
                    mesh.vertices = positions; mesh.normals = normals; mesh.uv = uv; mesh.tangents = tangents; mesh.boneWeights = weights; mesh.colors = colors;
                    int submeshes = Count(reader, 16); mesh.subMeshCount = submeshes; record.Materials = new int[submeshes];
                    for (int submesh = 0; submesh < submeshes; submesh++)
                    {
                        record.Materials[submesh] = reader.ReadInt32();
                        if (record.Materials[submesh] < 0 || record.Materials[submesh] >= Materials.Length) throw new InvalidDataException("Invalid material binding");
                        int indices = Count(reader, 1000000); int[] triangles = new int[indices];
                        for (int index = 0; index < indices; index++) triangles[index] = reader.ReadInt32();
                        mesh.SetTriangles(triangles, submesh);
                    }
                    mesh.RecalculateBounds(); record.Mesh = mesh; Geometry[i] = record;
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected cruiser geometry tail");
            }
        }
        static Material FindMaterial(GameObject source, string name)
        {
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Material material in renderer.sharedMaterials) if (material != null && material.name == name) return material;
            foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll(typeof(Material)))
            {
                Material material = (Material)item;
                if (material.name == name) return material;
            }
            throw new InvalidOperationException("Native material template unavailable: " + name);
        }
        static Texture2D Texture(string relative)
        {
            Texture2D texture;
            if (Textures.TryGetValue(relative, out texture)) return texture;
            string root = Path.GetFullPath(AssetPath), file = Path.GetFullPath(Path.Combine(root, relative));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid texture path");
            texture = new Texture2D(2,2,TextureFormat.RGBA32,true);
            if (!texture.LoadImage(File.ReadAllBytes(file))) throw new InvalidDataException("Cannot decode cruiser texture " + relative);
            texture.name = "KAC_" + relative; texture.filterMode = FilterMode.Trilinear; texture.hideFlags = HideFlags.HideAndDontSave;
            Textures[relative] = texture; return texture;
        }
        static Texture2D Solid(string name, Color color)
        {
            Texture2D texture;
            if (Textures.TryGetValue(name, out texture)) return texture;
            texture = new Texture2D(1,1,TextureFormat.RGBA32,false); texture.SetPixel(0,0,color); texture.Apply();
            texture.name = "KAC_" + name; texture.hideFlags = HideFlags.HideAndDontSave; Textures[name] = texture; return texture;
        }
        static string Text(BinaryReader reader) { return Encoding.UTF8.GetString(reader.ReadBytes(Count(reader, 4096))); }
        static int Count(BinaryReader reader, int max) { int count = reader.ReadInt32(); if (count < 0 || count > max) throw new InvalidDataException("Invalid model buffer length"); return count; }
        static Vector3 V3(BinaryReader reader) { return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
    }
}
