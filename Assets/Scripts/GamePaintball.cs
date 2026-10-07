using System.Collections;
using System.Linq;
using UnityEngine;

// Mode Paintball (FPS en ligne, deux equipes) dans une map d'Agrou.
public partial class Game
{
    public Paintball pb;
    public PaintballView pbview;
    float pbStart;
    bool pbAuto;   // autotest : je suis pilote automatiquement
    public int MySeatOrZero => MySeatOr0;

    void StartPaintball(System.Collections.Generic.List<string> n, int opt, int seed, System.Collections.Generic.List<string> av)
    {
        pb = new Paintball(n, opt, seed);
        int me = MySeatOr0;
        // Hors ligne : tous les autres sieges sont des bots ; en ligne, chacun pilote le sien.
        // (Autotest : mon siege aussi, pour jouer seul ou en ligne sans personne au clavier.)
        var isBot = Enumerable.Range(0, n.Count).Select(s => Online ? pbAuto && s == me : s != me || pbAuto).ToArray();
        pbview.Build(pb, me, av, isBot, a => { if (pb != null && !pb.Finished) { if (Online) net.Act(a); else Apply(a); } }, m => { if (Online) net.SendRT(m); });
        pbStart = Time.time;
        ui.ShowHud();
        ui.Say($"Équipe {Paintball.TeamName[Paintball.TeamOf(me)]} ! Premiers à {pb.target} points.", 3);
    }

    public float PaintballLeft => pb == null ? 0 : Mathf.Max(0, Paintball.Minutes * 60 - (Time.time - pbStart));

    void ApplyPaintball(string[] p)
    {
        if (!pb.TryApply(p)) return;
        foreach (var e in pb.events)
        {
            pbview.OnEvent(e);
            if (e.type == PbEv.Hit) ui.PaintballFeed(e.by, e.seat);
            if (e.type == PbEv.Over) StartCoroutine(PaintballEnd());
        }
        ui.Refresh();
    }

    IEnumerator PaintballEnd() { yield return new WaitForSeconds(2.5f); pbview.InputOn = false; ui.ShowVictory(); }

    // Messages temps reel (positions, billes) : hors du systeme d'actions.
    public void OnRealtime(string s) { if (pb != null && pbview) pbview.OnRealtime(s.Split('|')); }

    void PaintballUpdate()
    {
        if (pb == null) return;
        pbview.InputOn = Focused && !paused && !pb.Finished;
        pbview.Sens = settings.camSens;
        // Fin du temps : decidee par l'hote (ou hors ligne).
        if (!pb.Finished && PaintballLeft <= 0 && (!Online || net.IsHost) && Idle) { if (Online) net.HostEnd(); else Apply("end"); }
    }

    void PaintballCamera()
    {
        var pose = pbview.CamPose;
        cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        cam.fieldOfView = 75;
        cam.nearClipPlane = 0.05f;
        ui.UpdatePaintball();
    }

