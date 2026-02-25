using Xunit;
using Ploco.Models;
using Ploco.Helpers;

namespace Ploco.Tests.Helpers
{
    public class LocomotiveStateHelperTests
    {
        [Fact]
        public void CanDropLocomotiveOnTrack_WithNullInputs_ReturnsFalse()
        {
            // Arrange
            var loco = new LocomotiveModel();
            var track = new TrackModel();

            // Act & Assert
            Assert.False(LocomotiveStateHelper.CanDropLocomotiveOnTrack(null, track));
            Assert.False(LocomotiveStateHelper.CanDropLocomotiveOnTrack(loco, null));
            Assert.False(LocomotiveStateHelper.CanDropLocomotiveOnTrack(null, null));
        }

        [Fact]
        public void CanDropLocomotiveOnTrack_WithForecastGhost_ReturnsFalse()
        {
            // Arrange
            var loco = new LocomotiveModel { IsForecastGhost = true };
            var track = new TrackModel();

            // Act
            var result = LocomotiveStateHelper.CanDropLocomotiveOnTrack(loco, track);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CanDropLocomotiveOnTrack_ValidInputs_ReturnsTrue()
        {
            // Arrange
            var loco = new LocomotiveModel { IsForecastGhost = false };
            var track = new TrackModel { Kind = TrackKind.Line };

            // Act
            var result = LocomotiveStateHelper.CanDropLocomotiveOnTrack(loco, track);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsEligibleForSwap_WithNullInputs_ReturnsFalse()
        {
            var loco = new LocomotiveModel();

            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(null, loco));
            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(loco, null));
            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(null, null));
        }

        [Fact]
        public void IsEligibleForSwap_WithAnyGhost_ReturnsFalse()
        {
            var ghost = new LocomotiveModel { IsForecastGhost = true };
            var normal = new LocomotiveModel { IsForecastGhost = false };

            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(ghost, normal));
            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(normal, ghost));
            Assert.False(LocomotiveStateHelper.IsEligibleForSwap(ghost, ghost));
        }

        [Fact]
        public void IsEligibleForSwap_WithTwoNormalLocos_ReturnsTrue()
        {
            var loco1 = new LocomotiveModel { IsForecastGhost = false };
            var loco2 = new LocomotiveModel { IsForecastGhost = false };

            Assert.True(LocomotiveStateHelper.IsEligibleForSwap(loco1, loco2));
        }

        [Theory]
        [InlineData(LocomotiveStatus.HS, true)]
        [InlineData(LocomotiveStatus.ManqueTraction, true)]
        [InlineData(LocomotiveStatus.Ok, false)]
        [InlineData(LocomotiveStatus.DefautMineur, false)]
        public void IsLocomotiveHs_ReturnsExpectedResult(LocomotiveStatus status, bool expected)
        {
            var loco = new LocomotiveModel { Status = status };

            var result = LocomotiveStateHelper.IsLocomotiveHs(loco);

            Assert.Equal(expected, result);
        }
    }
}
