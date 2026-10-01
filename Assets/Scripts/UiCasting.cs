using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Outil annexe "casting" (lancer le jeu avec -casting, ou Casting.bat) : on cree les 5 personnages de
// l'illustration de l'accueil, avec leurs noms, on genere l'image, et on l'enregistre dans le projet :
//   Assets/Resources/MenuCast.txt                         les 5 personnages (nom|code), versionne
//   Assets/Synty/MMRes/Resources/UI/MenuArt.png           l'illustration (rendu des personnages Synty, non versionne)
// Au prochain build, tous les joueurs ont cette meme illustration.
public partial class Ui
{
    VisualElement castingScreen, castList;
    Label castStatus;
    public readonly List<(string name, string code)> cast = new List<(string, string)>();
    static readonly string[] CastRoles = { "Au centre : abat un +4", "À gauche : vient de piocher 4 cartes", "À droite : applaudit", "Au fond à gauche : réfléchit", "Au fond à droite : danse" };

    public static List<(string name, string code)> LoadCast(string text)
    {
        var list = new List<(string, string)>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var p = line.Trim().Split('|');
            if (p.Length == 2 && Sidekick.IsCode(p[1])) list.Add((p[0], p[1]));
        }
        return list;
    }

    static string ProjectAssets => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Assets"));

    public void OpenCasting()
    {
        if (castingScreen == null) BuildCasting();
        cast.Clear();
        cast.AddRange(LoadCast(Resources.Load<TextAsset>("MenuCast")?.text));
        var friends = new List<string>(Chars.Friends.Keys);
        while (cast.Count < 5) cast.Add(("Perso " + (cast.Count + 1), Chars.Friends[friends[cast.Count % friends.Count]]));
        RefreshCasting();
        RefreshProfile();
        history.Clear();
        foreach (var s in All) Hide(s);
        current = null;
        Go(castingScreen, false);
    }

    void BuildCasting()
    {
        castingScreen = Page(out var body, out var foot, "Casting", "de l'accueil");
        var box = Div(body, "m-box", "cast-box");
        Text(box, "Les 5 personnages de l'illustration de l'accueil. Clique sur un portrait pour le modifier.", "m-box-text");
        castList = Div(box);
        castStatus = Text(body, "", "m-status");
        Ico(Btn(foot, "Quitter", Application.Quit, "m-dark", "small"), "exit");
        Div(foot, "grow");
        Ico(Btn(foot, "Aperçu", () => game.RenderCastArt(null), "m-blue"), "play");
        Ico(Btn(foot, "Enregistrer dans le projet", SaveCasting, "m-gold"), "check");
    }

    void RefreshCasting()
    {
        castList.Clear();
        for (int i = 0; i < cast.Count; i++)
        {
            int k = i;
            var row = Div(castList, "m-strip", "m-player");
            Portrait(row, cast[i].code, () => OpenCreator(a => { cast[k] = (cast[k].name, a); Go(castingScreen, false); RefreshCasting(); }, cast[k].code), 64);
            var f = Add(row, new TextField { value = cast[i].name, maxLength = 20 }, "m-field");
            f.RegisterValueChangedCallback(e => cast[k] = (e.newValue, cast[k].code));
            Text(row, CastRoles[i], "m-strip-label").style.marginLeft = 20;
        }
    }

    void SaveCasting()
    {
        try
        {
            var txt = string.Join("\n", cast.ConvertAll(c => c.name.Replace("|", " ") + "|" + c.code));
            File.WriteAllText(Path.Combine(ProjectAssets, "Resources", "MenuCast.txt"), txt);
            var dir = Path.Combine(ProjectAssets, "Synty", "MMRes", "Resources", "UI");
            Directory.CreateDirectory(dir);
            game.RenderCastArt(Path.Combine(dir, "MenuArt.png"));
            castStatus.text = "Enregistré dans le projet : l'illustration sera dans le prochain build.";
        }
        catch (Exception e) { castStatus.text = "Erreur : " + e.Message; }
    }
}
