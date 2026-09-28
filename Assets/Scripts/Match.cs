using System;
using System.Collections.Generic;

public enum GameId { Croque, Blackjack, Roulette, Quiz, Trivia, Rhythm, Uno, Chevaux, Bac, Serpents, BonnePaye }

// Ce que le reseau a besoin de savoir d'une partie, quel que soit le jeu.
public interface IMatch
{
    int Actor { get; }                  // siege qui doit jouer (-1 : personne)
    bool Finished { get; }
    bool TryApply(string[] act);        // valide et applique une action ; false si illegale
    string[] Bot();                     // action automatique (joueur deconnecte)
}

public static class Games
{
    public static string Name(GameId g) => g == GameId.Croque ? "Croque-Carotte" : g == GameId.Blackjack ? "Blackjack" : g == GameId.Roulette ? "Roulette française" : g == GameId.Quiz ? "Quiz d'images" : g == GameId.Rhythm ? "Pique-Nique Live" : g == GameId.Uno ? "Uno" : g == GameId.Chevaux ? "Petits chevaux" : g == GameId.Bac ? "Petit bac" : g == GameId.Serpents ? "Serpents et échelles" : g == GameId.BonnePaye ? "La Bonne Paye" : "Le grand quiz de Tenna";

    public static string OptionName(GameId g, int option) =>
        g == GameId.BonnePaye ? BonnePaye.Months(option) + " mois" : g == GameId.Serpents ? "Classique" : g == GameId.Bac ? PetitBac.Rounds(option) + " manches" : g == GameId.Chevaux ? (Chevaux.Count(option) > 1 ? Chevaux.Count(option) + " chevaux par joueur" : "1 cheval par joueur") : g == GameId.Croque ? (option == 0 ? "Classique" : "Amélioré") : g == GameId.Blackjack ? option + " manches" : g == GameId.Roulette ? option + " coups" : g == GameId.Quiz ? new[] { "Flou", "Pixelisé", "Mélangé" }[option] : g == GameId.Rhythm ? Rhythm.Song(option).t : g == GameId.Uno ? new[] { "Une manche", "200 points", "500 points" }[Math.Min(2, option & 3)] + ((option & 4) != 0 ? ", cumul" : "") + ((option & 8) != 0 ? ", 7-0" : "") + ((option & 16) != 0 ? ", intervention" : "") + ((option & 32) != 0 ? ", pioche jusqu'à jouer" : "") + ((option & 64) != 0 ? ", jeu forcé" : "") : new[] { "QCM", "Réponse libre" }[option & 1];

    public static IMatch Create(GameId g, int option, IList<string> names, int seed, string text = null) =>
        g == GameId.Croque ? new Rules(option == 0 ? Mode.Classique : Mode.Ameliore, names, seed)
        : g == GameId.Blackjack ? new Blackjack(names, option, seed)
        : g == GameId.Roulette ? new Roulette(names, option, seed)
        : g == GameId.Quiz ? new Quiz(names, option, seed, Quiz.Pool)
        : g == GameId.Rhythm ? new Rhythm(names, option)
        : g == GameId.Uno ? new Uno(names, option, seed)
        : g == GameId.Chevaux ? new Chevaux(names, option, seed)
        : g == GameId.Bac ? new PetitBac(names, option, seed, text)
        : g == GameId.Serpents ? new Serpents(names, option, seed)
        : g == GameId.BonnePaye ? new BonnePaye(names, option, seed)
        : (IMatch)new Quiz(names, option, seed, Quiz.TriviaPool, true);

    public static bool TvTime(GameId g) => g == GameId.Quiz || g == GameId.Trivia || g == GameId.Rhythm || g == GameId.Bac;

    public static int MaxPlayers(GameId g) => TvTime(g) ? Quiz.MaxPlayers : g == GameId.Uno ? Uno.MaxPlayers : g == GameId.Chevaux ? Chevaux.MaxPlayers : g == GameId.Serpents ? Serpents.MaxPlayers : g == GameId.BonnePaye ? BonnePaye.MaxPlayers : Rules.MaxPlayers;

    // Hors ligne contre des bots (et non a plusieurs sur le meme PC) : jeux a main cachee ou tout le monde joue a la fois.
    public static bool WithBots(GameId g) => TvTime(g) || g == GameId.Uno;
}
