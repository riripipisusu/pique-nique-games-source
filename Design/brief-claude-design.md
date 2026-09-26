# Brief design — Pique-Nique's Games (interface & menus)

## 1. Le projet en deux phrases

**Pique-Nique's Games** est un jeu PC (Windows, Unity 6) pour une bande d'amis : plusieurs mini-jeux réunis dans un seul programme, jouables sur un même PC ou en ligne (jusqu'à 10 joueurs). Le jeu est en **3D low-poly** (packs Synty POLYGON : prairie ensoleillée, forêt, casino, plateau télé), et **l'interface actuelle fait amateur** : c'est elle qu'on veut refaire entièrement.

Référence d'ambiance : le jeu **Agrou** (low-poly chaleureux, lisible, cartoon mais soigné) et, pour les jeux « TV Time », l'univers de **Tenna** dans *Deltarune* (présentateur télé excentrique, néons, show TV rétro).

## 2. Ce qu'on attend de toi

1. **Un design system** (couleurs, typographies, boutons, cartes, panneaux, champs, curseurs, badges, icônes, animations) cohérent pour tout le jeu.
2. **Les maquettes de chaque écran** listé en section 5, en **1920 × 1080** (le jeu s'adapte aux autres résolutions en gardant ce ratio de référence).
3. Si possible, les **éléments graphiques exportables** : icônes en SVG ou PNG, illustrations des cartes de jeu (voir 5.2), textures de panneaux (PNG 9-slice si tu utilises des bords décorés).

Le rendu 3D reste en fond de la plupart des écrans (la prairie avec la bande d'amis autour d'une nappe de pique-nique) : **l'interface se pose par-dessus une scène 3D vivante**, elle ne doit pas la cacher entièrement.

## 3. Identité voulue

- **Chaleureux, convivial, « entre amis »** : c'est une soirée jeux, pas une application sérieuse.
- **Cartoon soigné, pas enfantin** : formes arrondies, contours affirmés, un peu de relief (ombres portées nettes, « boutons qu'on a envie d'appuyer »), couleurs franches mais harmonieuses.
- **Très lisible** : gros textes, contrastes forts, utilisable à 2 m d'un écran pendant une soirée.
- **Trois familles de jeux avec chacune sa teinte**, tout en gardant une identité commune :
  - **Jeux de société** (Croque-Carotte) : nature, potager, pique-nique — verts, oranges carotte, crème.
  - **Casino** (Blackjack, Roulette) : feutre vert, bois sombre, dorures, rouge profond.
  - **TV Time** (Quiz d'images, Le grand quiz de Tenna) : plateau télé rétro, rose fuchsia, violet, jaune néon, noir.

Nom du jeu : **Pique-Nique's Games** (avec l'apostrophe). Sous-titre actuel : « Des jeux de société à partager entre amis ». Tu peux proposer un logo / logotype.

## 4. Contraintes techniques (important)

L'interface est faite avec **Unity UI Toolkit** (UXML/USS, un sous-ensemble de CSS) et **construite dans le code** : toute ta proposition doit pouvoir être reproduite avec ces possibilités.

**Possible :**
- Mise en page **Flexbox uniquement** (pas de CSS Grid), positions absolues.
- Couleurs unies, **bordures** (épaisseur et couleur par côté), **coins arrondis**, opacité.
- **Images de fond** (PNG/SVG importés), y compris en **9-slice** pour des cadres décorés.
- **Polices personnalisées** (.ttf/.otf libres de droits, ex. Google Fonts) ; contour de texte (outline) et ombre de texte simple.
- **Transitions** (couleur, taille, position, opacité, rotation) et états **:hover / :active / :disabled / :focus**.
- Textes riches simples (gras, couleur dans une phrase).

**Pas possible (ou à éviter) :**
- `box-shadow` (les ombres portées se font avec une image ou un second élément décalé), dégradés CSS (`linear-gradient` → à remplacer par une image), `backdrop-filter`/flou d'arrière-plan, `mix-blend-mode`, clip-path, animations `@keyframes` complexes (les petites animations sont faites en code : dis simplement ce que tu veux).
- Emojis dans les textes (la police ne les affiche pas).

Police actuelle : **Fredoka** (Google Fonts). Tu peux la garder ou en proposer d'autres (libres de droits, en .ttf).

Donne tes valeurs en **pixels pour un écran 1920 × 1080** et tes couleurs en **hexadécimal** : je les reporte telles quelles.

## 5. Les écrans à dessiner

### 5.1 Accueil (titre)
- Fond : scène 3D (prairie, nappe de pique-nique, la bande d'amis assise, feu de camp). La caméra tourne doucement.
- Titre du jeu + sous-titre.
- Boutons : **Jouer**, **Paramètres**, **Quitter**.
- Carte « **Mise à jour disponible** » quand une nouvelle version existe (texte + bouton « Mettre à jour », barre de progression pendant le téléchargement).
- Petite ligne de crédits en bas (texte long, discret) + numéro de version.
- Bouton ou coin pour **son personnage** (avatar choisi, voir 5.4).

### 5.2 Choix du jeu (« À quoi on joue ? »)
- 5 jeux rangés en **3 catégories** : Jeux de société (Croque-Carotte) · Casino (Blackjack, Roulette) · TV Time (Quiz d'images, Le grand quiz de Tenna). D'autres jeux viendront : la mise en page doit accepter plus de cartes (défilement horizontal ou grille qui se remplit).
- Chaque **carte de jeu** : illustration (à proposer, actuellement un simple aplat de couleur), nom, nombre de joueurs (ex. « 2 à 4 joueurs »), type (« Course de lapins », « Cartes », « Casino », « En ligne », « Culture G »), description d'une ou deux lignes.
- Bouton **Retour**.

### 5.3 Préparation d'une partie
- Titre : nom du jeu.
- **Options** du jeu en cartes sélectionnables (2 ou 3 choix, avec titre + une ligne d'explication). Exemples : Croque-Carotte « Classique / Amélioré » ; Blackjack « 5 / 10 / 20 manches » ; Roulette « 10 / 20 / 40 coups » ; Quiz d'images « Flou / Pixelisé / Mélangé » ; Grand quiz « QCM / Réponse libre ».
- **Joueurs sur ce PC** (jeux de société et casino) : liste de 2 à 4 lignes, chacune avec portrait cliquable, champ prénom, bouton « Retirer » ; bouton « + Ajouter un joueur ».
- Pour les jeux TV Time : pas de joueurs locaux, mais une ligne **« Hors ligne contre des bots »** avec un compteur (− 3 bots +) et un bouton « Jouer contre les bots ».
- Boutons : **Retour**, **Règles**, **Jouer en ligne**, **Jouer sur ce PC**.

### 5.4 Choix du personnage
- Grille de ~50 portraits ronds (personnages 3D cartoon) ; le personnage sélectionné est mis en avant. Bouton Annuler.

### 5.5 Règles
- Panneau de lecture : titre « Règles : <jeu> », paragraphes avec intertitres, défilement. Bouton Retour.

### 5.6 Paramètres
- Onglets : **Graphismes**, **Audio**, **Jeu**.
- Graphismes : Qualité (liste), Résolution, Affichage, Synchronisation verticale (case), Images par seconde max., Ombres, Détail du décor, Anticrénelage, Effets visuels (case), Échelle de rendu (curseur).
- Audio : Volume général, Musique, Effets sonores, Interface (curseurs), Couper le son (case).
- Jeu : Vitesse des animations, Sensibilité de la caméra (curseurs), cases à cocher, liste « Dos des cartes ».
- Boutons : Retour, Réinitialiser.
- Composants à dessiner : **liste déroulante, case à cocher, curseur (slider) avec valeur, onglets**.

### 5.7 Jouer en ligne + salon
- **En ligne** : « Toi » (portrait + prénom), bloc « Créer une partie » (bouton Héberger), bloc « Rejoindre » (champ code à 6 caractères + bouton), message d'état (connexion, erreurs).
- **Salon** : code de la partie en grand avec bouton « Copier », jeu choisi, options (l'hôte peut les changer), liste des joueurs connectés (portrait, prénom, « hôte »), jusqu'à 10 places ; boutons Quitter / Lancer la partie (hôte seulement).

### 5.8 Pause et fin de partie
- **Pause** : Reprendre, Paramètres, Menu principal (sur fond assombri).
- **Victoire** : « <Prénom> gagne ! » en grand (couleur du joueur), classement (1. Prénom — 107 points…), boutons Menu principal / Rejouer.

### 5.9 Interfaces en jeu (HUD) — à harmoniser avec le reste
Ces écrans sont superposés à la 3D en pleine partie ; ils doivent rester **compacts et lisibles** :
- **Croque-Carotte** : barre des joueurs (portraits + couleurs), carte piochée (grand chiffre ou « Carotte ! »), bouton « Piocher une carte », boutons des 3 lapins, fil des dernières actions, bannière d'annonce au centre (« Au tour de Léo ! »).
- **Blackjack** : bulles de valeur des mains au-dessus des cartes, médaillons des joueurs, grands boutons ronds d'action (Tirer, Rester, Doubler, Séparer, Assurance oui/non), sélection de mise avec jetons (10, 50, 100, 500), barre du bas (Solde, Mise, Manche).
- **Roulette** : barre compacte en bas (Solde, Mise, Coup, jetons 10/50/100/500, Effacer, Rejouer, Passer, Valider), liste des joueurs à gauche, historique des numéros sortis à droite (pastilles rouge / noir / vert), infobulle au survol du tapis (« Cheval 14-17 · paie 17 contre 1 »).
- **TV Time** (Quiz d'images, Grand quiz) : catégorie + numéro en haut, barre de temps 20 s qui passe au rouge sur la fin, fil des propositions (« Léo : Portugal », « Ana a trouvé ! +11 »), champ de réponse **ou** 4 boutons QCM de couleur, panneau « C'était : <réponse> » + anecdote/crédit. Boîte de dialogue de Tenna (style Deltarune : fond noir, liseré blanc, texte qui s'écrit) avec bouton « Passer les règles » et confirmation.
- Bannière d'annonce générique (« Faites vos jeux ! », « Rien ne va plus ! »), bouton pause rond en haut à droite.

## 6. Couleurs des joueurs (à conserver ou à ajuster)

Jusqu'à 10 joueurs, chacun a une couleur fixe (pupitres, jetons, portraits) : rouge `#E8483B`, bleu `#3B7DE0`, vert `#45B35F`, jaune `#F0B92A`, violet `#9B59D6`, orange `#F07E2A`, rose `#E05AA8`, turquoise `#2AB7B0`, brun `#8D6E4A`, vert pomme `#A3D93B`. Propose une version harmonisée si besoin, en gardant 10 couleurs bien distinctes (aussi pour les daltoniens).

## 7. Captures de l'existant

Je joins des captures de l'accueil, du choix du jeu, et de parties en cours (Croque-Carotte, Blackjack, Roulette, Quiz). Elles montrent la 3D à garder… et l'interface à remplacer.

## 8. Livrables attendus (récapitulatif)

- Planche du **design system** : palette (générale + 3 familles), typographies (tailles et graisses), boutons (primaire, secondaire, danger, rond, désactivé, survol), cartes, panneaux, champs, listes déroulantes, cases, curseurs, onglets, badges, bannières, dialogue de Tenna.
- **Maquettes 1920 × 1080** des écrans 5.1 à 5.9.
- **Assets** : logo, icônes, illustrations des 5 cartes de jeu, éventuels cadres 9-slice.
- Valeurs en px et couleurs en hexa, prêtes à reporter en USS.
