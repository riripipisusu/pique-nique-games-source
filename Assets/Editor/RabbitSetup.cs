using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Lapin Poly Art (Malbers) pour Croque-Carotte : un prefab URP par pelage (Resources/Rabbits) et un controleur
// d'animation (Resources/RabbitAnim) qui garde les noms d'etats utilises par Board.
public static class RabbitSetup
{
    const string Dir = "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Rabbit/";
    const string Res = "Assets/Resources/";
    public static readonly string[] Coats = { "White", "Brown", "Brown White", "Common", "White Spots", "Black" };   // ordre = couleur du joueur (Board)

    // Etat du jeu -> clip Malbers (fichier, nom), boucle ?
    static readonly (string state, string file, string clip, bool loop)[] States =
    {
        ("Idle", "Rabbit_Idle", "Rab_Idle01", true),
        ("Duck", "Rabbit_Jump", "Rab_Land_InPlace", false),        // se ramasse avant de bondir
        ("Jump", "Rabbit_Run", "Rab_Run", true),                    // un bond de course par case
        ("Yes", "Rabbit_Idle", "Rab_Idle_Stand", false),            // dresse sur ses pattes au potager
        ("Sitting_Eating", "Rabbit_Actions", "Rab_Eat", true),
        ("HitReact", "Rabbit_Damaged", "Stun", false),
        ("Death", "Rabbit_Fall", "Rab_Fall_High", false),           // chute dans le trou
        ("Wave", "Rabbit_Jump", "Rab_Jump_InPlace", true),          // gagnant : saute de joie
        ("No", "Rabbit_Sleep", "Rab_Lie01", true),                  // perdants : couches, deçus
    };

    public static void Build()
    {
        Directory.CreateDirectory(Res + "Rabbits");
        Directory.CreateDirectory(Res + "RabbitAnim");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var coat in Coats)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "Models/Rabbit PA " + coat + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    string path = Res + "Rabbits/" + m.name + ".mat";
                    var u = new Material(lit) { name = m.name };
                    u.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
                    u.SetColor("_BaseColor", Color.white);   // la teinte Malbers (~0.7) assombrit trop sous URP
                    u.SetFloat("_Smoothness", 0.15f);
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(u, path);
                    return u;
                }).ToArray();
            PrefabUtility.SaveAsPrefabAsset(go, Res + "Rabbits/" + coat + ".prefab");
            Object.DestroyImmediate(go);
        }

        string ctrlPath = Res + "RabbitAnim.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var (state, file, clip, loop) in States)
        {
            var src = AssetDatabase.LoadAllAssetsAtPath(Dir + "Anims/" + file + ".FBX").OfType<AnimationClip>().First(c => c.name == clip);
            var copy = Object.Instantiate(src);
            copy.name = state;
            var s = AnimationUtility.GetAnimationClipSettings(copy);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, s);
            string p = Res + "RabbitAnim/" + state + ".anim";
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(copy, p);
            var st = sm.AddState(state);
            st.motion = copy;
            if (state == "Idle") sm.defaultState = st;
        }
        AssetDatabase.SaveAssets();
        var b = AssetDatabase.LoadAssetAtPath<GameObject>(Res + "Rabbits/Common.prefab").GetComponentInChildren<SkinnedMeshRenderer>();
        Debug.Log("DIAG lapin : bones " + string.Join(",", b.bones.Select(x => x.name).Take(40)));
    }
}
