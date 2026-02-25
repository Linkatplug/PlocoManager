Voici ce qui a été fait depuis la dernière release :

- Migration vers une architecture logicielle moderne en MVVM.
- Déplacement des locomotives par Drag & Drop fluide sur les voies.
- Base de données SQLite 100% asynchrone (aucune interface bloquée lors des sauvegardes).
- Ajout du mode "Placement Prévisionnel" (fantômes bleus) pour anticiper les départs.
- Nouvelle fenêtre analytique "Tapis T13".
- Création dynamique de 4 types de Tuiles : Dépôt, Garage, Ligne de roulement, Voiture arrêt.
- Rétractation du tiroir latéral gauche pour gagner de l'espace visuel (avec badge dynamique).
- Couverture fonctionnelle assurée par 24 tests unitaires automatiques.
- Correction du plantage SQLite au démarrage.
- Correction des bugs de liens "fantômes" non effacés à la destruction d'une Tuile.
- Correction des collisions d'ID mémoires entraînant un mauvais affichage dans le Tapis T13 ("DISPO THL").
- Ajout de la commande "Vider la tuile" dans les menus contextuels.
- Ajout de la commande "Réinitialiser locomotives" dans l'application.
