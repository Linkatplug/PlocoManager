using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ploco.Models
{
    public enum LocomotiveStatus
    {
        Ok,
        ManqueTraction,
        HS,
        DefautMineur
    }

    public enum TileType
    {
        Depot,
        ArretLigne,
        VoieGarage,
        RollingLine
    }

    public enum TrackKind
    {
        Main,
        Output,
        Zone,
        Line,
        RollingLine
    }



    public class RollingStockSeries
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int StartNumber { get; set; }
        public int EndNumber { get; set; }
    }

    public class LocomotiveModel : INotifyPropertyChanged
    {
        private string _pool = "Lineas";
        private LocomotiveStatus _status;
        private int? _tractionPercent;
        private string? _hsReason;
        private string? _defautInfo;
        private string? _tractionInfo;
        private string? _maintenanceDate;
        private double? _assignedTrackOffsetX;
        private int? _assignedTrackId;
        private bool _isVisibleInActivePool = true;
        private bool _isForecastOrigin;
        private int? _forecastTargetRollingLineTrackId;
        private bool _isForecastGhost;
        private int? _forecastSourceLocomotiveId;

        public int Id { get; set; }
        public int SeriesId { get; set; }
        public string SeriesName { get; set; } = string.Empty;
        public int Number { get; set; }

        public LocomotiveStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TractionDisplay));
                }
            }
        }

        public int? TractionPercent
        {
            get => _tractionPercent;
            set
            {
                if (_tractionPercent != value)
                {
                    _tractionPercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TractionDisplay));
                }
            }
        }

        public string? HsReason
        {
            get => _hsReason;
            set
            {
                if (_hsReason != value)
                {
                    _hsReason = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? DefautInfo
        {
            get => _defautInfo;
            set
            {
                if (_defautInfo != value)
                {
                    _defautInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? TractionInfo
        {
            get => _tractionInfo;
            set
            {
                if (_tractionInfo != value)
                {
                    _tractionInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? MaintenanceDate
        {
            get => _maintenanceDate;
            set
            {
                if (_maintenanceDate != value)
                {
                    _maintenanceDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Pool
        {
            get => _pool;
            set
            {
                if (_pool != value)
                {
                    _pool = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? AssignedTrackId
        {
            get => _assignedTrackId;
            set
            {
                if (_assignedTrackId != value)
                {
                    _assignedTrackId = value;
                    OnPropertyChanged();
                }
            }
        }

        public double? AssignedTrackOffsetX
        {
            get => _assignedTrackOffsetX;
            set
            {
                if (_assignedTrackOffsetX != value)
                {
                    _assignedTrackOffsetX = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsVisibleInActivePool
        {
            get => _isVisibleInActivePool;
            set
            {
                if (_isVisibleInActivePool != value)
                {
                    _isVisibleInActivePool = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsForecastOrigin
        {
            get => _isForecastOrigin;
            set
            {
                if (_isForecastOrigin != value)
                {
                    _isForecastOrigin = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? ForecastTargetRollingLineTrackId
        {
            get => _forecastTargetRollingLineTrackId;
            set
            {
                if (_forecastTargetRollingLineTrackId != value)
                {
                    _forecastTargetRollingLineTrackId = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsForecastGhost
        {
            get => _isForecastGhost;
            set
            {
                if (_isForecastGhost != value)
                {
                    _isForecastGhost = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? ForecastSourceLocomotiveId
        {
            get => _forecastSourceLocomotiveId;
            set
            {
                if (_forecastSourceLocomotiveId != value)
                {
                    _forecastSourceLocomotiveId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName => Number.ToString();
        public string TractionDisplay => Status == LocomotiveStatus.ManqueTraction && TractionPercent.HasValue
            ? $"{TractionPercent.Value}%"
            : string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class HistoryEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class TrackModel : INotifyPropertyChanged
    {
        private TrackKind _kind;
        private string _name = string.Empty;
        private bool _isOnTrain;
        private string? _stopTime;
        private string? _issueReason;
        private bool _isLocomotiveHs;
        private string? _leftLabel;
        private string? _rightLabel;
        private bool _isLeftBlocked;
        private bool _isRightBlocked;
        private string? _trainNumber;

        private static int _nextTempId = -1;
        public int Id { get; set; } = System.Threading.Interlocked.Decrement(ref _nextTempId);
        public int TileId { get; set; }
        public int Position { get; set; }

        public TrackKind Kind
        {
            get => _kind;
            set
            {
                if (_kind != value)
                {
                    _kind = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsOnTrain
        {
            get => _isOnTrain;
            set
            {
                if (_isOnTrain != value)
                {
                    _isOnTrain = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineInfo));
                }
            }
        }

        public string? StopTime
        {
            get => _stopTime;
            set
            {
                if (_stopTime != value)
                {
                    _stopTime = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineInfo));
                }
            }
        }

        public string? IssueReason
        {
            get => _issueReason;
            set
            {
                if (_issueReason != value)
                {
                    _issueReason = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineInfo));
                }
            }
        }

        public bool IsLocomotiveHs
        {
            get => _isLocomotiveHs;
            set
            {
                if (_isLocomotiveHs != value)
                {
                    _isLocomotiveHs = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineInfo));
                }
            }
        }

        public string? LeftLabel
        {
            get => _leftLabel;
            set
            {
                if (_leftLabel != value)
                {
                    _leftLabel = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? RightLabel
        {
            get => _rightLabel;
            set
            {
                if (_rightLabel != value)
                {
                    _rightLabel = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLeftBlocked
        {
            get => _isLeftBlocked;
            set
            {
                if (_isLeftBlocked != value)
                {
                    _isLeftBlocked = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRightBlocked
        {
            get => _isRightBlocked;
            set
            {
                if (_isRightBlocked != value)
                {
                    _isRightBlocked = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? TrainNumber
        {
            get => _trainNumber;
            set
            {
                if (_trainNumber != value)
                {
                    _trainNumber = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LineInfo));
                }
            }
        }

        public string LineInfo
        {
            get
            {
                var status = IsLocomotiveHs ? "HS" : "OK";
                var reason = string.IsNullOrWhiteSpace(IssueReason) ? "Raison non précisée" : IssueReason;

                if (!IsOnTrain)
                {
                    return $"Locomotive isolée · {reason} · Loco {status}";
                }

                var time = string.IsNullOrWhiteSpace(StopTime) ? "Heure inconnue" : StopTime;
                var train = string.IsNullOrWhiteSpace(TrainNumber) ? "Train non précisé" : $"Train {TrainNumber}";
                return $"{train} · Arrêt {time} · {reason} · Loco {status}";
            }
        }

        public ObservableCollection<LocomotiveModel> Locomotives { get; } = new ObservableCollection<LocomotiveModel>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class TileModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private double _x;
        private double _y;
        private double _width = 360;
        private double _height = 220;
        private readonly ObservableCollection<TrackModel> _outputTracks = new();
        private readonly ObservableCollection<TrackModel> _zoneTracks = new();
        private readonly ObservableCollection<TrackModel> _lineTracks = new();
        private readonly ObservableCollection<TrackModel> _rollingLineTracks = new();

        private static int _nextTempId = -1;
        public int Id { get; set; } = System.Threading.Interlocked.Decrement(ref _nextTempId);
        private TileType _type;

        public TileType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string? LocationPreset { get; set; }
        public int? GarageTrackNumber { get; set; }
        private int? _rollingLineCount;

        public int? RollingLineCount
        {
            get => _rollingLineCount;
            set
            {
                if (_rollingLineCount != value)
                {
                    _rollingLineCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public double X
        {
            get => _x;
            set
            {
                if (_x != value)
                {
                    _x = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (_y != value)
                {
                    _y = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (Math.Abs(_width - value) > 0.1)
                {
                    _width = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) > 0.1)
                {
                    _height = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<TrackModel> Tracks { get; } = new ObservableCollection<TrackModel>();
        public ObservableCollection<TrackModel> OutputTracks => _outputTracks;
        public ObservableCollection<TrackModel> ZoneTracks => _zoneTracks;
        public ObservableCollection<TrackModel> LineTracks => _lineTracks;
        public ObservableCollection<TrackModel> RollingLineTracks => _rollingLineTracks;

        public string DisplayTitle
        {
            get
            {
                return Type switch
                {
                    TileType.Depot => $"Dépôt {Name}",
                    _ => Name
                };
            }
        }

        public TrackModel? MainTrack => Tracks.FirstOrDefault(track => track.Kind == TrackKind.Main);

        public void RefreshTrackCollections()
        {
            _outputTracks.Clear();
            _zoneTracks.Clear();
            _lineTracks.Clear();
            _rollingLineTracks.Clear();

            foreach (var track in Tracks)
            {
                switch (track.Kind)
                {
                    case TrackKind.Output:
                        _outputTracks.Add(track);
                        break;
                    case TrackKind.Zone:
                        _zoneTracks.Add(track);
                        break;
                    case TrackKind.Line:
                        _lineTracks.Add(track);
                        break;
                    case TrackKind.RollingLine:
                        _rollingLineTracks.Add(track);
                        break;
                }
            }

            OnPropertyChanged(nameof(MainTrack));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
