using System;
using System.Collections.Generic;
using System.Linq;
using Ploco.Models;

namespace Ploco.Helpers
{
    public static class PrevisionnelLogicHelper
    {
        /// <summary>
        /// Renvoie vrai si la locomotive fantôme donnée a été engendrée par la locomotive source.
        /// </summary>
        public static bool IsGhostOf(LocomotiveModel sourceLoco, LocomotiveModel ghostLoco)
        {
            if (!ghostLoco.IsForecastGhost) return false;
            return ghostLoco.ForecastSourceLocomotiveId == sourceLoco.Id || ghostLoco.Number == sourceLoco.Number;
        }

        /// <summary>
        /// Nettoie toutes les locomotives "fantômes" (prévision) qui sont associées à une locomotive donnée,
        /// en les retirant de leurs voies respectives. Retourne le nombre de fantômes supprimés.
        /// </summary>
        public static int RemoveForecastGhostsFor(LocomotiveModel realLoco, IEnumerable<TileModel> tiles)
        {
            if (realLoco == null) return 0;
            int removedCount = 0;

            foreach (var tile in tiles)
            {
                foreach (var track in tile.Tracks)
                {
                    var ghostsToRemove = track.Locomotives
                        .Where(l => IsGhostOf(realLoco, l))
                        .ToList();

                    foreach (var ghost in ghostsToRemove)
                    {
                        track.Locomotives.Remove(ghost);
                        removedCount++;
                    }

                    if (ghostsToRemove.Any())
                    {
                        PlacementLogicHelper.EnsureTrackOffsets(track);
                    }
                }
            }
            return removedCount;
        }

        /// <summary>
        /// Nettoie toutes les locomotives "fantômes" de l'ensemble de l'application.
        /// </summary>
        public static void ClearAllGhosts(IEnumerable<TileModel> tiles)
        {
            foreach (var tile in tiles)
            {
                foreach (var track in tile.Tracks)
                {
                    var ghostsToRemove = track.Locomotives.Where(l => l.IsForecastGhost).ToList();
                    foreach (var ghost in ghostsToRemove)
                    {
                        track.Locomotives.Remove(ghost);
                    }

                    if (ghostsToRemove.Any())
                    {
                        PlacementLogicHelper.EnsureTrackOffsets(track);
                    }
                }
            }
        }

        /// <summary>
        /// Crée une copie exacte d'une locomotive pour le mode prévisionnel (Ghost).
        /// </summary>
        public static LocomotiveModel CreateGhostFrom(LocomotiveModel realLoco)
        {
            return new LocomotiveModel
            {
                Id = -Guid.NewGuid().GetHashCode(), // Fake ID temporaire
                Number = realLoco.Number,
                SeriesId = realLoco.SeriesId,
                SeriesName = realLoco.SeriesName,
                Status = realLoco.Status,
                Pool = realLoco.Pool,
                TractionPercent = realLoco.TractionPercent,
                HsReason = realLoco.HsReason,
                DefautInfo = realLoco.DefautInfo,
                TractionInfo = realLoco.TractionInfo,
                IsForecastGhost = true,
                ForecastSourceLocomotiveId = realLoco.Id,
                MaintenanceDate = realLoco.MaintenanceDate
            };
        }
    }
}