    // Autotest : moi pilote automatiquement (vers l'ennemi le plus proche, tir quand il est en vue), captures.
    IEnumerator PaintballTest(string dir, System.Func<string, IEnumerator> shot)
    {
        pbAuto = true;
        float strafe = 1, nextStrafe = 0;
        pbview.Auto = v =>
        {
            var me = v.MyPos;
            var foes = v.Others.Where(o => Paintball.TeamOf(o.seat) != Paintball.TeamOf(MySeatOr0) && !o.down).OrderBy(o => (o.pos - me).sqrMagnitude).ToList();
            if (Time.time > nextStrafe) { nextStrafe = Time.time + Random.Range(0.6f, 1.5f); strafe = -strafe; }
            if (foes.Count == 0) { v.AutoFire = false; return new Vector3(strafe * 0.3f, 0, 1); }
            var to = foes[0].pos + Vector3.up * 1.1f - (me + Vector3.up * 1.6f);
            v.MyYaw = Mathf.MoveTowardsAngle(v.MyYaw, Quaternion.LookRotation(to).eulerAngles.y, 300 * Time.deltaTime);
            v.MyPitch = -Mathf.Asin(to.normalized.y) * Mathf.Rad2Deg;
            v.AutoFire = to.magnitude < 30 && !Physics.Linecast(me + Vector3.up * 1.6f, foes[0].pos + Vector3.up * 1.1f);
            return new Vector3(strafe * 0.6f, 0, to.magnitude > 10 ? 1 : 0);
        };
        yield return new WaitForSeconds(2); yield return shot("b1-debut");
        // Mon corps en baissant les yeux.
        pbview.LookDown = 65; yield return new WaitForSeconds(0.5f); yield return shot("b1-corps"); pbview.LookDown = null;
        // Un joueur vu de pres (lanceur en main, course) : camera de cote, quelques secondes apres le depart.
        yield return new WaitForSeconds(3);
        for (int k2 = 0; k2 < 2; k2++)
        {
            int s = (MySeatOr0 + 1 + k2) % pb.players.Count;
            var p = pbview.AvatarPos(s); var f = Quaternion.Euler(0, pbview.AvatarYaw(s), 0);
            var from = p + f * new Vector3(1.6f, 1.5f, 1.4f);
            tour = new Pose(from, Quaternion.LookRotation(p + Vector3.up * 1.2f - from));
            yield return new WaitForSeconds(0.3f); yield return shot($"b1-joueur{k2}");
        }
        tour = null;
        float t0 = Time.time; int k = 0;
        while (pb != null && !pb.Finished && Time.time - t0 < 150)
        {
            yield return new WaitForSeconds(12);
            yield return shot($"b2-jeu{k++}");
            if (pbview.Down) { yield return new WaitForSeconds(0.4f); yield return shot($"b3-touche{k}"); }
        }
        if (pb != null && !pb.Finished) Apply("end");
        yield return new WaitForSeconds(4); yield return shot("b4-fin");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "paintball.txt"), $"fini={pb?.Finished} score={pb?.score[0]}-{pb?.score[1]}\n"
            + string.Join("\n", pb.players.Select(p => $"{p.name} equipe={p.team} touches={p.hits} elimine={p.outs}")) + "\n---\n" + string.Join("\n", pb.log));
        pbview.Auto = null; pbview.AutoFire = false; pbAuto = false;
    }

    // Outil : releve d'une map d'Agrou (sol / obstacles autour du feu, vues du dessus) pour y placer l'arene.
    //   -mapprobe <n>  -> relief.txt + p-*.png
    IEnumerator MapProbe(string dir, System.Func<string, IEnumerator> shot, int map)
    {
        Clairiere.Show(true); Clairiere.Nature(false); Clairiere.Camp(true); Clairiere.HideFire(true); Clairiere.Day();
        var fire = Clairiere.Center + new Vector3(0, 0, 0.5f);
        AgrouMap.Show(map, fire);
        yield return null;
        int cols = 0;
        foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            var r = mf.GetComponent<Renderer>();
            if (!mf.sharedMesh || !r || !r.enabled || mf.GetComponent<Collider>() || !mf.sharedMesh.isReadable || r.bounds.SqrDistance(fire) > 90 * 90) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; cols++;
        }
        yield return null;
        // Par point (pas de 2 m) : hauteur du sol (plus bas impact) ; '.' libre, '#' obstacle (> 1,2 m au-dessus du sol), '~' rien.
        var sb = new System.Text.StringBuilder();
        const int R = 50, S = 2;
        for (int z = R; z >= -R; z -= S)
        {
            for (int x = -R; x <= R; x += S)
            {
                var hits = Physics.RaycastAll(fire + new Vector3(x, 60, z), Vector3.down, 140);
                if (hits.Length == 0) { sb.Append('~'); continue; }
                // Premier impact sous 8 m (pas les cimes des arbres geants) : sol (.), muret/caisse (+), toit/mur (#).
                var low = hits.Where(h => h.point.y < fire.y + 8).Select(h => h.point.y - fire.y).DefaultIfEmpty(-99).Max();
                sb.Append(low < -3 ? 'v' : low < 0.6f ? '.' : low < 1.6f ? '+' : '#');
            }
            sb.AppendLine();
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "relief.txt"), $"colliders={cols} map={AgrouMap.Active} (x de -{R} a {R}, z de {R} en haut a -{R}, pas {S})\n" + sb);
        ui.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement.style.display = UnityEngine.UIElements.DisplayStyle.None;
        RenderSettings.fog = false;
        cam.farClipPlane = 1000;
        foreach (var (name, h, ang, back) in new[] { ("haut", 110f, 90f, 0f), ("biais", 50f, 45f, 55f), ("biais2", 50f, 45f, -55f) })
        {
            tour = new Pose(fire + new Vector3(0, h, -back), Quaternion.Euler(ang, back < 0 ? 180 : 0, 0));
            yield return new WaitForSeconds(0.6f);
            yield return shot("p-" + name);
        }
        Application.Quit();
    }
}
