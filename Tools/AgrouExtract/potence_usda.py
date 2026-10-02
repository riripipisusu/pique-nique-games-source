# Acteur Pendaison d'Agrou (Blueprint, cf. pendaison.json) reconstitue en USD pour convert_maps / map2fbx :
# potence, trappes, torches, et des reperes (SPHERE_1/2 = pieds des condamnes, CABLE_1/2 = haut des cordes, CAMERA).
# Positions Unreal (cm, X avant, Y droite, Z haut) -> USD de CUE4Parse : (x, -y, z), lacet oppose.
# python potence_usda.py <AgrouCache>
import math, os, sys

cache = sys.argv[1]
parts = [  # nom, maillage (relatif a Agrou/Content), position UE, lacet UE
    ("Pendaison1", "04_Blueprint/Potance/Potance_SM_Prop_Gallows_01", (0, -48.12, -109.0), -90),
    ("StaticMesh3", "04_Blueprint/Potance/Potance_SM_Prop_Gallows_01_001", (0, -48.08, -109.0), -90),
    ("TRAPPE_1", "02_Polygon_Asset/PolygonPirates/Meshes/Props/SM_Prop_Gallows_Trapdoor_01", (37.28, 68.35, 87.6), 90),
    ("TRAPPE_2", "02_Polygon_Asset/PolygonPirates/Meshes/Props/SM_Prop_Gallows_Trapdoor_01", (37.28, -50.70, 87.6), 90),
    ("TORCHE_1", "02_Polygon_Asset/PolygonDungeons/Meshes/Props/SM_Prop_TorchStick_01", (87.14, -200.11, 5.23), 0),
    ("TORCHE_2", "02_Polygon_Asset/PolygonDungeons/Meshes/Props/SM_Prop_TorchStick_01", (87.14, 219.28, 5.23), 0),
]
marks = [  # reperes (empties)
    ("SPHERE_1", (-0.58, 65.0, 217.0)), ("SPHERE_2", (-1.45, -48.81, 217.33)),
    ("CABLE_1", (0.70, 59.03, 398.44)), ("CABLE_2", (0.70, -38.0, 398.44)),
    ("CAMERA", (344.5, 0, 212.55)), ("SOL", (0, 0, -109.0)),
]

def xf(pos, yaw):
    x, y, z = pos
    a = math.radians(-yaw) / 2
    return (f'        uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:orient", "xformOp:scale"]\n'
            f'        float3 xformOp:translate = ({x}, {-y}, {z})\n'
            f'        quatf xformOp:orient = ({math.cos(a)}, 0, 0, {math.sin(a)})\n'
            f'        float3 xformOp:scale = (1, 1, 1)\n')

out = ['#usda 1.0\n(\n    defaultPrim = "Pendaison"\n    metersPerUnit = 0.01\n    upAxis = "Z"\n)\n\ndef Xform "Pendaison"\n{\n']
for name, mesh, pos, yaw in parts:
    out.append(f'    def Xform "{name}"\n    {{\n{xf(pos, yaw)}        def Mesh "{name}" (\n            prepend references = @../{mesh}.usda@\n        )\n        {{\n        }}\n    }}\n')
for name, pos in marks:
    out.append(f'    def Xform "{name}"\n    {{\n{xf(pos, 0)}    }}\n')
out.append('}\n')
open(os.path.join(cache, "Agrou", "Content", "Maps", "Pendaison.usda"), "w", encoding="utf-8").write("".join(out))
print("ok")
