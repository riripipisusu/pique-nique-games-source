using System.Collections.Generic;

public enum GameId { Croque, Blackjack }

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
    public static string Name(GameId g) => g == GameId.Croque ? "Croque-Carotte" : "Blackjack";

    public static string OptionName(GameId g, int option) =>
        g == GameId.Croque ? (option == 0 ? "Classique" : "Amélioré") : option + " manches";

    public static IMatch Create(GameId g, int option, IList<string> names, int seed) =>
        g == GameId.Croque ? new Rules(option == 0 ? Mode.Classique : Mode.Ameliore, names, seed)
                           : (IMatch)new Blackjack(names, option, seed);
}
