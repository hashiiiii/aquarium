#!/usr/bin/env python3
"""License-free structural checks, not a replacement for importing/building in Unity."""
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "unity/Assets"
DEMO = ASSETS / "Aquarium"
errors = []
guids = {}
for path in ASSETS.rglob("*.meta"):
    match = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8-sig"), re.M)
    if not match:
        errors.append(f"Invalid GUID: {path.relative_to(ROOT)}")
        continue
    guid = match.group(1)
    if guid in guids:
        errors.append(f"Duplicate GUID: {path.relative_to(ROOT)} and {guids[guid].relative_to(ROOT)}")
    guids[guid] = path
for path in [DEMO, *DEMO.rglob("*")]:
    if path.suffix != ".meta" and not Path(str(path) + ".meta").is_file():
        errors.append(f"Missing .meta: {path.relative_to(ROOT)}")
for path in DEMO.rglob("*.meta"):
    if not Path(str(path)[:-5]).exists():
        errors.append(f"Orphan .meta: {path.relative_to(ROOT)}")
assemblies = {}
for path in DEMO.rglob("*.asmdef"):
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if data["name"] in assemblies:
        errors.append(f"Duplicate assembly: {data['name']}")
    assemblies[data["name"]] = data
for name, data in assemblies.items():
    for reference in data.get("references", []):
        if reference.startswith("Aquarium.") and reference not in assemblies:
            errors.append(f"Unresolved assembly: {name} -> {reference}")
for scene_name in ("AquariumDemo.unity", "AquariumOnline.unity"):
    scene = DEMO / "Scenes" / scene_name
    if not scene.exists():
        errors.append(f"Missing scene: {scene_name}")
        continue
    for guid in re.findall(r"guid: ([0-9a-f]{32})", scene.read_text()):
        if guid not in guids:
            errors.append(f"Unresolved {scene_name} GUID: {guid}")
build = (ROOT / "unity/ProjectSettings/EditorBuildSettings.asset").read_text()
if "- enabled: 1\n    path: Assets/Aquarium/Scenes/AquariumDemo.unity" not in build:
    errors.append("Playable scene is not enabled in build settings")
for shader in ("ReefSolid", "ReefSprite"):
    if not (DEMO / f"Presentation/Resources/{shader}.shader").exists():
        errors.append(f"Missing runtime-retained shader: {shader}")
linker = ET.parse(DEMO / "Presentation/link.xml").getroot()
preserved = {node.attrib["fullname"] for node in linker.findall("assembly/type")}
for component in ("MeshFilter", "MeshRenderer", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider"):
    if "UnityEngine." + component not in preserved:
        errors.append(f"Missing primitive stripping protection: {component}")
if errors:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)
print(f"PASS: {len(guids)} asset GUIDs, {len(assemblies)} assemblies, demo scene and shader resources")
