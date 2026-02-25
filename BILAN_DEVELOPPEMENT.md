# Bilan Global du Projet Ploco : De la Phase 1 à Aujourd'hui (MVP)

Ce document retrace l'évolution architecturale et fonctionnelle de l'application Ploco depuis le début de sa refonte majeure vers une application métier moderne.

---

## 🎯 Phase 1 : Modernisation de l'Interface (UI) et Architecture MVVM
L'objectif initial était de quitter une approche statique pour aller vers une application réactive, fluide et agréable à utiliser, capable de concurrencer les outils web modernes depuis un bureau Windows.

- **Modèle-Vue-VueModèle (MVVM)** : Découpage strict du code entre l'affichage visuel (XAML), les données pures (`Model`), et la logique de liage (`ViewModel`).
- **Nouveau Design System WPF** : Mise en place de `DynamicResource` structurant le thème visuel (Fond Sombre, Surfaces arrondies Modernes, Borders colorées).
- **Responsive Layout** : Remplacement des coordonnées fixes (WinForms) par des Grilles (Grids) dynamiques, des conteneurs DockPanel et des Tuiles redimensionnables à la souris (Thumb Resizer).
- **Glisser-Déposer (Drag & Drop) Magique** : Implémentation complète via des `Behaviors` XAML (`LocomotiveDragBehavior`, `LocomotiveDropBehavior`) permettant de déplacer les machines visuellement des `ListBox` vers les `Canvas` de voies, sans aucun lag, avec gestion des Z-Index.

## 🧠 Phase 2 : Modélisation Métier & Helpers Logiques
Séparation des algorithmes complexes dans des classes statiques dédiées ("Helpers") pour rendre le code lisible et facile à tester.

- **`LocomotiveStateHelper`** : Gestion des règles métier complexes pour générer les textes et les couleurs. (Ex: "100%", "Traction Réduite", "Loc HS", "Manque Tension").
- **`PlacementLogicHelper`** : Algorithmique mathématique calculant précisément où les locomotives tombent visuellement sur une voie (Offset X) les unes à côté des autres (slots virtuels), évitant qu'elles ne s'empilent ou se chevauchent.
- **Tuiles Dynamiques** : Création libre de nouveaux "Lieux" par l'utilisateur (Dépôts, Garages, Arrêt-Ligne, Ligne de Roulement). Chaque lieu pouvant créer dynamiquement ses propres Voies (Main, Zone, Sortie).
- **Menu Contextuel Avancé (Clic Droit)** : Création de `ContextMenu` associés aux Tuiles, et distinctement aux Locomotives pour modifier leurs états instantanément.

## 💾 Phase 3 : Base de Données Asynchrone (La Réactivité)
Afin d'éviter tout ralentissement de l'interface (UI Freeze), la sauvegarde et le chargement ont été entièrement rebâtis.

- **SQLite & Asynchronisme** : Intégration de `Microsoft.Data.Sqlite` exploité de bout-en-bout avec l'API Asynchrone (`Task`, `async/await`, `ExecuteReaderAsync`). L'UI ne se bloque plus jamais pendant une sauvegarde (qui prend désormais moins d'une fraction de seconde).
- **Transactions Atomiques** : Encapsulation des requêtes d'insertion massives (`INSERT INTO tracks...`) au sein de `BeginTransaction()` afin d'empêcher toute corruption si l'application est brutalement coupée, tout en décuplant la vitesse d'écriture.
- **Persistance du Contexte Éric-Sérialisation JSON** : Les variables uniques comme le nombre de lignes, la taille des fenêtres, et les positions X/Y sont encodées à la volée en JSON pour ne pas polluer le schéma de la base relationnelle.

## 🧪 Phase 4 : Améliorations de Qualité (QA), Bugs & Prévisionnel
Consolidation de l'outil technique pour garantir qu'il soit un outil métier robuste, complétée par des fonctions pour l'Agent de Parc.

- **Mode Placement Prévisionnel (`PrevisionnelLogicHelper`)** : Implémentation du système de "Fantômes" (Ghosts). Une locomotive physique ("Sibelit") peut être projetée sur une Ligne de Roulement (ex: `1125`) tout en restant physiquement ailleurs. Gestion des liaisons parent-enfant complexes.
- **Tapis T13 ("Fenêtre de Suivi")** : Création d'une table analytique lisant spécifiquement les "Sibelit T13". Analyse dynamique des Tuiles pour déduire les bons raccourcis géographiques métier ("THL", "WPY", "FN"). Gestion de l'affichage distinct entre Fantôme vs Origine (ex: "DISPO THL + 1125").
- **Tests Unitaires Automatisés (xUnit & Moq)** : Mise en place d'un projet de test (`Ploco.Tests`). Les comportements vitaux (Drop, Prévisionnel, Traction) sont validés sous milliseconde unitairement pour prévenir les régressions à l'avenir ! (**24 Tests écrits en tout**).
- **Zéro Avertissement C# 8.0** : Nettoyage systématique des avertissements de code (notamment Nullability) et unification des `partial class`.
- **Rétractation Avancée de l'UI** : Le tiroir "Pool Locomotives" peut se replier complètement sur la gauche via `ToggleButton` et animation C#, affichant sobrement un 'Badge' bleu listant le nombre de machines à disposition.

## ⚡ Derniers Correctifs Majeurs & Finalisation MVP
Des bugs extrêmement spécifiques inhérents au développement de logiciel local ont été éliminés avec succès :

1. **Bug d'Identifiant Temporel (ID Collision RAM)** : Le Tapis T13 associait mal la position des locomotives avant réouverture, car les voies fraîchement créées recevaient toutes l'index par défaut "0", créant une collision. Corrigé par `System.Threading.Interlocked.Decrement(ref _nextTempId)` générant des IDs négatifs temporels imperméables.
2. **Fuites du Prévisionnel** : Ajouter l'option "Vider la tuile" et "Réinitialiser locomotives" a nécessité la conception de boucles purgeant adéquatement l'association `ForecastTargetRollingLineTrackId` des deux bouts du spectre pour que les machines recouvrent vraiment leur couleur Verte (Libre).
3. **Optimisation des Menus UI** : Intégration systématique des options dans les `DataTemplates` XAML sans duplication massive de code.

---
> Ce grand bond de la première ligne de code "WinForms" vers cette phase de maturation permet à **Ploco** d'être officiellement **prêt pour une période de test en production intensive (QA de 24h)**. L'architecture est totalement disposée à accueillir aisément des extensions comme un Auto-Save, l'Historique Ctrl+Z, ou encore une Barre de Recherche en Temps Réel.
