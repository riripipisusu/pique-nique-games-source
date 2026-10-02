using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Loup-garou d'Agrou (exporte du pak Unreal, cf. Tools/AgrouExtract) : importe en humanoide pour jouer les clips
// de CharAnim (assis...). Unity -batchmode -executeMethod LoupSetup.Run
public static class LoupSetup
{
    const string Dir = "Assets/Resources/LoupGarou/Models/";

    // Os du squelette d'Agrou -> os humanoides Unity (l'auto-mapping confond Clavicle/Shoulder).
    static readonly (string bone, string human)[] Map =
    {
        ("Hips", "Hips"), ("Spine_01", "Spine"), ("Spine_02", "Chest"), ("Spine_03", "UpperChest"), ("Neck", "Neck"), ("Head", "Head"), ("Jaw", "Jaw"),
        ("UpperLeg_L", "LeftUpperLeg"), ("LowerLeg_L", "LeftLowerLeg"), ("Foot_L", "LeftFoot"), ("Toes_L", "LeftToes"),
        ("UpperLeg_R", "RightUpperLeg"), ("LowerLeg_R", "RightLowerLeg"), ("Foot_R", "RightFoot"), ("Toes_R", "RightToes"),
        ("Clavicle_L", "LeftShoulder"), ("Shoulder_L", "LeftUpperArm"), ("Elbow_L", "LeftLowerArm"), ("Hand_L", "LeftHand"),
        ("Clavicle_R", "RightShoulder"), ("Shoulder_R", "RightUpperArm"), ("Elbow_R", "RightLowerArm"), ("Hand_R", "RightHand"),
        ("Thumb_01_L", "Left Thumb Proximal"), ("Thumb_02_L", "Left Thumb Intermediate"), ("Thumb_03_L", "Left Thumb Distal"),
        ("IndexFinger_01_L", "Left Index Proximal"), ("IndexFinger_02_L", "Left Index Intermediate"), ("IndexFinger_03_L", "Left Index Distal"),
        ("MiddleFinger_01_L", "Left Middle Proximal"), ("MiddleFinger_02_L", "Left Middle Intermediate"), ("MiddleFinger_03_L", "Left Middle Distal"),
        ("Thumb_01_R", "Right Thumb Proximal"), ("Thumb_02_R", "Right Thumb Intermediate"), ("Thumb_03_R", "Right Thumb Distal"),
        ("IndexFinger_01_R", "Right Index Proximal"), ("IndexFinger_02_R", "Right Index Intermediate"), ("IndexFinger_03_R", "Right Index Distal"),
        ("MiddleFinger_01_R", "Right Middle Proximal"), ("MiddleFinger_02_R", "Right Middle Intermediate"), ("MiddleFinger_03_R", "Right Middle Distal"),
    };

