using System.Collections.Generic;
using Xunit;
using Ploco.Models;
using Ploco.Helpers;

namespace Ploco.Tests.Helpers
{
    public class PlacementLogicHelperTests
    {
        [Fact]
        public void GetInsertIndex_EmptyList_ReturnsZero()
        {
            var locos = new List<LocomotiveModel>();
            var index = PlacementLogicHelper.GetInsertIndex(locos, 100);
            Assert.Equal(0, index);
        }

        [Fact]
        public void GetInsertIndex_DropBeforeFirst_ReturnsZero()
        {
            var locos = new List<LocomotiveModel>
            {
                new LocomotiveModel { AssignedTrackOffsetX = 50 }
            };
            
            // Drop at 10, SlotWidth is 44, so currentX + width/2 = 50 + 22 = 72. 10 < 72 -> index 0
            var index = PlacementLogicHelper.GetInsertIndex(locos, 10);
            
            Assert.Equal(0, index);
        }

        [Fact]
        public void GetInsertIndex_DropAfterFirst_ReturnsOne()
        {
            var locos = new List<LocomotiveModel>
            {
                new LocomotiveModel { AssignedTrackOffsetX = 10 }
            };
            
            // Drop at 100, currentX(10) + 22 = 32. 100 is not < 32. -> index 1
            var index = PlacementLogicHelper.GetInsertIndex(locos, 100);
            
            Assert.Equal(1, index);
        }

        [Fact]
        public void CalculateBestOffset_EmptyTrack_ReturnsCorrectSlot()
        {
            var track = new TrackModel { Kind = TrackKind.Line };
            var loco = new LocomotiveModel();
            
            // Drop at X=100. 100 / 44 = 2.27 -> Round to 2.
            // Expected offset: 2 * 44 = 88.
            var bestOffset = PlacementLogicHelper.CalculateBestOffset(track, loco, 100, 500);
            
            Assert.Equal(88.0, bestOffset);
        }

        [Fact]
        public void CalculateBestOffset_SlotOccupied_ReturnsNextAvailableSlot()
        {
            var track = new TrackModel { Kind = TrackKind.Line };
            var locoToPlace = new LocomotiveModel();
            var existingLoco = new LocomotiveModel { AssignedTrackOffsetX = 88 }; // Occupies slot 2
            
            track.Locomotives.Add(existingLoco);
            
            // Drop at X=100. Desired slot is 2. Slot 2 is occupied, so it should fallback to slot 3.
            // Expected offset: 3 * 44 = 132.
            var bestOffset = PlacementLogicHelper.CalculateBestOffset(track, locoToPlace, 100, 500);
            
            Assert.Equal(132.0, bestOffset);
        }
        
        [Fact]
        public void CalculateBestOffset_ClampsToActualWidth()
        {
            var track = new TrackModel { Kind = TrackKind.Line };
            var loco = new LocomotiveModel();
            
            // Drop at X=1000, but ActualWidth is only 200. MaxX = 200 - 44 = 156.
            // ClampedX = 156. Desired slot = 156 / 44 = 3.54 -> Round to 4.
            // Expected offset: 4 * 44 = 176.
            var bestOffset = PlacementLogicHelper.CalculateBestOffset(track, loco, 1000, 200);
            
            Assert.Equal(176.0, bestOffset);
        }
    }
}
