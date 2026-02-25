using System.Collections.Generic;

namespace Ploco.Models
{
    public class LayoutPreset
    {
        public string Name { get; set; } = string.Empty;
        public List<LayoutTile> Tiles { get; set; } = new();
    }

    public class LayoutTile
    {
        public string Name { get; set; } = string.Empty;
        public TileType Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<LayoutTrack> Tracks { get; set; } = new();
    }

    public class LayoutTrack
    {
        public string Name { get; set; } = string.Empty;
        public TrackKind Kind { get; set; }
        public string? LeftLabel { get; set; }
        public string? RightLabel { get; set; }
        public bool IsLeftBlocked { get; set; }
        public bool IsRightBlocked { get; set; }
    }
}
