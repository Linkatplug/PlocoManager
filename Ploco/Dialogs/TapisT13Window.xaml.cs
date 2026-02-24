using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco.Dialogs
{
    public partial class TapisT13Window : Window, IRefreshableWindow
    {
        private readonly ObservableCollection<T13Row> _rows = new();
        private readonly IEnumerable<LocomotiveModel> _locomotives;
        private readonly IEnumerable<TileModel> _tiles;

        public TapisT13Window(IEnumerable<LocomotiveModel> locomotives, IEnumerable<TileModel> tiles)
        {
            InitializeComponent();
            T13Grid.ItemsSource = _rows;
            _locomotives = locomotives;
            _tiles = tiles;
            LoadRows(_locomotives, _tiles);
        }

        private void LoadRows(IEnumerable<LocomotiveModel> locomotives, IEnumerable<TileModel> tiles)
        {
            _rows.Clear();
            var tracks = tiles.SelectMany(tile => tile.Tracks).ToList();
            var allLocomotives = locomotives.ToList();

            foreach (var loco in allLocomotives
                         .Where(l => IsT13(l) && string.Equals(l.Pool, "Sibelit", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(l => l.Number))
            {
                var realTrack = tracks.FirstOrDefault(t => t.Locomotives.Contains(loco));
                var ghostTrack = (loco.IsForecastOrigin && loco.ForecastTargetRollingLineTrackId.HasValue) 
                    ? tracks.FirstOrDefault(t => t.Id == loco.ForecastTargetRollingLineTrackId.Value)
                    : null;

                var isHs = loco.Status == LocomotiveStatus.HS;
                string locHs, report;
                bool isNonHsOnLine;

                if (ghostTrack != null)
                {
                    string originStr = GetLocationTextForTrack(loco, realTrack, tiles);
                    string targetStr = GetLocationTextForTrack(loco, ghostTrack, tiles);
                    report = $"{originStr} + {targetStr}";
                    locHs = isHs ? report : string.Empty;

                    isNonHsOnLine = !isHs && ((realTrack?.Kind == TrackKind.Line && realTrack?.IsOnTrain == true) || 
                                              (ghostTrack?.Kind == TrackKind.Line && ghostTrack?.IsOnTrain == true));
                }
                else
                {
                    var trainLocationText = GetTrainLocationText(loco, realTrack, tiles);
                    var rollingLineNumber = ResolveRollingLineNumber(realTrack);

                    isNonHsOnLine = !isHs && realTrack?.Kind == TrackKind.Line && realTrack?.IsOnTrain == true;

                    if (isHs && !string.IsNullOrWhiteSpace(rollingLineNumber))
                    {
                        locHs = GetOriginTileLocation(loco, tiles);
                        report = $"HS CV {rollingLineNumber}";
                    }
                    else
                    {
                        locHs = isHs ? trainLocationText : string.Empty;
                        report = isHs ? trainLocationText 
                            : isNonHsOnLine ? trainLocationText
                            : !string.IsNullOrWhiteSpace(rollingLineNumber) ? rollingLineNumber
                            : trainLocationText;
                    }
                }
                
                // Motif/Status info for HS, DefautMineur, and ManqueTraction
                var motif = loco.Status switch
                {
                    LocomotiveStatus.HS => loco.HsReason ?? string.Empty,
                    LocomotiveStatus.DefautMineur => loco.DefautInfo ?? string.Empty,
                    LocomotiveStatus.ManqueTraction => FormatTractionMotif(loco),
                    _ => string.Empty
                };

                _rows.Add(new T13Row
                {
                    Locomotive = loco.Number.ToString(),
                    MaintenanceDate = loco.MaintenanceDate ?? string.Empty,
                    Motif = motif,
                    LocHs = locHs,
                    Report = report,
                    IsHs = isHs,
                    IsNonHsOnLine = isNonHsOnLine,
                    Status = loco.Status
                });
            }

            UpdateSummary();
        }
        
        private static string GetLocationTextForTrack(LocomotiveModel loco, TrackModel? track, IEnumerable<TileModel> tiles)
        {
            if (track == null) return string.Empty;
            if (track.Kind == TrackKind.RollingLine) return track.Name;
            return GetTrainLocationText(loco, track, tiles);
        }

        /// <summary>
        /// Gets the origin tile location for a locomotive.
        /// Searches for the tile where the locomotive originates (Depot/Garage/Line tracks, not RollingLine).
        /// </summary>
        private static string GetOriginTileLocation(LocomotiveModel loco, IEnumerable<TileModel> tiles)
        {
            // Find the tile where loco originates (in Depot/Garage/Line tracks, not RollingLine)
            foreach (var tile in tiles)
            {
                foreach (var track in tile.Tracks.Where(t => t.Kind != TrackKind.RollingLine))
                {
                    // Match by Id or Number (to handle WPF instance mismatches)
                    if (track.Locomotives.Any(l => l.Id == loco.Id || l.Number == loco.Number))
                    {
                        return ResolveLocation(track, tiles);
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// Gets the train location text for a locomotive.
        /// Handles different track kinds and locomotive statuses.
        /// </summary>
        private static string GetTrainLocationText(LocomotiveModel loco, TrackModel? track, IEnumerable<TileModel> tiles)
        {
            if (track == null)
            {
                return string.Empty;
            }

            var location = ResolveLocation(track, tiles);
            
            // For Line track with train: "TileName TrainNumber"
            if (track.Kind == TrackKind.Line && track.IsOnTrain && !string.IsNullOrWhiteSpace(track.TrainNumber))
            {
                return $"{location} {track.TrainNumber}";
            }
            
            // For OK locomotives in depot/garage (not Line, not RollingLine): "DISPO TileName"
            if (loco.Status == LocomotiveStatus.Ok && 
                track.Kind != TrackKind.Line && 
                track.Kind != TrackKind.RollingLine)
            {
                return $"DISPO {location}";
            }

            // Otherwise just the location
            return location;
        }

        private static bool IsT13(LocomotiveModel loco)
        {
            return loco.SeriesName.Contains("1300", StringComparison.OrdinalIgnoreCase)
                   || loco.SeriesName.Contains("T13", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveLocation(TrackModel? track, IEnumerable<TileModel> tiles)
        {
            if (track == null)
            {
                return string.Empty;
            }

            var tile = tiles.FirstOrDefault(t => t.Tracks.Contains(track));
            if (tile == null)
            {
                return track.Name;
            }

            return GetLocationAbbreviation(tile.Name);
        }

        private static string ResolveRollingLineNumber(TrackModel? track)
        {
            if (track?.Kind != TrackKind.RollingLine)
            {
                return string.Empty;
            }

            return track.Name;
        }

        private static string GetLocationAbbreviation(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var normalized = name.Trim();
            return normalized switch
            {
                "Thionville" => "THL",
                "SRH" => "SRH",
                "Anvers Nord" => "FN",
                "Anvers" => "FN",
                "Mulhouse Nord" => "MUN",
                "Mulhouse" => "MUN",
                "Bale" => "BAL",
                "Woippy" => "WPY",
                "Uckange" => "UCK",
                "Zeebrugge" => "LZR",
                "Gent" => "FGZH",
                "Muizen" => "FIZ",
                "Monceau" => "LNC",
                "La Louviere" => "GLI",
                "Chatelet" => "FCL",
                _ => normalized
            };
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyLocomotive_Click(object sender, RoutedEventArgs e)
        {
            CopyColumn(row => row.Locomotive);
        }

        private void CopyLocHs_Click(object sender, RoutedEventArgs e)
        {
            CopyColumn(row => row.LocHs);
        }

        private void CopyReport_Click(object sender, RoutedEventArgs e)
        {
            CopyColumn(row => row.Report);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private void CopyColumn(Func<T13Row, string> selector)
        {
            var text = string.Join(Environment.NewLine, _rows.Select(selector));
            Clipboard.SetText(text);
        }

        private void UpdateSummary()
        {
            var total = _rows.Count;
            var hsCount = _rows.Count(r => r.IsHs);
            var okCount = total - hsCount;
            SummaryText.Text = $"Total : {total} · HS : {hsCount} · OK : {okCount}";
        }

        public void RefreshData()
        {
            var selected = T13Grid.SelectedItem as T13Row;
            var selectedKey = selected?.Locomotive;
            LoadRows(_locomotives, _tiles);
            if (!string.IsNullOrWhiteSpace(selectedKey))
            {
                var row = _rows.FirstOrDefault(item => item.Locomotive == selectedKey);
                if (row != null)
                {
                    T13Grid.SelectedItem = row;
                }
            }
        }

        private static string FormatTractionMotif(LocomotiveModel loco)
        {
            if (!loco.TractionPercent.HasValue)
                return string.Empty;
            
            var percent = $"{loco.TractionPercent}%";
            
            if (!string.IsNullOrWhiteSpace(loco.TractionInfo))
            {
                return $"{percent} {loco.TractionInfo}";
            }
            
            return percent;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window settings
            WindowSettingsHelper.RestoreWindowSettings(this, "TapisT13Window");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save window settings
            WindowSettingsHelper.SaveWindowSettings(this, "TapisT13Window");
        }

        private sealed class T13Row
        {
            public string Locomotive { get; set; } = string.Empty;
            public string MaintenanceDate { get; set; } = string.Empty;
            public string Motif { get; set; } = string.Empty;
            public string LocHs { get; set; } = string.Empty;
            public string Report { get; set; } = string.Empty;
            public bool IsHs { get; set; }
            public bool IsNonHsOnLine { get; set; }
            public LocomotiveStatus Status { get; set; }
        }
    }
}