    public static void Run()
    {
        var tex = (TextureImporter)AssetImporter.GetAtPath(Dir + "werewolf_texture.png");
        if (tex) { tex.isReadable = true; tex.filterMode = FilterMode.Point; tex.mipmapEnabled = false; tex.textureCompression = TextureImporterCompression.Uncompressed; tex.SaveAndReimport(); }

        var imp = (ModelImporter)AssetImporter.GetAtPath(Dir + "werewolf.fbx");
        if (!imp) { Debug.LogError("LoupSetup : werewolf.fbx absent"); return; }
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.SaveAndReimport();   // premier passage : Unity remplit le squelette (humanDescription.skeleton)
        var hd = imp.humanDescription;
        hd.human = Map.Select(m => new HumanBone { boneName = m.bone, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        imp.humanDescription = hd;
        imp.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(Dir + "werewolf.fbx").OfType<Avatar>().FirstOrDefault();
        Debug.Log("LoupSetup : avatar " + (avatar && avatar.isValid && avatar.isHuman ? "humanoide OK" : "INVALIDE"));
    }

    // --- Animations des persos d'Agrou (squelette Synty Modular) : couche "haut du corps" de CharAnim -----------------
    const string AnimDir = "Assets/Resources/Agrou/Anims/";
    static readonly (string bone, string human)[] SyntyMap =
    {
        ("Pelvis", "Hips"), ("spine_01", "Spine"), ("spine_02", "Chest"), ("spine_03", "UpperChest"), ("neck_01", "Neck"), ("head", "Head"),
        ("clavicle_l", "LeftShoulder"), ("UpperArm_L", "LeftUpperArm"), ("lowerarm_l", "LeftLowerArm"), ("Hand_L", "LeftHand"),
        ("clavicle_r", "RightShoulder"), ("UpperArm_R", "RightUpperArm"), ("lowerarm_r", "RightLowerArm"), ("Hand_R", "RightHand"),
        ("Thigh_L", "LeftUpperLeg"), ("calf_l", "LeftLowerLeg"), ("Foot_L", "LeftFoot"), ("ball_l", "LeftToes"),
        ("Thigh_R", "RightUpperLeg"), ("calf_r", "RightLowerLeg"), ("Foot_R", "RightFoot"), ("ball_r", "RightToes"),
        ("thumb_01_l", "Left Thumb Proximal"), ("thumb_02_l", "Left Thumb Intermediate"), ("thumb_03_l", "Left Thumb Distal"),
        ("indexFinger_01_l", "Left Index Proximal"), ("indexFinger_02_l", "Left Index Intermediate"), ("indexFinger_03_l", "Left Index Distal"),
        ("finger_01_l", "Left Middle Proximal"), ("finger_02_l", "Left Middle Intermediate"), ("finger_03_l", "Left Middle Distal"),
        ("thumb_01_r", "Right Thumb Proximal"), ("thumb_02_r", "Right Thumb Intermediate"), ("thumb_03_r", "Right Thumb Distal"),
        ("indexFinger_01_r", "Right Index Proximal"), ("indexFinger_02_r", "Right Index Intermediate"), ("indexFinger_03_r", "Right Index Distal"),
        ("finger_01_r", "Right Middle Proximal"), ("finger_02_r", "Right Middle Intermediate"), ("finger_03_r", "Right Middle Distal"),
    };
    // Gestes en boucle (les autres se jouent une fois ; les poses d'une image restent tenues).
    static readonly string[] Loops = { "Idle_Aiming_Anim_mixamo_com", "Sleep", "Sitting_Angry_Anim_mixamo_com", "Votecontresoit", "Levelebras" };

    // Copie les FBX d'AgrouCache/animsfbx (Tools/AgrouExtract) et les regle en humanoide. -executeMethod LoupSetup.Anims
    public static void Anims()
    {
        if (!Directory.Exists("AgrouCache/animsfbx")) return;
        Directory.CreateDirectory(AnimDir);
        foreach (var f in Directory.GetFiles("AgrouCache/animsfbx", "*.fbx")) File.Copy(f, AnimDir + Path.GetFileName(f), true);
        AssetDatabase.Refresh();
        foreach (var path in Directory.GetFiles(AnimDir, "*.fbx").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.SaveAndReimport();
            var hd = imp.humanDescription;
            hd.human = SyntyMap.Select(m => new HumanBone { boneName = m.bone, humanName = m.human, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            imp.humanDescription = hd;
            string name = Path.GetFileNameWithoutExtension(path);
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name; c.loopTime = Loops.Contains(name) || c.lastFrame - c.firstFrame < 2;
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (!av || !av.isHuman) Debug.LogWarning("LoupSetup : avatar non humanoide " + path);
        }
    }

    // Couche 1 de CharAnim : haut du corps (bras, tronc, tete), un etat "wg_<clip>" par animation d'Agrou ; poids pilote par le jeu.
    public static void AgrouLayer(AnimatorController ctrl)
    {
        if (!Directory.Exists(AnimDir)) return;
        string maskPath = "Assets/Resources/Agrou/HautDuCorps.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (!mask)
        {
            mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            // Ni bassin ni colonne (Body) : les gestes d'Agrou sont joues debout, le perso doit rester assis.
            foreach (var b in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg, AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                mask.SetHumanoidBodyPartActive(b, false);
            AssetDatabase.CreateAsset(mask, maskPath);
        }
        ctrl.AddLayer(new AnimatorControllerLayer { name = "Agrou", defaultWeight = 0, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = new AnimatorStateMachine { name = "Agrou" } });
        var layer = ctrl.layers.Last();
        AssetDatabase.AddObjectToAsset(layer.stateMachine, ctrl);
        var sm = layer.stateMachine;
        sm.defaultState = sm.AddState("Rien");
        foreach (var path in Directory.GetFiles(AnimDir, "*.fbx"))
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path.Replace(Path.DirectorySeparatorChar, '/')).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (clip) sm.AddState("wg_" + clip.name).motion = clip;
        }
        var ls = ctrl.layers; ls[ls.Length - 1] = layer; ctrl.layers = ls;
    }
}
