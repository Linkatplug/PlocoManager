# PLAN DE REFACTORISATION - PHASE 3 (P3) : Migration des Commandes Complexes et Nettoyage UI

**Objectif :** Finaliser la migration MVVM de `MainWindow.xaml.cs` en déplaçant la logique métier complexe (Glisser-Déposer, Placement Prévisionnel) dans le `MainViewModel`. Utiliser des patterns WPF modernes (Attached Behaviors, interactivité métier pure) pour vider presque totalement le Code-Behind de toute orchestration.

**Contrainte absolue pour l'IA :** 
- Les étapes doivent être exécutées **strictement une par une**. 
- Compiler et lancer l'application après chaque changement majeur (spécialement le Drag & Drop).
- Utiliser `Microsoft.Xaml.Behaviors.Wpf` si nécessaire, ou des commandes paramétrées, pour lier les événements UI complexes au ViewModel sans briser le pattern MVVM.

---

## ÉTAPE 1 : Migration des Composants Intermédiaires (Tuiles et Voies)

Actuellement, les tuiles et les voies sont souvent générées ou manipulées directement dans le Code-Behind.

1. **Sous-étape 1.1 : Actions sur les Tuiles (Ajout / Suppression / Renommage)**
   - Dans `MainViewModel`, créer les commandes `AddTileCommand`, `DeleteTileCommand`, `RenameTileCommand` (décorées de `[RelayCommand]`).
   - Mettre à jour `MainWindow.xaml` pour binder ces commandes (ex: `Command="{Binding AddTileCommand}"`).
   - S'assurer que les fenêtres de dialogue appelées par ces commandes (si besoin) sont de préférence abstraites (création d'un `IDialogService` par exemple) pour ne pas polluer le ViewModel avec du `new Window().ShowDialog()`.

2. **Sous-étape 1.2 : Actions sur les Voies (Ajout / Suppression Track)**
   - Répéter le même processus pour les actions des voies (ex: `AddLineTrack_Click`, etc.).
   - Relier ces actions aux éléments visuels via Binding.

3. **Sous-étape 1.3 : Validation des dialogues (Optionnel mais recommandé)**
   - Si les étapes 1.1 et 1.2 impliquent des pop-ups de saisie, extraire cette logique dans une interface `IDialogService` (enregistrée dans `App.xaml.cs`) que le `MainViewModel` appellera.

---

## ÉTAPE 2 : Le Cœur du Réacteur : Placement Prévisionnel

Le mode Prévisionnel (Ghosts) est la logique la plus complexe. Il faut la lier formellement au ViewModel.

1. **Sous-étape 2.1 : Migration des commandes contextuelles du Prévisionnel**
   - Transformer `MenuItem_PlacementPrevisionnel_Click`, `MenuItem_AnnulerPrevisionnel_Click`, et `MenuItem_ValiderPrevisionnel_Click` en commandes dans `MainViewModel`.
   - Ces commandes prendront probablement en paramètre le modèle sélectionné (ex: `[RelayCommand] public void PlacementPrevisionnel(LocomotiveModel loco)`).
   - Utiliser `CommandParameter="{Binding}"` dans le XAML.

2. **Sous-étape 2.2 : Utilisation des Helpers depuis le ViewModel**
   - S'assurer que le ViewModel appelle bien `PrevisionnelLogicHelper` pour toute manipulation de liste (Ajout de Ghost dans `ObservableCollection`).

---

## ÉTAPE 3 : Le défi final : Drag & Drop MVVM

Le glisser-déposer modifie les collections ET l'interface. En MVVM, l'interface doit simplement refléter l'état des collections.

1. **Sous-étape 3.1 : Installation de Behaviors (Si nécessaire)**
   - Installer le NuGet `Microsoft.Xaml.Behaviors.Wpf`.
   - *Note : Selon l'implémentation existante, on peut aussi utiliser un Behavior personnalisé ou un Attached Property (ex: `DragDrop.DropCommand="{Binding MonDropCommand}"`).*

2. **Sous-étape 3.2 : Câblage des événements de Drop**
   - Créer une `DropLocomotiveCommand` (ou similaire) dans `MainViewModel` qui prendra en compte l'élément source et la destination.
   - Utiliser les Behaviors dans le XAML pour lier les événements `Drop` natifs (`UIElement.Drop`) à cette/ces commandes du ViewModel. L'objectif est de ne plus utiliser `LocomotiveList_Drop` dans le `MainWindow.xaml.cs`.

3. **Sous-étape 3.3 : Nettoyage Ultime de MainWindow.xaml.cs**
   - Supprimer toutes les méthodes devenues orphelines.
   - Le code-behind ne devrait idéalement contenir que le constructeur (`InitializeComponent();`) et potentiellement quelques manipulations purement visuelles (comme le redimensionnement `TileResizeThumb_DragDelta` qui n'a pas sa place dans le ViewModel).

---

## Règles de Validation de la Phase 3

- L'application compile.
- Le Glisser-Déposer fonctionne toujours de manière fluide.
- Le Placement prévisionnel (création, validation, annulation) fonctionne.
- `MainWindow.xaml.cs` a vu sa taille réduite de façon drastique (idéalement sous les 500 ou 300 lignes, ne conservant que l'UI stricte).
- Le pattern MVVM est dorénavant l'épine dorsale de l'application.
