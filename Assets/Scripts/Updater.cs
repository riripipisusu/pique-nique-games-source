using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

// Mise a jour automatique : compare la version du jeu a la derniere "release" GitHub,
// telecharge le zip, puis un petit script remplace les fichiers une fois le jeu ferme.
public static class Updater
{
    public const string Repo = "OWNER/pique-nique-games";   // proprietaire/depot des releases
    const string Asset = "PiqueNiqueGames-Windows.zip";

    public static string Latest;
    static string zipUrl;

    [Serializable] class Release { public string tag_name; public ReleaseAsset[] assets; }
    [Serializable] class ReleaseAsset { public string name; public string browser_download_url; }

    public static bool IsNewer(string tag, string current) =>
        Version.TryParse(tag.TrimStart('v', 'V'), out var a) && Version.TryParse(current, out var b) && a > b;

    public static IEnumerator Check(Action<string> onNewVersion)
    {
        if (Repo.StartsWith("OWNER/")) yield break;
        using var req = UnityWebRequest.Get($"https://api.github.com/repos/{Repo}/releases/latest");
        req.SetRequestHeader("User-Agent", "PiqueNiqueGames");
        req.timeout = 10;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) yield break;
        var rel = JsonUtility.FromJson<Release>(req.downloadHandler.text);
        if (rel == null || !IsNewer(rel.tag_name, Application.version)) yield break;
        foreach (var a in rel.assets) if (a.name == Asset) zipUrl = a.browser_download_url;
        if (zipUrl == null) yield break;
        Latest = rel.tag_name;
        onNewVersion(Latest);
    }

    public static IEnumerator Install(Action<float> progress, Action<string> fail)
    {
        if (Application.isEditor || zipUrl == null) { fail("Mise à jour impossible depuis l'éditeur."); yield break; }
        string tmp = Path.Combine(Path.GetTempPath(), "PiqueNiqueGames_update");
        string files = Path.Combine(tmp, "files"), zip = Path.Combine(tmp, "update.zip");
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); Directory.CreateDirectory(files); }
        catch (Exception e) { fail(e.Message); yield break; }

        using (var req = new UnityWebRequest(zipUrl, "GET", new DownloadHandlerFile(zip), null))
        {
            req.SetRequestHeader("User-Agent", "PiqueNiqueGames");
            var op = req.SendWebRequest();
            while (!op.isDone) { progress(req.downloadProgress); yield return null; }
            if (req.result != UnityWebRequest.Result.Success) { fail("Téléchargement échoué : " + req.error); yield break; }
        }
        progress(1);

        string dir = Path.GetDirectoryName(Application.dataPath);
        string exe = Path.Combine(dir, "PiqueNiqueGames.exe");
        string bat = Path.Combine(tmp, "update.bat");
        try
        {
            ZipFile.ExtractToDirectory(zip, files);
            int pid = Process.GetCurrentProcess().Id;
            File.WriteAllText(bat,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"robocopy \"{files}\" \"{dir}\" /E /IS /IT /NFL /NDL /NJH /NJS /NP >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception e) { fail(e.Message); yield break; }
        Application.Quit();
    }
}
