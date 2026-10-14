"""Read native donor geometry/materials without modifying any game archives."""
from pathlib import Path
import json
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "work/resources/python_deps"))
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

GAME = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak")
OUT = ROOT / "assets/kitbash/donors"
OUT.mkdir(parents=True, exist_ok=True)
env = UnityPy.Environment(path=str(GAME / "Data"))
env.load_file(str(GAME / "Data/sharedassets0.assets"))
env.load_file(str(GAME / "Bundles/dlc/dlc0002"))

def vector(v):
    return [getattr(v, axis) for axis in "xyz"]

def quaternion(q):
    return [getattr(q, axis) for axis in "xyzw"]

def ptr_key(ptr):
    reader = ptr.deref()
    return f"{reader.assets_file.name}:{reader.path_id}"

def component_ptr(pair):
    return pair.component if hasattr(pair, "component") else pair[1]

materials, textures, meshes = {}, {}, {}

def material(ptr):
    key = ptr_key(ptr)
    if key in materials:
        return key
    data = ptr.read()
    props = data.m_SavedProperties
    record = {"name": data.m_Name, "colors": {}, "floats": {}, "textures": {}}
    for name, value in props.m_Colors:
        name = name.name if hasattr(name, "name") else name
        record["colors"][name] = [value.r, value.g, value.b, value.a]
    for name, value in props.m_Floats:
        name = name.name if hasattr(name, "name") else name
        record["floats"][name] = value
    for name, value in props.m_TexEnvs:
        name = name.name if hasattr(name, "name") else name
        if not value.m_Texture:
            continue
        texkey = ptr_key(value.m_Texture)
        if texkey not in textures:
            texture = value.m_Texture.read()
            filename = f"texture_{len(textures):02d}.png"
            texture.image.save(OUT / filename)
            textures[texkey] = {"name": texture.m_Name, "file": filename,
                                "width": texture.m_Width, "height": texture.m_Height}
        record["textures"][name] = {"key": texkey, "scale": [value.m_Scale.x, value.m_Scale.y],
                                     "offset": [value.m_Offset.x, value.m_Offset.y]}
    materials[key] = record
    return key

def mesh(ptr):
    key = ptr_key(ptr)
    if key in meshes:
        return key
    native = ptr.read()
    handler = MeshHandler(native)
    handler.process()
    record = {"name": native.m_Name, "vertices": handler.m_Vertices,
              "normals": handler.m_Normals, "uv": handler.m_UV0,
              "submeshes": handler.get_triangles(), "weights": handler.m_BoneWeights,
              "bone_indices": handler.m_BoneIndices}
    if native.m_BindPose:
        record["bindposes"] = [[getattr(pose, f"e{row}{col}") for row in range(4) for col in range(4)]
                                for pose in native.m_BindPose]
    meshes[key] = record
    return key

def prefab(root):
    transforms, renderers = [], []
    def visit(go, parent):
        parts = [component_ptr(pair) for pair in go.m_Component]
        transform = next(ptr for ptr in parts if ptr.type.name == "Transform")
        data = transform.read()
        key = ptr_key(transform)
        transforms.append({"key": key, "name": go.m_Name, "parent": parent,
                           "position": vector(data.m_LocalPosition), "rotation": quaternion(data.m_LocalRotation),
                           "scale": vector(data.m_LocalScale), "active": bool(go.m_IsActive)})
        filters = [ptr for ptr in parts if ptr.type.name == "MeshFilter"]
        for ptr in parts:
            if ptr.type.name not in ("SkinnedMeshRenderer", "MeshRenderer"):
                continue
            renderer = ptr.read()
            meshptr = renderer.m_Mesh if hasattr(renderer, "m_Mesh") else filters[0].read().m_Mesh
            renderers.append({"transform": key, "name": go.m_Name, "type": ptr.type.name,
                              "mesh": mesh(meshptr), "materials": [material(p) for p in renderer.m_Materials],
                              "bones": [ptr_key(p) for p in getattr(renderer, "m_Bones", [])],
                              "enabled": bool(renderer.m_Enabled)})
        for child in data.m_Children:
            visit(child.read().m_GameObject.read(), key)
    visit(root.read(), None)
    return {"name": root.read().m_Name, "transforms": transforms, "renderers": renderers}

sources = [("coalition_assault", "sharedassets0.assets", 9166),
           ("khaaneph_honor", "CAB-2e9fc07973a399cde98f3ed5a9cd833d", -4858069049477306287),
           ("khaaneph_siege", "CAB-2e9fc07973a399cde98f3ed5a9cd833d", -3419007820283949715),
           ("khaaneph_production", "CAB-2e9fc07973a399cde98f3ed5a9cd833d", 6462739249178112410),
           ("khaaneph_baserunner", "CAB-2e9fc07973a399cde98f3ed5a9cd833d", -7324966947487926791)]
objects = {(obj.assets_file.name, obj.path_id): obj for obj in env.objects}
prefabs = {name: prefab(objects[(file, path_id)]) for name, file, path_id in sources}
report = {"coordinate_system": "Unity native; no normalization or rescaling", "sources": sources,
          "prefabs": prefabs, "meshes": meshes, "materials": materials, "textures": textures}
(OUT / "donors.json").write_text(json.dumps(report, separators=(",", ":")), encoding="utf-8")
summary = {name: {"name": data["name"], "transforms": len(data["transforms"]),
                  "renderers": [{"name": r["name"], "mesh": meshes[r["mesh"]]["name"],
                                 "vertices": len(meshes[r["mesh"]]["vertices"]), "bones": len(r["bones"]),
                                 "materials": [materials[m]["name"] for m in r["materials"]]} for r in data["renderers"]]}
           for name, data in prefabs.items()}
evidence = ROOT / "evidence/models"
evidence.mkdir(parents=True, exist_ok=True)
(evidence / "donor-inventory.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps({"output": str(OUT), "materials": len(materials), "textures": len(textures), "donors": summary}, indent=2))
