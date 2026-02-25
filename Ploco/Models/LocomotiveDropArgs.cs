using System.Windows;

namespace Ploco.Models
{
    public class LocomotiveDropArgs
    {
        public LocomotiveModel Loco { get; set; } = null!;
        public object Target { get; set; } = null!; // TrackModel, TileModel, or null/identifier for Pool
        public int InsertIndex { get; set; }
        public Point DropPosition { get; set; }
        public double TargetActualWidth { get; set; }
        public bool IsRollingLineRow { get; set; }
    }
}
