using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Le pouilleux : vignettes au-dessus des eventails (nom, nombre de cartes, "Tranquille !"), ma main en bas (cartes du
// jeu classique, face visible), et la consigne du tour.
public partial class Ui
{
    VisualElement pqHud, pqTags, pqHand;
    Label pqStatus;
    public int pqNew = -1;   // derniere carte piochee (surlignee dans ma main)
    readonly List<VisualElement> pqTagEls = new List<VisualElement>();

    void BuildPouilleuxHud()
    {
        pqHud = Div(hud, "layer"); pqHud.pickingMode = PickingMode.Ignore;
        pqTags = Div(pqHud, "layer"); pqTags.pickingMode = PickingMode.Ignore;
        pqHand = Div(pqHud, "uno-hand", "pq-hand"); pqHand.pickingMode = PickingMode.Ignore;
        pqStatus = Text(pqHud, "", "uno-status", "pq-status"); pqStatus.pickingMode = PickingMode.Ignore;
    }

    int PqMe => game.pq == null ? 0 : Mathf.Clamp(game.mySeat, 0, game.pq.players.Count - 1);

    void RefreshPouilleux()
    {
        var p = game.pq;
        if (p == null) return;
        if (pqTagEls.Count != p.players.Count)
        {
            pqTags.Clear(); pqTagEls.Clear();
            for (int i = 0; i < p.players.Count; i++)
            {
                var tag = Div(pqTags, "uno-tag"); tag.pickingMode = PickingMode.Ignore;
                Ring(Portrait(tag, Avatar(i), null, 56), Board.Colors[i % Board.Colors.Length]);
                var col = Div(tag, "uno-tag-col");
                Text(col, p.players[i].name, "uno-tag-name");
                Text(col, "", "uno-tag-count");
                pqTagEls.Add(tag);
            }
        }
        for (int i = 0; i < p.players.Count; i++)
        {
            var pl = p.players[i];
            var t = pqTagEls[i];
            t.Q<Label>(className: "uno-tag-count").text = pl.hand.Count > 0 ? $"{pl.hand.Count} carte{(pl.hand.Count > 1 ? "s" : "")}" : i == p.loser ? "POUILLEUX !" : "Tranquille !";
            t.EnableInClassList("active", i == p.turn && !p.Finished);
            t.EnableInClassList("pq-target", !p.Finished && p.turn == PqMe && i == p.Target && !game.Spectating);
            t.style.display = i == PqMe && !game.Spectating ? DisplayStyle.None : DisplayStyle.Flex;
        }
        // Ma main, face visible (triee par valeur pour reperer les paires... qui sont deja jetees).
        pqHand.Clear();
        if (!game.Spectating)
        {
            var cards = p.players[PqMe].hand.OrderBy(Pouilleux.Rank).ThenBy(c => c / 13).ToList();
            float overlap = cards.Count <= 7 ? -30 : -Mathf.Min(120, 30 + (cards.Count - 7) * 9);
            for (int k = 0; k < cards.Count; k++)
            {
                float x = k - (cards.Count - 1) / 2f, spread = Mathf.Min(4, 40f / Mathf.Max(1, cards.Count));
                var slot = Div(pqHand, "uno-slot");
                slot.style.marginLeft = slot.style.marginRight = overlap / 2;
                slot.style.rotate = new Rotate(new Angle(x * spread));
                slot.style.translate = new Translate(0, x * x * spread * 0.9f - (cards[k] == pqNew ? 46 : 0));
                var c = Div(slot, "uno-card", "pq-card");
                c.style.backgroundImage = Resources.Load<Texture2D>("Cards/" + PouilleuxView.Face(cards[k]));
                c.EnableInClassList("pq-bad", cards[k] == Pouilleux.Pouilleu);
                slot.EnableInClassList("pq-new", cards[k] == pqNew);
            }
        }
        int tg = p.Target;
        pqStatus.text = p.Finished ? $"{p.players[p.loser].name} est le pouilleux !"
            : game.busy ? ""
            : p.turn == PqMe && !game.Spectating && tg >= 0 ? $"À toi ! Pioche une carte chez {p.players[tg].name} : clique sur une de ses cartes."
            : tg >= 0 ? $"{p.players[p.turn].name} pioche chez {p.players[tg].name}..." : "";
        pqStatus.style.display = pqStatus.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Chaque image : vignettes au-dessus des eventails.
    public void UpdatePouilleux(Camera cam)
    {
        var p = game.pq;
        if (p == null || pqTags.panel == null) return;
        for (int i = 0; i < pqTagEls.Count; i++)
        {
            var t = pqTagEls[i];
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(pqTags.panel, game.pview.TagOf(i), cam);
            float w = float.IsNaN(t.resolvedStyle.width) ? 230 : t.resolvedStyle.width;
            t.style.left = sp.x - w / 2;
            t.style.top = sp.y - 80;
        }
    }
}
