using System.Collections.Generic;

public enum GameId { Croque, Blackjack, Roulette, Quiz, Trivia }

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
    public static string Name(GameId g) => g == GameId.Croque ? "Croque-Carotte" : g == GameId.Blackjack ? "Blackjack" : g == GameId.Roulette ? "Roulette française" : g == GameId.Quiz ? "Quiz d'images" : "Le grand quiz de Tenna";

    public static string OptionName(GameId g, int option) =>
        g == GameId.Croque ? (option == 0 ? "Classique" : "Amélioré") : g == GameId.Blackjack ? option + " manches" : g == GameId.Roulette ? option + " coups" : g == GameId.Quiz ? new[] { "Flou", "Pixelisé", "Mélangé" }[option] : new[] { "QCM", "Réponse libre" }[option];

    public static IMatch Create(GameId g, int option, IList<string> names, int seed) =>
        g == GameId.Croque ? new Rules(option == 0 ? Mode.Classique : Mode.Ameliore, names, seed)
        : g == GameId.Blackjack ? new Blackjack(names, option, seed)
        : g == GameId.Roulette ? new Roulette(names, option, seed)
        : g == GameId.Quiz ? new Quiz(names, option, seed, Quiz.Pool)
        : (IMatch)new Quiz(names, option, seed, Quiz.TriviaPool, true);

    public static bool TvTime(GameId g) => g == GameId.Quiz || g == GameId.Trivia;

    public static int MaxPlayers(GameId g) => TvTime(g) ? Quiz.MaxPlayers : Rules.MaxPlayers;
}
