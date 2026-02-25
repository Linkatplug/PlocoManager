# PLAN DE REFACTORISATION - PHASE 4 (P4) : Tests Unitaires et Modernisation des Données

**Objectif :** Maintenant que l'architecture MVVM est en place et que le code-behind est allégé, il s'agit de fiabiliser le code métier via des tests automatisés, et de moderniser l'accès aux données (asynchronisme) pour éviter les gels d'interface (UI freezes).

**Contrainte absolue pour l'IA :** 
- Les étapes doivent être exécutées **strictement une par une**. 
- Créer un projet séparé pour les tests. Ne pas polluer le projet principal.
- Utiliser `xUnit` pour l'exécution des tests et `Moq` (ou NSubstitute) pour le mocking de `IPlocoRepository`.
- S'assurer que le projet compile entièrement après chaque modification de signature.

---

## ÉTAPE 1 : Mise en place de l'environnement de Tests

Ploco ne possédait aucun test. L'abstraction apportée par P1, P2 et P3 permet enfin d'en écrire.

1. **Sous-étape 1.1 : Création du projet de tests**
   - Créer un nouveau projet de type "Test Project" (ex: `Ploco.Tests`).
   - Ajouter une référence depuis `Ploco.Tests` vers le projet principal `Ploco.csproj`.
   - Installer les packages NuGet : `xUnit`, `xUnit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq` (et idéalement `FluentAssertions` si pertinent).

2. **Sous-étape 1.2 : Tests des Helpers Métier (Les "Low-Hanging Fruits")**
   - Écrire des tests unitaires pour `LocomotiveStateHelper` : tester si une loco "HS" est bien refusée sur une ligne classique.
   - Écrire des tests unitaires pour `PlacementLogicHelper` : vérifier que le calcul d'offset d'une loco retourne bien la coordonnée X attendue.
   - Écrire des tests unitaires pour `PrevisionnelLogicHelper` (Ghost detection).
   - Ces classes étant statiques et pures, ces tests doivent être simples et exécutés rapidement.

---

## ÉTAPE 2 : Tests Unitaires du ViewModel

C'est ici que le pattern MVVM montre sa puissance : on peut tester l'interface utilisateur sans interface graphique !

1. **Sous-étape 2.1 : Configuration des Mocks**
   - Créer une classe de test `MainViewModelTests`.
   - Utiliser `Moq` pour créer une fausse instance `Mock<IPlocoRepository>`.
   - Configurer le mock pour qu'il retourne de fausses données synchrones lors de l'initialisation du ViewModel (ex: `mockRepo.Setup(r => r.LoadState()).Returns(...)`).

2. **Sous-étape 2.2 : Test des commandes simples**
   - Instancier le `MainViewModel` avec le repo mocké.
   - Appeler manuellement une commande (ex: `viewModel.LoadDatabaseCommand.Execute(null)`).
   - Vérifier via `Assert` que l'état interne (`viewModel.Tiles.Count`, etc.) a correctement été modifié ou que le mock du repo a bien été appelé (`mockRepo.Verify()`).

3. **Sous-étape 2.3 : Test du glisser-déposer (Drag & Drop)**
   - Tester la commande `DropLocomotiveCommand` en lui passant des paramètres (source track, target track, locomotive).
   - Vérifier que la logique métier et le ViewModel mettent bien à jour les listes respectives (la loco disparaît de la source et apparaît dans la cible).

---

## ÉTAPE 3 : Modernisation des accès base de données (Asynchronisme / Async-Await)

L'import d'Excel ou le rechargement asynchrone d'historique peuvent encore bloquer l'UI de la fenêtre principale.

1. **Sous-étape 3.1 : Migration de `IPlocoRepository` vers l'asynchrone**
   - Modifier les signatures des méthodes lourdes dans l'interface `IPlocoRepository` pour qu'elles retournent des `Task` ou `Task<T>` (ex: `Task<bool> SaveStateAsync(...)`).
   - *Attention : Ne faire cela que sur les grosses méthodes (LoadState, SaveState, Import, LoadHistory), pas sur les requêtes minuscules si cela engendre trop de complexité.*

2. **Sous-étape 3.2 : Mise à jour de `PlocoRepository`**
   - Implémenter ces nouvelles signatures asynchrones en utilisant les méthodes asynchrones de SQLite (ex: `ExecuteReaderAsync()`, `ExecuteNonQueryAsync()`).

3. **Sous-étape 3.3 : Propagation dans le `MainViewModel`**
   - Adapter les appels dans le ViewModel. Les commandes `CommunityToolkit.Mvvm` (`[RelayCommand]`) supportent nativement l'asynchrone. Par exemple : `[RelayCommand] private async Task LoadDatabaseAsync() {...}`.
   - Gérer l'état de chargement potentiel (ex: un booléen `IsLoading` bindé à un spinner d'attente WPF).

---

## Règles de Validation de la Phase 4

- L'explorateur de tests de Visual Studio ou la commande `dotnet test` découvre et exécute avec succès tous les tests ajoutés.
- L'application compile et démarre. L'interface ne "freeze" plus lors du chargement de grosses données (si l'étape 3 est réalisée).
- Une partie critique de la logique métier est désormais sécurisée face aux futures régressions.
