using System.Collections.Generic;
using System.Linq;
using Xunit;
using Ploco.Models;
using Ploco.Helpers;

namespace Ploco.Tests.Helpers
{
    public class PrevisionnelLogicHelperTests
    {
        [Fact]
        public void CreateGhostFrom_ReturnsCorrectGhostProperties()
        {
            // Arrange
            var sourceLoco = new LocomotiveModel
            {
                Id = 42,
                Number = 1301,
                SeriesId = 1,
                Status = LocomotiveStatus.HS,
                Pool = "Lineas"
            };

            // Act
            var ghost = PrevisionnelLogicHelper.CreateGhostFrom(sourceLoco);

            // Assert
            Assert.True(ghost.IsForecastGhost);
            Assert.NotEqual(sourceLoco.Id, ghost.Id); // ID should be a negative fake hash
            Assert.Equal(sourceLoco.Id, ghost.ForecastSourceLocomotiveId);
            Assert.Equal(sourceLoco.Number, ghost.Number);
            Assert.Equal(sourceLoco.Status, ghost.Status);
        }

        [Fact]
        public void IsGhostOf_ReturnsTrueForMatchingSourceAndGhost()
        {
            var sourceLoco = new LocomotiveModel { Id = 42, Number = 1301 };
            var ghostLoco = new LocomotiveModel { IsForecastGhost = true, ForecastSourceLocomotiveId = 42, Number = 1301 };

            var result = PrevisionnelLogicHelper.IsGhostOf(sourceLoco, ghostLoco);

            Assert.True(result);
        }

        [Fact]
        public void IsGhostOf_ReturnsFalseForNotAGhost()
        {
            var sourceLoco = new LocomotiveModel { Id = 42 };
            var normalLoco = new LocomotiveModel { IsForecastGhost = false, Id = 99 };

            var result = PrevisionnelLogicHelper.IsGhostOf(sourceLoco, normalLoco);

            Assert.False(result);
        }

        [Fact]
        public void RemoveForecastGhostsFor_RemovesOnlyTargetGhosts()
        {
            // Arrange
            var sourceLoco = new LocomotiveModel { Id = 42, Number = 1301 };
            
            // Target ghost
            var ghost1 = new LocomotiveModel { IsForecastGhost = true, ForecastSourceLocomotiveId = 42, Number = 1301 };
            
            // Unrelated ghost
            var ghost2 = new LocomotiveModel { IsForecastGhost = true, ForecastSourceLocomotiveId = 99, Number = 1302 };
            
            // Normal loco
            var normalLoco = new LocomotiveModel { IsForecastGhost = false, Id = 100, Number = 1303 };

            var tile = new TileModel();
            var track = new TrackModel();
            track.Locomotives.Add(ghost1);
            track.Locomotives.Add(ghost2);
            track.Locomotives.Add(normalLoco);
            tile.Tracks.Add(track);

            var tiles = new List<TileModel> { tile };

            // Act
            var removedCount = PrevisionnelLogicHelper.RemoveForecastGhostsFor(sourceLoco, tiles);

            // Assert
            Assert.Equal(1, removedCount);
            Assert.DoesNotContain(ghost1, track.Locomotives); // Was removed
            Assert.Contains(ghost2, track.Locomotives); // Kept (unrelated ghost)
            Assert.Contains(normalLoco, track.Locomotives); // Kept (not a ghost)
        }
    }
}
