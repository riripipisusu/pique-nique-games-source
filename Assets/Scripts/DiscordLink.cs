using System;
using Discord.Sdk;
using UnityEngine;

// Discord (Social SDK, Packages/com.discord.partnersdk hors depot) : statut "Joue a Pique-Nique's Games" sur le profil,
// et salon en ligne rejoignable depuis Discord (bouton Rejoindre du profil, invitation par le "+" du chat).
// Le "secret" d'invitation est le code du salon : Discord le rend au jeu de l'ami, qui rejoint le salon.
// Sans Discord ouvert (ou sans la bibliotheque), le jeu fonctionne normalement.
public class DiscordLink : MonoBehaviour
{
    const ulong AppId = 1553720885901529170;
    Game game;
    Client client;
    string shown = "";
    float next;
    readonly long startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void Create(Game g)
    {
        try { g.gameObject.AddComponent<DiscordLink>().Init(g); }
        catch (Exception e) { Debug.LogWarning("Discord indisponible : " + e.Message); }
    }

    void Init(Game g)
    {
        game = g;
        client = new Client();
        client.SetApplicationId(AppId);
        bool ok = client.RegisterLaunchCommand(AppId, "");   // Discord pourra lancer ce jeu quand un ami accepte une invitation
        Debug.Log("Discord : commande de lancement enregistree = " + ok);
        client.SetActivityJoinCallback(Join);
    }

    // Invitation acceptee (ou bouton Rejoindre) : le secret est le code du salon.
    void Join(string code)
    {
        if (string.IsNullOrEmpty(code) || game.net.Code == code) return;
        Debug.Log("Discord : rejoindre le salon " + code);
        game.JoinFromDiscord(code);
    }

    void Update()
    {
        if (client == null || Time.unscaledTime < next) return;
        next = Time.unscaledTime + 2;
        var n = game.net;
        bool salon = n.Active && n.Code != "";
        string details = !salon ? "À l'accueil" : n.InGame ? Games.Name(n.LobbyGame) : "Dans un salon";
        string state = !salon ? "" : n.InGame ? (game.Spectating ? "Regarde la partie" : "En partie") : "Choisit un jeu";
        string key = $"{details}|{state}|{n.Code}|{n.Lobby.Count}";
        if (key == shown) return;
        shown = key;

        var a = new Activity();
        a.SetType(ActivityTypes.Playing);
        a.SetDetails(details);
        if (state != "") a.SetState(state);
        var t = new ActivityTimestamps();
        t.SetStart((ulong)startedAt);
        a.SetTimestamps(t);
        if (salon && n.Lobby.Count < Net.SalonMax)
        {
            var party = new ActivityParty();
            party.SetId("salon-" + n.Code);   // Discord refuse un identifiant de groupe egal au secret
            party.SetCurrentSize(Math.Max(1, n.Lobby.Count));
            party.SetMaxSize(Net.SalonMax);
            party.SetPrivacy(ActivityPartyPrivacy.Public);
            a.SetParty(party);
            var secrets = new ActivitySecrets();
            secrets.SetJoin(n.Code);
            a.SetSecrets(secrets);
            a.SetSupportedPlatforms(ActivityGamePlatforms.Desktop);
        }
        client.UpdateRichPresence(a, r => Debug.Log(r.Successful() ? "Discord : statut mis a jour (" + key + ")" : "Discord : statut refuse : " + r.Error()));
    }
}
