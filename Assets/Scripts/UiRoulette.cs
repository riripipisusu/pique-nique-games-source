using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// HUD de la roulette : barre compacte (solde, mise, coup, jetons, boutons), joueurs et derniers numeros.
// Les mises se posent en cliquant sur le tapis 3D : cases, bordures = chevaux, coins = carres,
// bas des rangees = transversales / sixains. Anchor donne la geometrie du tapis (en pixels de case C).
public partial class Ui
{
    public const float C = 54;   // cote d'une case du tapis, en pixels
    VisualElement rtHud, rtPanel, rtPlayers, rtHistory, rtChipRow, rtButtons;
    Label rtTurn, rtInfo, rtPrison, rtTip;
    static List<string> rtSpots;
    readonly Dictionary<string, int> rtBets = new Dictionary<string, int>();
    int rtChip = 10, rtFor = -1, rtRound = -1;

    // Tapis vu du joueur : 0 a gauche, 12 colonnes de 3 numeros (3 en haut, 1 en bas).
    static float Col(int n) => C + (n - 1) / 3 * C;
    static float Row(int n) => (2 - (n - 1) % 3) * C;

    // Point du tapis ou se pose le jeton d'une mise.
    public static Vector2 Anchor(string key)
    {
        var p = key.Split(':');
        int A() => int.Parse(p[1]);
        int si = Array.IndexOf(Roulette.Simple, key);
        if (si >= 0) return new Vector2(C + si * 2 * C + C, 3.8f * C + 0.4f * C);
        switch (p[0])
        {
            case "P": { int n = A(); return n == 0 ? new Vector2(C / 2, 1.5f * C) : new Vector2(Col(n) + C / 2, Row(n) + C / 2); }
            case "C":
            {
                var ab = p[1].Split('-').Select(int.Parse).OrderBy(x => x).ToArray();
                if (ab[0] == 0) return new Vector2(C, Row(ab[1]) + C / 2);
                return ab[1] - ab[0] == 3 ? new Vector2(Col(ab[1]), Row(ab[0]) + C / 2) : new Vector2(Col(ab[0]) + C / 2, Row(ab[0]));
            }
            case "T": return p[1] == "0a" ? new Vector2(C, 2 * C) : p[1] == "0b" ? new Vector2(C, C) : new Vector2(C + A() * C + C / 2, 3 * C);
            case "Q": return A() == 0 ? new Vector2(C, 3 * C) : new Vector2(Col(A()) + C, Row(A()));
            case "S": return new Vector2(C + (A() + 1) * C, 3 * C);
            case "D": return new Vector2(C + A() * 4 * C + 2 * C, 3.4f * C);
            default: return new Vector2(13.5f * C, (2 - A()) * C + C / 2);   // colonnes "2 a 1"
        }
    }

    void BuildRouletteHud()
    {
        rtHud = Div(hud, "layer");
        rtHud.pickingMode = PickingMode.Ignore;
        rtTurn = Text(rtHud, "", "bj-turn");
        rtTurn.pickingMode = PickingMode.Ignore;
        rtPlayers = Div(rtHud, "rt-players");
        rtHistory = Div(rtHud, "rt-history");

        rtPanel = Div(rtHud, "rt-panel");
        var top = Div(rtPanel, "row", "spread");
        rtInfo = Text(top, "", "rt-info");
        rtChipRow = Div(top, "row");
        foreach (int v in new[] { 10, 50, 100, 500 })
        {
            int val = v;
            var chip = Btn(rtChipRow, "", () => { rtChip = val; Sound.I.Play("bj_chip1"); RefreshRoulette(); }, "chip-btn", "rt-chipbtn");
            chip.style.backgroundImage = Resources.Load<Texture2D>("Casino/chip_" + v);
            chip.userData = v;
        }
        rtPrison = Text(rtPanel, "", "rt-prison");
        rtTip = Text(rtHud, "", "rt-tip");
        rtTip.pickingMode = PickingMode.Ignore;
        rtButtons = Div(rtPanel, "row");
        rtButtons.style.justifyContent = Justify.Center;
        Btn(rtButtons, "Effacer", () => { if (rtBets.Count > 0) Sound.I.Play("bj_collect"); rtBets.Clear(); RefreshRoulette(); }, "ghost", "small");
        Btn(rtButtons, "Rejouer", () =>
        {
            var cur = game.rt.Current;
            var last = Roulette.Parse(cur.lastBets) ?? new List<RBet>();
            if (last.Sum(b => b.amount) > cur.chips) return;
            rtBets.Clear();
            foreach (var b in last) rtBets[b.key] = b.amount;
            Sound.I.Play("bj_chips");
            RefreshRoulette();
        }, "ghost", "small");
        Btn(rtButtons, "Passer", () => game.Act("bets|"), "ghost", "small");
        Btn(rtButtons, "Valider", () =>
        {
            if (rtBets.Count == 0) return;
            game.Act("bets|" + Roulette.Format(rtBets.Select(kv => new RBet { key = kv.Key, amount = kv.Value })));
        }, "green", "small");
    }

