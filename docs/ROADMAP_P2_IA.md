# PLAN DE REFACTORISATION - PHASE 2 (P2) : MVVM & Injection de Dépendances

**Objectif :** Poursuivre la refactorisation de l’application Ploco en introduisant un véritable conteneur IoC (Injection of Control) et en migrant l'interface vers le pattern MVVM (Model-View-ViewModel). Cette phase permettra de détacher la base de données et la logique d'état de la vue (`MainWindow.xaml.cs`).

**Contrainte absolue pour l'IA :** 
- Les étapes doivent être exécutées **strictement une par une**. 
- Compiler et lancer (ou s'assurer que ça build) après chaque sous-étape.
- Utiliser le package `CommunityToolkit.Mvvm` pour simplifier la réécriture des commandes et la notification des propriétés (ObservableProperty, RelayCommand).

---

## ÉTAPE 1 : Mise en place de l'Injection de Dépendances (IoC)

Le but est de ne plus utiliser `new PlocoRepository()` un peu partout, mais de confier la création des services à un conteneur global.

1. **Sous-étape 1.1 : Installation des packages NuGet**
   - Installer `Microsoft.Extensions.DependencyInjection`.
   - Installer `CommunityToolkit.Mvvm`.

2. **Sous-étape 1.2 : Abstraction du Repository (Création de l'interface)**
   - Créer une interface `IPlocoRepository` dans le dossier `Data/` ou `Interfaces/`.
   - Y déclarer toutes les méthodes publiques actuellement présentes dans `PlocoRepository.cs` (ex: `LoadState`, `SaveState`, `GetTableCounts`, etc.).
   - Faire en sorte que `PlocoRepository` implémente `IPlocoRepository`.

3. **Sous-étape 1.3 : Configuration du conteneur dans `App.xaml.cs`**
   - Surcharger la méthode `OnStartup(StartupEventArgs e)` dans `App.xaml.cs`.
   - Instancier un `ServiceCollection` et y enregistrer :
     - `IPlocoRepository` en tant que Singleton (`AddSingleton<IPlocoRepository, PlocoRepository>()`).
     - Le futur `MainViewModel` en tant que Transient ou Singleton (`AddTransient<MainViewModel>()`).
   - Construire le `ServiceProvider`.

---

## ÉTAPE 2 : Création du Socle MVVM (MainViewModel)

On commence à déplacer l'état global (les listes de tuiles, de locomotives) du `MainWindow.xaml.cs` vers un ViewModel dédié.

1. **Sous-étape 2.1 : Initialisation de `MainViewModel`**
   - Créer le dossier `ViewModels/` si inexistant.
   - Créer `MainViewModel.cs` héritant de `ObservableObject` (issu de `CommunityToolkit.Mvvm`).
   - Y injecter `IPlocoRepository` via le constructeur.

2. **Sous-étape 2.2 : Migration des propriétés d'état (DataBinding)**
   - Déplacer (ou recréer) les collections observables clés depuis `MainWindow.xaml.cs` vers `MainViewModel.cs` :
     - `ObservableCollection<TileModel> Tiles`
     - `ObservableCollection<LocomotiveModel> UnassignedLocomotives`
   - Décorer ces propriétés avec `[ObservableProperty]` pour générer automatiquement le code de notification (INotifyPropertyChanged).

3. **Sous-étape 2.3 : Connexion Vue ↔ ViewModel**
   - Dans `MainWindow.xaml.cs`, remplacer l'instanciation manuelle des données par l'injection du ViewModel : `DataContext = App.ServiceProvider.GetRequiredService<MainViewModel>();` (ou via injection dans le constructeur de MainWindow).
   - Dans `MainWindow.xaml`, s'assurer que les listes visuelles (`ItemsControl`, `ListView`) sont bien bindées sur les nouvelles propriétés du ViewModel (ex: `ItemsSource="{Binding UnassignedLocomotives}"`).

---

## ÉTAPE 3 : Migration des Commandes Simples

Remplacer les anciens `Click="MethodName"` des petits boutons ou menus par des `ICommand`.

1. **Sous-étape 3.1 : Création des Commandes dans le ViewModel**
   - Identifier une action simple, par exemple le rafraichissement, le changement de thème, ou l'ajout d'une tuile standard.
   - Créer des méthodes dans `MainViewModel` décorées avec `[RelayCommand]`.

2. **Sous-étape 3.2 : Remplacement dans le XAML**
   - Dans le fichier `MainWindow.xaml`, remplacer `Click="MonBouton_Click"` par `Command="{Binding MaNouvelleCommand}"`.
   - Nettoyer le code-behind de `MainWindow.xaml.cs` en y supprimant `MonBouton_Click`.

*(Note : Le Drag & Drop restera temporairement géré par du Code-Behind ou des Behaviors dans cette phase, car son traitement purement MVVM est complexe. Seules les actions "clic" directes sont ciblées ici).*

---

## Règles de Validation de la Phase 2

- `PlocoRepository` n'est **jamais** instancié avec le mot-clé `new` dans le code métier ou UI.
- L'application démarre et les données s'affichent correctement grâce au Binding `MainWindow` -> `MainViewModel`.
- Le projet compile sans erreur après l'ajout de `CommunityToolkit.Mvvm`.
- La taille de `MainWindow.xaml.cs` continue de baisser au profit du `MainViewModel.cs`.
