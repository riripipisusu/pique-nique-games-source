using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// HUD de la roulette : tapis francais cliquable (cases, bordures = chevaux, coins = carres,
// bas des rangees = transversales / sixains), jetons, joueurs et derniers numeros sortis.
public partial class Ui
{
    public const float C = 54;   // cote d'une case du tapis, en pixels
    VisualElement rtHud, rtPanel, rtBoard, rtChips, rtPlayers, rtHistory, rtChipRow, rtButtons;
    Label rtTurn, rtInfo, rtPrison;
    readonly Dictionary<int, VisualElement> rtCells = new Dictionary<int, VisualElement>();
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
        rtBoard = Div(rtPanel, "rt-board");
        rtBoard.style.width = 14 * C;
        rtBoard.style.height = 4.6f * C;

        void Cell(string key, float x, float y, float w, float h, string label, string cls, IEnumerable<int> nums = null)
        {
            var e = Div(rtBoard, "rt-cell", cls);
            e.style.left = x; e.style.top = y; e.style.width = w; e.style.height = h;
            Text(e, label, "rt-num").pickingMode = PickingMode.Ignore;
            Hook(e, key);
            if (nums != null) foreach (var n in nums) rtCells[n] = e;
        }
        Cell("P:0", 0, 0, C, 3 * C, "0", "green", new[] { 0 });
        for (int n = 1; n <= 36; n++) Cell("P:" + n, Col(n), Row(n), C, C, n.ToString(), Roulette.IsRed(n) ? "red" : "black", new[] { n });
        for (int k = 0; k < 3; k++) Cell("L:" + k, 13 * C, (2 - k) * C, C, C, "2 à 1", "out");
        string[] dz = { "12 P", "12 M", "12 D" };
        for (int d = 0; d < 3; d++) Cell("D:" + d, C + d * 4 * C, 3 * C, 4 * C, 0.8f * C, dz[d], "out");
        string[] simple = { "Manque", "Pair", "", "", "Impair", "Passe" };
        for (int i = 0; i < 6; i++) Cell(Roulette.Simple[i], C + i * 2 * C, 3.8f * C, 2 * C, 0.8f * C, simple[i], i == 2 ? "red-chance" : i == 3 ? "black-chance" : "out");

        // Zones entre les cases : chevaux, carres, transversales, sixains.
        var spots = new List<string> { "C:0-1", "C:0-2", "C:0-3", "T:0a", "T:0b", "Q:0" };
        for (int n = 1; n <= 36; n++)
        {
            if (n <= 33) spots.Add($"C:{n}-{n + 3}");
            if (n % 3 != 0) spots.Add($"C:{n}-{n + 1}");
            if (n % 3 != 0 && n <= 32) spots.Add("Q:" + n);
        }
        for (int r = 0; r < 12; r++) spots.Add("T:" + r);
        for (int r = 0; r < 11; r++) spots.Add("S:" + r);
        foreach (var key in spots)
        {
            var a = Anchor(key);
            var e = Div(rtBoard, "rt-spot");
            e.style.left = a.x - 9; e.style.top = a.y - 9;
            Hook(e, key);
        }
        rtChips = Div(rtBoard, "layer");
        rtChips.pickingMode = PickingMode.Ignore;

        rtPrison = Text(rtPanel, "", "rt-prison");
        rtButtons = Div(rtPanel, "row");
        rtButtons.style.justifyContent = Justify.Center;
        Btn(rtButtons, "Effacer", () => { rtBets.Clear(); RefreshRoulette(); }, "ghost", "small");
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

    // Clic gauche : pose un jeton ; clic droit : retire la mise. Survol : les numeros couverts s'allument.
    void Hook(VisualElement e, string key)
    {
        var nums = Roulette.Numbers(key);
        e.RegisterCallback<PointerEnterEvent>(_ => { foreach (var n in nums) if (rtCells.TryGetValue(n, out var c)) c.AddToClassList("hl"); });
        e.RegisterCallback<PointerLeaveEvent>(_ => { foreach (var c in rtCells.Values) c.RemoveFromClassList("hl"); });
        e.RegisterCallback<PointerDownEvent>(ev =>
        {
            var r = game.rt;
            if (r == null || game.busy || !game.MyTurn || r.Current == null) return;
            if (ev.button == 1) { if (rtBets.Remove(key)) Sound.I.UI("tick"); }
            else if (ev.button == 0 && rtBets.Values.Sum() + rtChip <= r.Current.chips)
            {
                rtBets[key] = rtBets.TryGetValue(key, out int a) ? a + rtChip : rtChip;
                Sound.I.Play("bj_chip1");
            }
            RefreshRoulette();
        });
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
        rtInfo.text = $"Solde : <b>{cur.chips - total}</b>   ·   Mise : <b>{total}</b>   ·   Coup <b>{r.round}/{r.rounds}</b>";
        foreach (var b in rtChipRow.Children()) b.EnableInClassList("rt-sel", (int)b.userData == rtChip);
        rtPrison.text = cur.prison.Count > 0 ? "En prison : " + string.Join(", ", cur.prison.Select(b => $"{Roulette.Label(b.key)} {b.amount}")) : "";
        game.rview.ShowBets(cur.seat, rtBets);
        rtChips.Clear();
        foreach (var kv in rtBets)
        {
            var a = Anchor(kv.Key);
            var chip = Text(rtChips, kv.Value.ToString(), "rt-chip");
            chip.style.left = a.x - 17; chip.style.top = a.y - 17;
            chip.pickingMode = PickingMode.Ignore;
        }
    }
}
