using System;
using System.Collections.Generic;
using System.Linq;
using Ploco.Models;

namespace Ploco.Helpers
{
    public static class PlacementLogicHelper
    {
        private const double SlotWidth = 44.0;

        /// <summary>
        /// Détermine l'index d'insertion d'une locomotive par rapport à sa position X (DropPosition).
        /// </summary>
        public static int GetInsertIndex(IEnumerable<LocomotiveModel> locomotives, double dropX)
        {
            var sortedLocos = locomotives.OrderBy(l => l.AssignedTrackOffsetX ?? 0).ToList();
            var index = 0;
            foreach (var loco in sortedLocos)
            {
                var currentX = loco.AssignedTrackOffsetX ?? 0;
                var currentWidth = SlotWidth;

                if (dropX < currentX + (currentWidth / 2))
                {
                    break;
                }
                index++;
            }
            return index;
        }

        /// <summary>
        /// S'assure que les offsets de toutes les locomotives sur une voie sont corrects pour éviter les chevauchements.
        /// </summary>
        public static void EnsureTrackOffsets(TrackModel track)
        {
            if (track.Kind != TrackKind.Line && track.Kind != TrackKind.Zone && track.Kind != TrackKind.Output)
            {
                foreach (var loco in track.Locomotives)
                {
                    loco.AssignedTrackOffsetX = null;
                }
                return;
            }

            var occupiedSlots = new HashSet<int>();
            foreach (var loco in track.Locomotives)
            {
                if (loco.AssignedTrackOffsetX.HasValue)
                {
                    var slot = (int)Math.Round(loco.AssignedTrackOffsetX.Value / SlotWidth);
                    if (!occupiedSlots.Contains(slot))
                    {
                        occupiedSlots.Add(slot);
                        loco.AssignedTrackOffsetX = slot * SlotWidth;
                        continue;
                    }
                }

                var fallbackSlot = 0;
                while (occupiedSlots.Contains(fallbackSlot))
                {
                    fallbackSlot++;
                }

                occupiedSlots.Add(fallbackSlot);
                loco.AssignedTrackOffsetX = fallbackSlot * SlotWidth;
            }
        }
        
        /// <summary>
        /// Calcule le meilleur offset disponible sur une voie en fonction du dropX, pour une nouvelle locomotive.
        /// Retourne le nouvel offset.
        /// </summary>
        public static double CalculateBestOffset(TrackModel track, LocomotiveModel locoToPlace, double dropPositionX, double actualWidth)
        {
            var maxX = Math.Max(0, actualWidth - SlotWidth);
            var clampedX = Math.Max(0, Math.Min(dropPositionX, maxX));
            var desiredSlot = (int)Math.Round(clampedX / SlotWidth);
            
            var occupiedSlots = track.Locomotives
                .Where(item => !ReferenceEquals(item, locoToPlace))
                .Select(item => item.AssignedTrackOffsetX.HasValue ? (int)Math.Round(item.AssignedTrackOffsetX.Value / SlotWidth) : -1)
                .Where(slot => slot >= 0)
                .ToHashSet();

            var slot = desiredSlot;
            while (occupiedSlots.Contains(slot))
            {
                slot++;
            }

            return slot * SlotWidth;
        }
    }
}
