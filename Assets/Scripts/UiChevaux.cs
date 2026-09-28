using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Petits chevaux : reprend le HUD de Croque-Carotte (joueurs en haut, panneau d'action, fil des coups).
// Sur chaque carte joueur, une pastille par cheval : vide a l'ecurie, numero de case sur le parcours, "M1".."M6"
// dans l'escalier, pleine a sa couleur une fois au centre.
public partial class Ui
{
    // Le bouton d'action sert a piocher (Croque-Carotte) ou a lancer le de (petits chevaux).
    void DrawLabel(string text, string icon)
    {
        var l = drawBtn.Q<Label>(className: "gl-label");
        if (l.text == text) return;
        l.text = text;
        Ico(drawBtn, icon);
    }

    void RefreshChevaux()
    {
        var c = game.ch;
        DrawLabel("Lancer le dé", "dice");
        cycle.style.display = DisplayStyle.None;
        playersBar.Clear();
        for (int i = 0; i < c.players.Count; i++)
        {
            var p = c.players[i];
            int col = c.ColorOf(i);
            var pc = Div(playersBar, "pcard");
            pc.EnableInClassList("active", i == c.turn && !c.Finished);
            Ring(Portrait(pc, Avatar(i), null, 52), Board.Colors[col]);
            Text(pc, p.name, "pname").style.color = Board.Colors[col];
            foreach (int h in p.horses.OrderByDescending(x => x))
            {
                var pip = Text(pc, h < 0 || h == Chevaux.Home ? "" : h > Chevaux.Foot ? "M" + (h - Chevaux.Foot) : (h + 1).ToString(), "pip");
                if (h == Chevaux.Home) pip.style.backgroundColor = Board.Colors[col];
                else if (h >= 0) pip.AddToClassList("field");
            }
        }

        bool mine = !game.busy && !c.Finished && game.MyTurn;
        drawBtn.style.display = c.dice == 0 && !c.sacrifice && mine ? DisplayStyle.Flex : DisplayStyle.None;
        var name = $"<color={Hex(Board.Colors[c.ColorOf(c.turn)])}>{c.Current.name}</color>";
        turn.text = c.Finished ? "Partie terminée !" : game.busy ? "..." : c.sacrifice ? $"Trois 6 ! {name} renvoie un cheval à l'écurie :" : c.dice == 0 ? $"Au tour de <b>{name}</b>" : $"{name} a fait <b>{c.dice}</b> : quel cheval ?";
        rabbitRow.Clear();
        if ((c.dice > 0 || c.sacrifice) && mine)
            for (int k = 0; k < c.count; k++)
            {
                int idx = k, h = c.Current.horses[k];
                string where = h < 0 ? "écurie" : h == Chevaux.Home ? "arrivé" : h > Chevaux.Foot ? "marche " + (h - Chevaux.Foot) : "case " + (h + 1);
                var b = Btn(rabbitRow, $"Cheval {k + 1}\n<size=17>{where}</size>", () => game.Move(idx));
                b.SetEnabled(c.CanPick(k));
                Face(b).style.backgroundColor = Board.Colors[c.ColorOf(c.turn)];
            }
        deck.text = c.count > 1 ? $"Rentre tes {c.count} chevaux au centre !" : "Rentre ton cheval au centre !";
        Feed(c.log);
    }
}
