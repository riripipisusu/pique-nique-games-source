using UnityEngine;
using UnityEngine.UIElements;

// Serpents et echelles : HUD de Croque-Carotte (joueurs en haut avec leur case, panneau d'action avec "Lancer le de").
public partial class Ui
{
    void RefreshSerpents()
    {
        var s = game.sp;
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
            var pip = Text(pc, p.pos > 0 ? p.pos.ToString() : "", "pip");
            if (p.pos > 0) pip.AddToClassList("field");
        }
        bool mine = !game.busy && !s.Finished && game.MyTurn;
        drawBtn.style.display = mine ? DisplayStyle.Flex : DisplayStyle.None;
        var name = $"<color={Hex(SerpentsView.ColorOf(s.turn))}>{s.Current.name}</color>";
        turn.text = s.Finished ? "Partie terminée !" : game.busy ? "..." : $"Au tour de <b>{name}</b>";
        rabbitRow.Clear();
        deck.text = "Premier sur la case 100 !";
        Feed(s.log);
    }
}
