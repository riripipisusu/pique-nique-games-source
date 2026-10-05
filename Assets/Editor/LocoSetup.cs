using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Deplacements des persos (marche, course, saut) tires du mannequin d'Agrou (AgrouCache/locofbx, cf. Tools/AgrouExtract :
// export FMT=usd puis anims2fbx.py). Hors depot comme le reste d'Agrou.
//   -executeMethod LocoSetup.Run   (importe les clips puis reconstruit CharAnim)
public static class LocoSetup
{
    const string AnimDir = "Assets/Resources/Loco/";

    // Squelette du mannequin Unreal (noms en minuscules, contrairement aux persos modulaires d'Agrou).
    static readonly (string bone, string human)[] Mannequin =
    {
        ("pelvis", "Hips"), ("spine_01", "Spine"), ("spine_02", "Chest"), ("spine_03", "UpperChest"), ("neck_01", "Neck"), ("head", "Head"),
        ("clavicle_l", "LeftShoulder"), ("upperarm_l", "LeftUpperArm"), ("lowerarm_l", "LeftLowerArm"), ("hand_l", "LeftHand"),
        ("clavicle_r", "RightShoulder"), ("upperarm_r", "RightUpperArm"), ("lowerarm_r", "RightLowerArm"), ("hand_r", "RightHand"),
        ("thigh_l", "LeftUpperLeg"), ("calf_l", "LeftLowerLeg"), ("foot_l", "LeftFoot"), ("ball_l", "LeftToes"),
        ("thigh_r", "RightUpperLeg"), ("calf_r", "RightLowerLeg"), ("foot_r", "RightFoot"), ("ball_r", "RightToes"),
        ("thumb_01_l", "Left Thumb Proximal"), ("thumb_02_l", "Left Thumb Intermediate"), ("thumb_03_l", "Left Thumb Distal"),
        ("index_01_l", "Left Index Proximal"), ("index_02_l", "Left Index Intermediate"), ("index_03_l", "Left Index Distal"),
        ("middle_01_l", "Left Middle Proximal"), ("middle_02_l", "Left Middle Intermediate"), ("middle_03_l", "Left Middle Distal"),
        ("thumb_01_r", "Right Thumb Proximal"), ("thumb_02_r", "Right Thumb Intermediate"), ("thumb_03_r", "Right Thumb Distal"),
        ("index_01_r", "Right Index Proximal"), ("index_02_r", "Right Index Intermediate"), ("index_03_r", "Right Index Distal"),
        ("middle_01_r", "Right Middle Proximal"), ("middle_02_r", "Right Middle Intermediate"), ("middle_03_r", "Right Middle Distal"),
    };

    // Etat de CharAnim -> clip, boucle ?
    static readonly (string state, string file, bool loop)[] States =
    {
        ("Walk", "ThirdPersonWalk", true), ("Run", "ThirdPersonRun", true), ("JumpStart", "ThirdPersonJump_Start", false),
        ("JumpLoop", "ThirdPersonJump_Loop", true), ("JumpEnd", "ThirdPersonJump_End", false),
    };

    public static void Run()
    {
        Import();
        CharSetup.Build();
    }

    static void Import()
    {
        if (!Directory.Exists("AgrouCache/locofbx")) { Debug.LogWarning("LocoSetup : AgrouCache/locofbx absent"); return; }
        Directory.CreateDirectory(AnimDir);
        foreach (var f in Directory.GetFiles("AgrouCache/locofbx", "*.fbx")) File.Copy(f, AnimDir + Path.GetFileName(f), true);
        AssetDatabase.Refresh();
        foreach (var (_, file, loop) in States)
        {
            string path = AnimDir + file + ".fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) continue;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.SaveAndReimport();
            var hd = imp.humanDescription;
            hd.human = Mannequin.Select(m => new HumanBone { boneName = m.bone, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            imp.humanDescription = hd;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = file; c.loopTime = loop;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (!av || !av.isHuman) Debug.LogWarning("LocoSetup : avatar non humanoide " + path);
        }
    }

    // Etats ajoutes a la couche de base de CharAnim (appele par CharSetup.Build).
    public static void AddStates(AnimatorController ctrl)
    {
        var sm = ctrl.layers[0].stateMachine;
        foreach (var (state, file, _) in States)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimDir + file + ".fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip) sm.AddState(state).motion = clip;
        }
    }
}