    // Mise sous un point du tapis : les jonctions (chevaux, carres, transversales, sixains) d'abord, puis les cases.
    public static string KeyAt(Vector2 a)
    {
        if (rtSpots == null)
        {
            rtSpots = new List<string> { "C:0-1", "C:0-2", "C:0-3", "T:0a", "T:0b", "Q:0" };
            for (int n = 1; n <= 36; n++)
            {
                if (n <= 33) rtSpots.Add($"C:{n}-{n + 3}");
                if (n % 3 != 0) rtSpots.Add($"C:{n}-{n + 1}");
                if (n % 3 != 0 && n <= 32) rtSpots.Add("Q:" + n);
            }
            for (int r = 0; r < 12; r++) rtSpots.Add("T:" + r);
            for (int r = 0; r < 11; r++) rtSpots.Add("S:" + r);
        }
        string best = null; float bd = 0.2f * C;
        foreach (var k in rtSpots) { float d = Vector2.Distance(Anchor(k), a); if (d < bd) { bd = d; best = k; } }
        if (best != null) return best;
        if (a.y < 0 || a.x < 0 || a.x > 14 * C || a.y > 4.6f * C) return null;
        if (a.y < 3 * C)
        {
            if (a.x < C) return "P:0";
            if (a.x >= 13 * C) return "L:" + (2 - (int)(a.y / C));
            return "P:" + (3 * (int)((a.x - C) / C) + (2 - (int)(a.y / C)) + 1);
        }
        if (a.x < C || a.x >= 13 * C) return null;
        return a.y < 3.8f * C ? "D:" + (int)((a.x - C) / (4 * C)) : Roulette.Simple[(int)((a.x - C) / (2 * C))];
    }

    // Survol et clics sur le tapis 3D (appele par la camera de la roulette).
    public void RouletteMouse(Camera cam, bool active)
    {
        var r = game.rt;
        if (!active) { rtTip.style.display = DisplayStyle.None; return; }
        bool mine = r != null && !game.busy && game.MyTurn && !r.Finished && r.Current != null;
        string key = null;
        var m = (Vector2)Input.mousePosition;
        if (mine && !OverUi(m))
        {
            var at = game.rview.LayoutAt(cam.ScreenPointToRay(m));
            if (at.HasValue) key = KeyAt(at.Value);
        }
        game.rview.Highlight(key);
        rtTip.style.display = key != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (key == null) return;
        var pp = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(m.x, UnityEngine.Screen.height - m.y));
        rtTip.style.left = pp.x + 22; rtTip.style.top = pp.y + 18;
        rtBets.TryGetValue(key, out int on);
        rtTip.text = $"{char.ToUpper(Roulette.Label(key)[0])}{Roulette.Label(key).Substring(1)}  ·  paie {Roulette.Payout(key)} contre 1" + (on > 0 ? $"  ·  misé {on}" : "");
        if (Input.GetMouseButtonDown(1)) { if (rtBets.Remove(key)) { Sound.I.Play("bj_slide1"); RefreshRoulette(); } }
        else if (Input.GetMouseButtonDown(0))
        {
            if (rtBets.Values.Sum() + rtChip > r.Current.chips) { Say("Pas assez de jetons !"); return; }
            rtBets[key] = on + rtChip;
            Sound.I.Play(UnityEngine.Random.value < 0.5f ? "bj_place1" : "bj_place2");
            RefreshRoulette();
        }
    }

    bool OverUi(Vector2 screen)
    {
        if (root.panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));
        return root.panel.Pick(p) != null;
    }

    void ResetRouletteBets() { rtBets.Clear(); rtFor = rtRound = -1; }

    void RefreshRoulette()
    {
        var r = game.rt;
        if (r == null) return;
        var cur = r.Current;
        if (cur != null && (cur.seat != rtFor || r.round != rtRound)) { rtBets.Clear(); rtFor = cur.seat; rtRound = r.round; }
        bool mine = !game.busy && game.MyTurn && !r.Finished && cur != null;

        rtPlayers.Clear();
        foreach (var p in r.players)
        {
            var row = Div(rtPlayers, "rt-player");
            row.EnableInClassList("active", r.Actor == p.seat);
            var por = Portrait(row, Avatar(p.seat), null, 46);
            por.style.borderTopColor = por.style.borderBottomColor = por.style.borderLeftColor = por.style.borderRightColor = Board.Colors[p.seat];
            var col = Div(row);
            Text(col, p.name, "seat-name");
            int jail = p.prison.Sum(b => b.amount);
            Text(col, p.broke ? "ruiné" : $"{p.chips} jetons" + (jail > 0 ? $"  ·  {jail} en prison" : ""), "seat-chips");
        }
        rtHistory.Clear();
        foreach (var n in r.history.Skip(Math.Max(0, r.history.Count - 12)).Reverse())
            Text(rtHistory, n.ToString(), "rt-hist", n == 0 ? "green" : Roulette.IsRed(n) ? "red" : "black");

        string who = cur != null ? $"<color={Hex(Board.Colors[cur.seat])}>{cur.name}</color>" : "";
        rtTurn.text = r.Finished ? "" : game.busy ? "" : mine ? $"{who}, faites vos jeux !" : cur != null ? $"Au tour de {who}..." : "";
        rtPanel.style.display = mine ? DisplayStyle.Flex : DisplayStyle.None;
        if (!mine) return;

        int total = rtBets.Values.Sum();
        rtInfo.text = $"Solde : <b>{cur.chips - total}</b>   ·   Mise : <b>{total}</b>   ·   Coup <b>{r.round}/{r.rounds}</b>" + (total == 0 ? "   ·   <i>clique sur le tapis pour miser</i>" : "");
        foreach (var b in rtChipRow.Children()) b.EnableInClassList("rt-sel", (int)b.userData == rtChip);
        rtPrison.text = cur.prison.Count > 0 ? "En prison : " + string.Join(", ", cur.prison.Select(b => $"{Roulette.Label(b.key)} {b.amount}")) : "";
        game.rview.ShowBets(cur.seat, rtBets);
    }
}
