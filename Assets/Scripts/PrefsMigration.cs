using UnityEngine;

// Le jeu s'appelait "Anastasia / Pique-Nique's Games" pour Windows (dossier des sauvegardes) ; il s'appelle
// maintenant "Pique-Nique". Au premier lancement, on recopie les anciens reglages (prenom, personnage, options).
static class PrefsMigration
{
    const string Old = @"HKCU\Software\Anastasia\Pique-Nique's Games";
    const string New = @"HKCU\Software\Pique-Nique\Pique-Nique's Games";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Run()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // Unity cree le nouveau dossier avant nous : on se fie a un marqueur, pas a son existence.
        if (Reg($"query \"{New}\" /v migre") == 0 || Reg($"query \"{Old}\"") != 0) return;   // deja fait, ou rien a recopier
        Reg($"copy \"{Old}\" \"{New}\" /s /f");
        Reg($"add \"{New}\" /v migre /t REG_DWORD /d 1 /f");
        Debug.Log("Reglages recopies depuis l'ancien dossier (Anastasia) : prenom " + PlayerPrefs.GetString("cc-name", "(vide)"));
#endif
    }

    static int Reg(string args)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("reg.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit(5000);
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
