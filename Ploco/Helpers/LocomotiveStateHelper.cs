using System;
using System.Linq;
using Ploco.Models;

namespace Ploco.Helpers
{
    public static class LocomotiveStateHelper
    {
        /// <summary>
        /// Vérifie si une locomotive peut légalement être posée sur une voie donnée selon les règles métier.
        /// </summary>
        public static bool CanDropLocomotiveOnTrack(LocomotiveModel loco, TrackModel targetTrack)
        {
            if (loco == null || targetTrack == null)
            {
                return false;
            }

            // Les locomotives prévisionnelles ne peuvent pas être glissées dans une voie physique standard via un Drop classique.
            if (loco.IsForecastGhost)
            {
                return false;
            }

            // Pour une 'RollingLine', seule 1 locomotive est autorisée (sauf si c'est pour un swap).
            if (targetTrack.Kind == TrackKind.RollingLine)
            {
                // Note : le swap gère l'échange plus tard, cette méthode vérifie l'état "immédiat".
                // L'UI peut autoriser le drop visuel puis traiter le swap dans un événement séparé.
                // Par sécurité métier stricte, ici on renvoie bool, typiquement géré en UI par une messagebox.
                if (targetTrack.Locomotives.Any() && !targetTrack.Locomotives.Contains(loco))
                {
                    // L'UI gérera le swap par dessus. Pour la validation basique, c'est OK d'autoriser le drop (renvoie true) si le code d'UI a prévu le swap
                    return true; 
                }
            }

            // Pour les tuiles "ArretLigne", une seule locomotive est autorisée.
            if (targetTrack.Kind == TrackKind.Main)
            {
                // On revérifie au cas où l'UI aurait loupé la règle. 
                // Un Arrêt Ligne est particulier, on devra injecter ParentTileType dans le TrackModel si on veut isoler ça parfaitement.
                // Pour l'instant, on laisse l'UI vérifier la tuile parente si besoin.
            }

            return true;
        }

        /// <summary>
        /// Vérifie si une locomotive est éligible pour un échange (Swap).
        /// </summary>
        public static bool IsEligibleForSwap(LocomotiveModel loco1, LocomotiveModel loco2)
        {
            if (loco1 == null || loco2 == null) return false;
            if (loco1.IsForecastGhost || loco2.IsForecastGhost) return false;
            
            return true;
        }

        /// <summary>
        /// Renvoie vrai si la locomotive nécessite des réparations / empêche un départ.
        /// </summary>
        public static bool IsLocomotiveHs(LocomotiveModel loco)
        {
            return loco.Status == LocomotiveStatus.HS || loco.Status == LocomotiveStatus.ManqueTraction;
        }
    }
}
