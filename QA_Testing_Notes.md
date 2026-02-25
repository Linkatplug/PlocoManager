# Fiche de Tests QA - Ploco (Phase Refactoring MVP)

**Version / Build Tested :** _______________  
**Testeur QA :** __________________________  
**Date du test :** _________________________

___

## Contexte de la Release
Cette version intègre une restructuration massive du code source de l'application Ploco :
1. **Passage au modèle MVVM** pour l'interface graphique (séparation de la logique visuelle et métier).
2. **Asynchronisme des données (SQLite)** pour empêcher le blocage complet de l'interface lors d'une lecture ou d'une sauvegarde.
3. **Mise à jour du système de Drag & Drop** (Glisser-Déposer) des matériels roulants (Locomotives).

L'objectif de cette phase de QA de 24h est d'éprouver **exclusivement la stabilité des interactions utilisateur habituelles**.

## ✅ 1. Tests de Base de Données (Asynchronisme)
*L'application ne doit générer aucun freeze ni plantage lors d'opérations d'enregistrement lourds.*

- [ ] L'application démarre et affiche toutes les locomotives et tuiles de la session précédente.
- [ ] Fermer Ploco puis le relancer ne génère **aucun crash ni perte de données** par rapport au dernier état visuel.
- [ ] Charger un ancien fichier Backup `.db` manquant de certaines colonnes via `Fichier -> Charger un espace de travail` (Vérification des vérifications de schémas adaptatives).
- [ ] Exporter l'état de la base (`Fichier -> Exporter Base`) s'exécute sans figer l'écran.
- [ ] Supprimer toutes les locos / toutes les tuiles via le menu supprime tout sans crasher.

**📝 Notes / Bugs relevés (BDD) :**
<br><br><br><br>

## ✅ 2. Tests Système Glisser-Déposer (Drag & Drop MVVM)
*Ce métier a été complètement réécrit. L'emplacement de la souris dicte l'ordre visuel sur une ligne.*

- [ ] Je peux glisser une locomotive du Pool (gauche) vers une voie vide (droite).
- [ ] La zone de prise d'une locomotive n'est plus "trop petite" : on peut facilement la saisir peu importe l'endroit cliqué sur la tuile.
- [ ] Déplacer horizontalement une loco au milieu de 4 locos existantes la place "correctement" entre les bonnes locos et décale les suivantes (Système de "Slots").
- [ ] Lâcher une locomotive en dehors d'une tuile (dans le vide complet) renvoie visuellement celle-ci dans son bassin (Pool de gauche).
- [ ] Swap automatique : Lâcher une loco de Pool "Lineas" sur une voie **déjà occupée par une locomotive Sibelit** échange physiquement les deux tuiles.

**📝 Notes / Bugs relevés (Glisser-Déposer) :**
<br><br><br><br>

## ✅ 3. Tests Mode Prévisionnel (Mode Fantôme)
*Les "fantômes" sont les copies grises d'une locomotive.*

- [ ] Je peux utiliser le Clic Droit -> "Placement Prévisionnel" depuis le Pool de gauche pour générer un double fantôme avec l'icône calendrier sur une ligne.
- [ ] Annuler un placement prévisionnel efface bien le fantôme sans toucher au système global ni effacer la loco primaire.
- [ ] Sur un Clic Droit -> "Valider le placement prévisionnel", le fantôme disparaît et laisse place à la LA VRAIE locomotive (changement colorimétrique bleu / jaune et disparition du logo calendrier).
- [ ] Redémarrer l'application maintient mon fantôme gris exactement là où je l'avais laissé. L'application ne plante pas au redémarrage car l'ID asynchrone est factice (négatif).

**📝 Notes / Bugs relevés (Mode Prévisionnel) :**
<br><br><br><br>

## ✅ 4. Comportements Interface Secondaire
*Bugs résiduels éventuels liés à une instabilité des données asynchrones.*

- [ ] Modifier l'état d'une Loco via clic droit (ex: Ok -> HS) met à jour la couleur du Pool **instantanément**.
- [ ] Ouvrir la fenêtre `Outils -> Tapis T13` n'interrompt pas brutalement l'interface principale.
- [ ] Utiliser la barre de recherche des Pools filtre bien ce qui existe aujourd'hui.
- [ ] Manipuler les tuiles grises elles-mêmes (Drag des bordures pour redimensionner, Drag complet de la boîte grise de Ligne G pour la repositionner) conserve le bon ordre des sous-composants à l'écran.

**📝 Notes / Bugs relevés (UI Secondaire) :**
<br><br><br><br><br>

---
*Fin du rapport de tests.*
