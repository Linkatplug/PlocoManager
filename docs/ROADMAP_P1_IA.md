# PLAN DE REFACTORISATION - PHASE 1 (P1) : Préparation et Quick-Wins

**Objectif :** Préparer le terrain pour une future migration MVVM en réduisant la taille du code-behind (`MainWindow.xaml.cs`) et en unifiant la gestion des logs, de manière itérative, sans casser l'application.

**Contrainte absolue pour l'IA :** Les étapes doivent être exécutées **strictement une par une**. Ne jamais modifier plus de 100 lignes de code d'un coup. S'assurer que le projet compile après **chaque** étape. Ne pas casser la logique métier ou l'UI WPF. Ne pas introduire de patterns complexes (pas d'IoC ni MVVM lourd pour l'instant).

---

## ÉTAPE 1 : Unification du Système de Logs (Mise en place de NLog ou Serilog)

L'objectif de cette étape est de remplacer les écritures de logs manuelles parsemées par un système robuste (ex: Serilog). 

1. **Sous-étape 1.1 : Installation du package NuGet**
   - Ajouter le package NuGet de logging choisi (ex: `Serilog` et `Serilog.Sinks.File`).
   - Mettre à jour `Ploco.csproj`.

2. **Sous-étape 1.2 : Configuration Globale**
   - Dans `App.xaml.cs`, configurer l'instance statique globale du Logger pour écrire dans `%AppData%\Ploco\Logs\`.
   - Mettre en place la rotation des fichiers (ex: 30 jours).

3. **Sous-étape 1.3 : Remplacement progressif - Partie 1**
   - Remplacer les appels artisanaux de log dans `PlocoRepository.cs` par des appels au nouveau Logger global.
   - Supprimer le code maison gérant l'écriture de fichier texte manuelle s'il s'y trouve, ou le remplacer par des wrappers.

4. **Sous-étape 1.4 : Remplacement progressif - Partie 2**
   - Remplacer les `.WriteLine` ou les `MessageBox.Show(err)` (utilisés pour debugger silencieusement) par des `.LogError()` / `.LogInformation()` dans `MainWindow.xaml.cs` et les fenêtres de dialog.

---

## ÉTAPE 2 : Extraction de la Logique Algorithmique (Extract Method)

`MainWindow.xaml.cs` est trop volumineux. Nous allons extraire la logique qui ne manipule pas *directement* des objets de la vue (`UIElement`, `Canvas`, etc.) vers de nouvelles classes statiques.

1. **Sous-étape 2.1 : Création de `PlacementLogicHelper.cs`**
   - Créer une nouvelle classe statique `Helpers/PlacementLogicHelper.cs`.
   - Isoler les algorithmes mathématiques déterminant les collisions, les index d'insertion et les décalages (offsets) des locomotives sur une voie.
   - Refactoriser `MainWindow.xaml.cs` (ex: `GetInsertIndex`, `EnsureTrackOffsets`) pour faire appel à ce Helper. L'UI passe des arguments primitifs (double, int, listes de `LocomotiveModel`), et le Helper retourne un résultat.

2. **Sous-étape 2.2 : Création de `LocomotiveStateHelper.cs`**
   - Extraire la logique de manipulation des statuts (OK, HS, ManqueTraction, etc.).
   - Par exemple, tout ce qui concerne la validation du transfert ("la locomotive X a-t-elle le droit d'aller sur la voie Y ?") doit exister indépendamment des événements `Drop` UI.
   - Les méthodes doivent renvoyer des `bool` ou des `Enum` indiquant si l'action est valide.

3. **Sous-étape 2.3 : Création de `PrevisionnelLogicHelper.cs`**
   - Le mode prévisionnel implémente beaucoup de logique complexe pour créer, trouver ou détruire des "Fantômes" (Ghosts).
   - Extraire les méthodes qui gèrent spécifiquement l'état métier des ghosts hors de l'UI (ex: la logique derrière `RemoveForecastGhostsFor`, ou la détermination si un drag and drop est un placement prévisionnel ou réel).

---

## ÉTAPE 3 : Allègement des Handlers d'Événements

Actuellement, les événements (ex: `Tile_Drop`, `LocomotiveList_Drop`) contiennent trop de logique métier.

1. **Sous-étape 3.1 : Démêler `LocomotiveList_Drop` et `TrackLocomotives_Drop`**
   - Extraire le code interne de ces handlers vers des méthodes distinces comme `TryMoveLocomotiveToTrack(source, destination, loco)` au sein de `MainWindow.xaml.cs` (avant de les déplacer dans un vrai Service plus tard).
   - Le handler d'événement ne doit faire que : Capturer l'event -> Récupérer la Data (Locomotive) -> Appeler une méthode d'action de haut niveau -> Terminer.

2. **Sous-étape 3.2 : Démêler les événements du Menu Contextuel (ContextMenu)**
   - Répéter l'opération pour les `MenuItem_Click` (ex: Modification de statut, Placement prévisionnel).
   - Créer des méthodes de haut niveau dans `MainWindow` (ex: `HandleLocomotiveStatusChange(LocomotiveModel loco)`).

---

## Règles de Validation de la Phase 1

Avant de considérer la Phase 1 comme terminée, vérifier les points suivants :
- Aucune régression n'a été introduite (le Drag&Drop et le Placement prévisionnel fonctionnent de manière identique).
- Le projet compile sans aucun avertissement critique (CS warnings).
- Le fichier `MainWindow.xaml.cs` est considérablement réduit (idéalement de moitié, en extrayant les calculs et validations métier hors des événements d'interface).
- Un système de logging standard est utilisé partout, rendant le futur débogage plus lisible.
