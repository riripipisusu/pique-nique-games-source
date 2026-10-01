using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Serpents et echelles : HUD de Croque-Carotte (joueurs en haut avec leur case, panneau d'action avec "Lancer le de").
public partial class Ui
{
    // Pendant le lancer et le deplacement, les cases et le journal gardent l'etat d'avant (sinon la case d'arrivee
    // s'affichait avant meme le resultat du de).
    int[] spShownPos;
    int spShownLog;

    void RefreshSerpents()
    {
        var s = game.sp;
        if (spShownPos == null || spShownPos.Length != s.players.Count || !game.busy)
        {
            spShownPos = s.players.Select(p => p.pos).ToArray();
            spShownLog = s.log.Count;
        }
        DrawLabel("Lancer le dé", "dice");
        cycle.style.display = DisplayStyle.None;
        playersBar.Clear();
        for (int i = 0; i < s.players.Count; i++)
        {
            var p = s.players[i];
            var col = SerpentsView.ColorOf(i);
            var pc = Div(playersBar, "pcard");
            pc.EnableInClassList("active", i == s.turn && !s.Finished);
            Ring(Portrait(pc, Avatar(i), null, 52), col);
            Text(pc, p.name, "pname").style.color = col;
            int pos = spShownPos[i];
            var pip = Text(pc, pos > 0 ? pos.ToString() : "", "pip");
            if (pos > 0) pip.AddToClassList("field");
        }
        bool mine = !game.busy && !s.Finished && game.MyTurn;
        drawBtn.style.display = mine ? DisplayStyle.Flex : DisplayStyle.None;
        var name = $"<color={Hex(SerpentsView.ColorOf(s.turn))}>{s.Current.name}</color>";
        turn.text = s.Finished ? "Partie terminée !" : game.busy ? "..." : $"Au tour de <b>{name}</b>";
        rabbitRow.Clear();
        deck.text = "Premier sur la case 100 !";
        Feed(s.log.Take(spShownLog).ToList());
    }
}
