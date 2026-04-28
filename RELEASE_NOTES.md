# Ploco - Notes de Version

## Version 2.0.0 - Février 2026 : La Renaissance Architecturale

**Date de release** : 25 février 2026

Cette version **2.0.0** est probablement la mise à jour la plus importante de l'histoire de Ploco. Elle marque l'aboutissement d'une refonte architecturale totale (Refactoring en 4 phases) et l'introduction de fonctionnalités d'interface utilisateur très demandées. Ploco est désormais plus rapide, plus stable et plus ergonomique que jamais.

---

## 🛠️ Refonte Architecturale Majeure (Sous le capot)

L'application a été entièrement réécrite pour abandonner son "Code-Behind" monolithique au profit des standards de développement modernes de Microsoft.

1. **Migration vers MVVM Stricte (Phase 1 à 3)**
   - L'interface ne gère plus la logique. Tout le cerveau de l'application est dorénavant encapsulé dans le `MainViewModel`.
   - L'introduction d'un conteneur d'**Injection de Dépendances (IoC)** garantit que chaque service (Base de données, Dialogues) est appelé de façon sécurisée et isolée.
   - Les actions complexes (telles que le fameux Drag & Drop intelligent des locomotives et la gestion des "Fantômes") ont été portées vers des *Attached Behaviors* et des *RelayCommands* pures.

2. **Asynchronisme de bout en bout (Phase 4)**
   - **Adieu les freezes !** L'intégralité du dialogue avec la base de données SQLite (`IPlocoRepository`) a été basculée en asynchrone (`async/await`). 
   - Qu'il s'agisse de charger l'historique massif, d'importer un Excel de 500 lignes, ou de sauvegarder le Tapis T13, l'interface utilisateur restera fluide et réactive à 100%.

3. **Environnement de Tests Unitaires (Phase 4)**
   - Réécriture de la logique mathématique et métier sous forme de `Helpers` purs, désormais blindés par une suite de **Tests Unitaires Automatisés (xUnit & Moq)**.
   - La prévention des bugs régressifs est désormais garantie mathématiquement avant chaque livraison.

---

## 🎨 Nouvelles Fonctionnalités d'Interface (UI)

Outre la stabilité monumentale apportée par le refactoring, de nouveaux superbes ajouts ergonomiques complètent cette V2 :

### 1. Tiroir Latéral Rétractable (Drawer)
La gestion de l'espace a été repensée. Le menu latéral gauche (contenant les locomotives non assignées) est désormais **rétractable**.
- Un simple clic permet de masquer ou d'afficher le tiroir.
- Maximise l'espace dédié au Canvas (vos dépôts et lignes) sur les plus petits écrans.
- Animation fluide de l'ouverture et fermeture (si activée).

### 2. Splash Screen de Chargement
Lors de la première ouverture ou de chargements massifs de bases de données (imports lourds), Ploco affiche dorénavant un élégant **Écran de chargement (Splash Screen)** au lieu de figer l'écran ou de présenter une interface vide. Vous savez toujours ce que Ploco est en train de faire.

### 3. Refonte Visuelle du Placement Prévisionnel
Les couleurs du système de "Fantômes" (Forecast) ont été retravaillées pour une meilleure lisibilité visuelle selon les retours métier :
- **Locomotive Originale en attente** : Affichée en **Bleu**.
- **Fantôme cible (Copie)** : Affiché en **Bleu Clair**.
- Ce code couleur remplace l'ancien Vert, permettant de mieux contraster avec le Statut "OK" (Vert).

---

## ⚡ Rappel des Fonctionnalités Clés Récentes (V1.0.5 incluses dans la V2)

### Gestion Intelligente des Pools
- Double-clic immédiat pour transférer une loco de *Sibelit* vers *Lineas*.
- Outil d'Import intelligent Excel ↔ Base de données pour synchroniser un parc complet en un clic.

### Système de Statuts Avancé
- Les 4 statuts historiques : ✅ **OK**, 🟡 **Défaut Mineur** (avec commentaire obligatoire), 🟠 **Manque Traction** (avec %), et 🔴 **HS**.
- Cohérence parfaite sur la grille, la liste et le rendu final PDF du rapport Tapis T13.

### Mémorisation de l'Espace de Travail
- La taille, la position de vos fenêtres, et vos préférences de mode sombre sont toujours sauvegardées.

### Logging unifié
- Système propulsé par `Serilog` garantissant que le moindre comportement (déplacement, mise à jour, erreur SQLite) est audité et conservé pendant 30 jours dans votre répertoire système `%AppData%`.

---

## 🐛 Corrections de Bugs et Optimisations
- Élimination de plus de 15 *Warnings* de nullité structurale au moment de la compilation (`CS8600`, `CS8618`).
- Correction complexe de l'affichage du Tapis T13 où la vue et la mémoire luttaient parfois à contresens (résolu via MVC asynchrone).

---

**Version 2.0.0 - Une fondation indestructible pour le futur de Ploco ! 🚂✨**
