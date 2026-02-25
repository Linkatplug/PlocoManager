# Documentation des Tests Unitaires - Projet Ploco

Ce document recense l'ensemble des tests unitaires mis en place dans le projet **Ploco.Tests** (utilisant **xUnit** et **Moq**). Les tests sont divisés en deux grandes catégories : les tests de logique métier pure (Helpers) et les tests de comportement interactif (ViewModels).

## 1. Tests de la Logique Métier (Helpers)

Ces tests vérifient de manière isolée le comportement des fonctions d'assistance statiques.

### `LocomotiveStateHelperTests`
Valide les règles de gestion d'état des locomotives (autorisations de déplacement, éligibilité au swap, détection des pannes).

* **`CanDropLocomotiveOnTrack_WithNullInputs_ReturnsFalse`** : Vérifie que le système rejette une tentative de placement si la loco ou la voie est nulle.
* **`CanDropLocomotiveOnTrack_WithForecastGhost_ReturnsFalse`** : Vérifie qu'on ne peut pas déplacer manuellement une locomotive de type "Fantôme" (Ghost).
* **`CanDropLocomotiveOnTrack_ValidInputs_ReturnsTrue`** : Valide qu'une combinaison correcte loco/voie autorise le placement.
* **`IsEligibleForSwap_WithNullInputs_ReturnsFalse`** : Assure qu'on ne tente pas un swap Sibelit/Lineas avec des objets manquants.
* **`IsEligibleForSwap_WithAnyGhost_ReturnsFalse`** : Interdit de lancer un swap si l'une des locomotives impliquées est un fantôme prévisionnel.
* **`IsEligibleForSwap_WithTwoNormalLocos_ReturnsTrue`** : Permet le swap entre deux vraies locomotives physiques.
* **`IsLocomotiveHs_ReturnsExpectedResult`** *(Theory)* : Test paramétré qui vérifie si une loco est considérée en panne (HS ou ManqueTraction) ou non (Ok, DefautMineur).

### `PlacementLogicHelperTests`
Valide les algorithmes de calcul mathématique pour le positionnement visuel libre (les "slots") des locomotives sur les voies.

* **`GetInsertIndex_EmptyList_ReturnsZero`** : Vérifie qu'insérer dans une voie vide place l'élément à l'index 0.
* **`GetInsertIndex_DropBeforeFirst_ReturnsZero`** : Valide le calcul mathématique lorsqu'on lâche une loco visuellement *avant* la première loco existante.
* **`GetInsertIndex_DropAfterFirst_ReturnsOne`** : Valide l'insertion *après* une locomotive déjà présente sur la voie.
* **`CalculateBestOffset_EmptyTrack_ReturnsCorrectSlot`** : Assure que le système aimante la locomotive au *slot* le plus proche du point de chute de la souris sur une voie vide.
* **`CalculateBestOffset_SlotOccupied_ReturnsNextAvailableSlot`** : Cas complexe vérifiant que si le joueur lâche la loco sur un *slot* déjà occupé, l'algorithme "décale" la loco sur l'emplacement libre suivant.
* **`CalculateBestOffset_ClampsToActualWidth`** : Empêche un placement visuel hors des limites réelles de la zone UI dessinée à l'écran.

### `PrevisionnelLogicHelperTests`
Valide la manipulation des entités fictives associées à la fonctionnalité "Ghost" / Prévisionnel.

* **`CreateGhostFrom_ReturnsCorrectGhostProperties`** : Vérifie que la fabrication d'une entité Fantôme depuis une loco source clone bien les attributs (Numéro, Statut) mais génère un ID factice négatif et lie l'ID parent.
* **`IsGhostOf_ReturnsTrueForMatchingSourceAndGhost`** : Valide que la relation Parent/Fantôme est correctement reconnue par le système via la clé parent.
* **`IsGhostOf_ReturnsFalseForNotAGhost`** : Assure qu'une loco ne se considère pas à tort comme le fantôme d'une autre loco normale.
* **`RemoveForecastGhostsFor_RemovesOnlyTargetGhosts`** : Vérifie que lorsqu'on demande le nettoyage des fantômes d'une loco (ex: annulation prévisionnelle), seuls ses fantômes à elle sont effacés des tuiles, laissant les autres intacts.

## 2. Tests de Comportement (ViewModels)

Ces tests s'assurent que le coeur battant de l'application réagit correctement. L'accès à la base de données y est **simulé (Mocking)** via `Moq`.

### `MainViewModelTests`
* **`LoadDatabaseCommand_LoadsStateIntoCollections`** : Alimente le "faux" connecteur base de données puis ordonne au `MainViewModel` de charger l'état. On valide que les listes en mémoire (`_viewModel.Locomotives`) sont bien remplies avec les données récupérées.
* **`DropLocomotive_ToValidTrack_MovesLocomotive`** : Exécute de bout en bout l'événement de glisser-déposer. S'assure de l'effacement de l'ancienne voie, de l'insertion dans la nouvelle voie, et de la mise à jour de son ID de liaison (`AssignedTrackId`).
* **`DropLocomotive_ToOccupiedTrack_TriggersSwap`** : Exécute de bout en bout un glisser-déposer d'une loco sur une **voie spécifique (ligne de roulement) déjà occupée**. S'assure que le ViewModel effectue un chassé-croisé (Swap inversé) des locomotives entre l'ancienne et la nouvelle voie.
