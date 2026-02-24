using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Ploco.Models;

namespace Ploco.Data
{
    public class PlocoRepository : IPlocoRepository
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public PlocoRepository(string databasePath)
        {
            _databasePath = databasePath;
            _connectionString = $"Data Source={databasePath}";
        }

        public void Initialize()
        {
            EnsureDatabaseFile();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var commands = new[]
            {
                @"CREATE TABLE IF NOT EXISTS series (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    start_number INTEGER NOT NULL,
                    end_number INTEGER NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS locomotives (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    series_id INTEGER NOT NULL,
                    number INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    pool TEXT NOT NULL DEFAULT 'Lineas',
                    traction_percent INTEGER,
                    hs_reason TEXT,
                    maintenance_date TEXT,
                    FOREIGN KEY(series_id) REFERENCES series(id)
                );",
                @"CREATE TABLE IF NOT EXISTS tiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    type TEXT NOT NULL,
                    x REAL NOT NULL,
                    y REAL NOT NULL,
                    config_json TEXT
                );",
                @"CREATE TABLE IF NOT EXISTS tracks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    tile_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    position INTEGER NOT NULL,
                    type TEXT NOT NULL DEFAULT 'Main',
                    config_json TEXT,
                    FOREIGN KEY(tile_id) REFERENCES tiles(id)
                );",
                @"CREATE TABLE IF NOT EXISTS track_locomotives (
                    track_id INTEGER NOT NULL,
                    loco_id INTEGER NOT NULL,
                    position INTEGER NOT NULL,
                    offset_x REAL,
                    PRIMARY KEY(track_id, loco_id),
                    FOREIGN KEY(track_id) REFERENCES tracks(id),
                    FOREIGN KEY(loco_id) REFERENCES locomotives(id)
                );",
                @"CREATE TABLE IF NOT EXISTS history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    action TEXT NOT NULL,
                    details TEXT NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS places (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    type TEXT NOT NULL,
                    name TEXT NOT NULL,
                    UNIQUE(type, name)
                );",
                @"CREATE TABLE IF NOT EXISTS pdf_documents (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_path TEXT NOT NULL,
                    document_date TEXT NOT NULL,
                    template_hash TEXT NOT NULL,
                    page_count INTEGER NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS pdf_template_calibrations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    template_hash TEXT NOT NULL,
                    page_index INTEGER NOT NULL,
                    x_start REAL NOT NULL,
                    x_end REAL NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS pdf_template_rows (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    calibration_id INTEGER NOT NULL,
                    roulement_id TEXT NOT NULL,
                    y_center REAL NOT NULL,
                    FOREIGN KEY(calibration_id) REFERENCES pdf_template_calibrations(id)
                );",
                @"CREATE TABLE IF NOT EXISTS pdf_calibration_lines (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    calibration_id INTEGER NOT NULL,
                    type TEXT NOT NULL,
                    position REAL NOT NULL,
                    label TEXT NOT NULL,
                    minute_of_day INTEGER,
                    FOREIGN KEY(calibration_id) REFERENCES pdf_template_calibrations(id)
                );",
                @"CREATE TABLE IF NOT EXISTS pdf_placements (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    pdf_document_id INTEGER NOT NULL,
                    page_index INTEGER NOT NULL,
                    roulement_id TEXT NOT NULL,
                    minute_of_day INTEGER NOT NULL,
                    loc_number INTEGER NOT NULL,
                    status TEXT NOT NULL,
                    traction_percent INTEGER,
                    motors_hs_count INTEGER,
                    hs_reason TEXT,
                    on_train INTEGER NOT NULL,
                    train_number TEXT,
                    train_stop_time TEXT,
                    comment TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(pdf_document_id) REFERENCES pdf_documents(id)
                );"
            };

            foreach (var sql in commands)
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            EnsureColumn(connection, "locomotives", "pool", "TEXT NOT NULL DEFAULT 'Lineas'");
            EnsureColumn(connection, "locomotives", "traction_percent", "INTEGER");
            EnsureColumn(connection, "locomotives", "hs_reason", "TEXT");
            EnsureColumn(connection, "locomotives", "defaut_info", "TEXT");
            EnsureColumn(connection, "locomotives", "traction_info", "TEXT");
            EnsureColumn(connection, "locomotives", "maintenance_date", "TEXT");
            EnsureColumn(connection, "locomotives", "is_forecast_origin", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "locomotives", "is_forecast_ghost", "INTEGER DEFAULT 0");
            EnsureColumn(connection, "tracks", "type", "TEXT NOT NULL DEFAULT 'Main'");
            EnsureColumn(connection, "tracks", "config_json", "TEXT");
            EnsureColumn(connection, "track_locomotives", "offset_x", "REAL");
        }

        public PdfDocumentModel? GetPdfDocument(string filePath, DateTime date)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, file_path, document_date, template_hash, page_count
                                    FROM pdf_documents
                                    WHERE file_path = $path AND document_date = $date;";
            command.Parameters.AddWithValue("$path", filePath);
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new PdfDocumentModel
            {
                Id = reader.GetInt32(0),
                FilePath = reader.GetString(1),
                DocumentDate = DateTime.Parse(reader.GetString(2)),
                TemplateHash = reader.GetString(3),
                PageCount = reader.GetInt32(4)
            };
        }

        public PdfDocumentModel SavePdfDocument(PdfDocumentModel document)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            if (document.Id > 0)
            {
                command.CommandText = @"UPDATE pdf_documents
                                        SET file_path = $path,
                                            document_date = $date,
                                            template_hash = $hash,
                                            page_count = $count
                                        WHERE id = $id;";
                command.Parameters.AddWithValue("$id", document.Id);
            }
            else
            {
                command.CommandText = @"INSERT INTO pdf_documents (file_path, document_date, template_hash, page_count)
                                        VALUES ($path, $date, $hash, $count);";
            }

            command.Parameters.AddWithValue("$path", document.FilePath);
            command.Parameters.AddWithValue("$date", document.DocumentDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$hash", document.TemplateHash);
            command.Parameters.AddWithValue("$count", document.PageCount);
            command.ExecuteNonQuery();

            if (document.Id == 0)
            {
                document.Id = GetLastInsertRowId(connection);
            }

            return document;
        }

        public List<PdfTemplateCalibrationModel> LoadTemplateCalibrations(string templateHash)
        {
            var calibrations = new List<PdfTemplateCalibrationModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT id, template_hash, page_index, x_start, x_end
                                        FROM pdf_template_calibrations
                                        WHERE template_hash = $hash;";
                command.Parameters.AddWithValue("$hash", templateHash);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    calibrations.Add(new PdfTemplateCalibrationModel
                    {
                        Id = reader.GetInt32(0),
                        TemplateHash = reader.GetString(1),
                        PageIndex = reader.GetInt32(2),
                        XStart = reader.GetDouble(3),
                        XEnd = reader.GetDouble(4)
                    });
                }
            }

            foreach (var calibration in calibrations)
            {
                calibration.Rows = LoadTemplateRows(connection, calibration.Id);
                calibration.VisualLines = LoadCalibrationLines(connection, calibration.Id);
            }

            return calibrations;
        }

        public void SaveTemplateCalibration(PdfTemplateCalibrationModel calibration)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                if (calibration.Id > 0)
                {
                    command.CommandText = @"UPDATE pdf_template_calibrations
                                            SET x_start = $xStart, x_end = $xEnd
                                            WHERE id = $id;";
                    command.Parameters.AddWithValue("$id", calibration.Id);
                }
                else
                {
                    command.CommandText = @"INSERT INTO pdf_template_calibrations (template_hash, page_index, x_start, x_end)
                                            VALUES ($hash, $pageIndex, $xStart, $xEnd);";
                    command.Parameters.AddWithValue("$hash", calibration.TemplateHash);
                    command.Parameters.AddWithValue("$pageIndex", calibration.PageIndex);
                }

                command.Parameters.AddWithValue("$xStart", calibration.XStart);
                command.Parameters.AddWithValue("$xEnd", calibration.XEnd);
                command.ExecuteNonQuery();

                if (calibration.Id == 0)
                {
                    calibration.Id = GetLastInsertRowId(connection);
                }
            }

            using (var deleteRows = connection.CreateCommand())
            {
                deleteRows.CommandText = "DELETE FROM pdf_template_rows WHERE calibration_id = $id;";
                deleteRows.Parameters.AddWithValue("$id", calibration.Id);
                deleteRows.ExecuteNonQuery();
            }

            foreach (var row in calibration.Rows)
            {
                using var insertRow = connection.CreateCommand();
                insertRow.CommandText = @"INSERT INTO pdf_template_rows (calibration_id, roulement_id, y_center)
                                          VALUES ($calibrationId, $roulementId, $yCenter);";
                insertRow.Parameters.AddWithValue("$calibrationId", calibration.Id);
                insertRow.Parameters.AddWithValue("$roulementId", row.RoulementId);
                insertRow.Parameters.AddWithValue("$yCenter", row.YCenter);
                insertRow.ExecuteNonQuery();
            }

            // Sauvegarder les lignes visuelles
            using (var deleteLines = connection.CreateCommand())
            {
                deleteLines.CommandText = "DELETE FROM pdf_calibration_lines WHERE calibration_id = $id;";
                deleteLines.Parameters.AddWithValue("$id", calibration.Id);
                deleteLines.ExecuteNonQuery();
            }

            foreach (var line in calibration.VisualLines)
            {
                using var insertLine = connection.CreateCommand();
                insertLine.CommandText = @"INSERT INTO pdf_calibration_lines (calibration_id, type, position, label, minute_of_day)
                                          VALUES ($calibrationId, $type, $position, $label, $minuteOfDay);";
                insertLine.Parameters.AddWithValue("$calibrationId", calibration.Id);
                insertLine.Parameters.AddWithValue("$type", line.Type.ToString());
                insertLine.Parameters.AddWithValue("$position", line.Position);
                insertLine.Parameters.AddWithValue("$label", line.Label);
                insertLine.Parameters.AddWithValue("$minuteOfDay", line.MinuteOfDay.HasValue ? (object)line.MinuteOfDay.Value : DBNull.Value);
                insertLine.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public List<PdfPlacementModel> LoadPlacements(int pdfDocumentId)
        {
            var placements = new List<PdfPlacementModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, pdf_document_id, page_index, roulement_id, minute_of_day,
                                           loc_number, status, traction_percent, motors_hs_count, hs_reason,
                                           on_train, train_number, train_stop_time, comment, created_at, updated_at
                                    FROM pdf_placements
                                    WHERE pdf_document_id = $docId;";
            command.Parameters.AddWithValue("$docId", pdfDocumentId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                placements.Add(new PdfPlacementModel
                {
                    Id = reader.GetInt32(0),
                    PdfDocumentId = reader.GetInt32(1),
                    PageIndex = reader.GetInt32(2),
                    RoulementId = reader.GetString(3),
                    MinuteOfDay = reader.GetInt32(4),
                    LocNumber = reader.GetInt32(5),
                    Status = Enum.TryParse(reader.GetString(6), out LocomotiveStatus status) ? status : LocomotiveStatus.Ok,
                    TractionPercent = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    MotorsHsCount = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    HsReason = reader.IsDBNull(9) ? null : reader.GetString(9),
                    OnTrain = reader.GetInt32(10) == 1,
                    TrainNumber = reader.IsDBNull(11) ? null : reader.GetString(11),
                    TrainStopTime = reader.IsDBNull(12) ? null : reader.GetString(12),
                    Comment = reader.IsDBNull(13) ? null : reader.GetString(13),
                    CreatedAt = DateTime.Parse(reader.GetString(14)),
                    UpdatedAt = DateTime.Parse(reader.GetString(15))
                });
            }

            return placements;
        }

        public void SavePlacement(PdfPlacementModel placement)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            if (placement.Id > 0)
            {
                command.CommandText = @"UPDATE pdf_placements
                                        SET page_index = $pageIndex,
                                            roulement_id = $roulementId,
                                            minute_of_day = $minute,
                                            loc_number = $locNumber,
                                            status = $status,
                                            traction_percent = $traction,
                                            motors_hs_count = $motorsHs,
                                            hs_reason = $hsReason,
                                            on_train = $onTrain,
                                            train_number = $trainNumber,
                                            train_stop_time = $trainStopTime,
                                            comment = $comment,
                                            updated_at = $updatedAt
                                        WHERE id = $id;";
                command.Parameters.AddWithValue("$id", placement.Id);
            }
            else
            {
                command.CommandText = @"INSERT INTO pdf_placements (pdf_document_id, page_index, roulement_id, minute_of_day, loc_number,
                                            status, traction_percent, motors_hs_count, hs_reason, on_train, train_number, train_stop_time,
                                            comment, created_at, updated_at)
                                        VALUES ($docId, $pageIndex, $roulementId, $minute, $locNumber,
                                            $status, $traction, $motorsHs, $hsReason, $onTrain, $trainNumber, $trainStopTime,
                                            $comment, $createdAt, $updatedAt);";
                command.Parameters.AddWithValue("$docId", placement.PdfDocumentId);
                placement.CreatedAt = placement.CreatedAt == default ? DateTime.UtcNow : placement.CreatedAt;
                command.Parameters.AddWithValue("$createdAt", placement.CreatedAt.ToString("O"));
            }

            placement.UpdatedAt = DateTime.UtcNow;
            command.Parameters.AddWithValue("$pageIndex", placement.PageIndex);
            command.Parameters.AddWithValue("$roulementId", placement.RoulementId);
            command.Parameters.AddWithValue("$minute", placement.MinuteOfDay);
            command.Parameters.AddWithValue("$locNumber", placement.LocNumber);
            command.Parameters.AddWithValue("$status", placement.Status.ToString());
            command.Parameters.AddWithValue("$traction", (object?)placement.TractionPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("$motorsHs", (object?)placement.MotorsHsCount ?? DBNull.Value);
            command.Parameters.AddWithValue("$hsReason", string.IsNullOrWhiteSpace(placement.HsReason) ? DBNull.Value : placement.HsReason);
            command.Parameters.AddWithValue("$onTrain", placement.OnTrain ? 1 : 0);
            command.Parameters.AddWithValue("$trainNumber", string.IsNullOrWhiteSpace(placement.TrainNumber) ? DBNull.Value : placement.TrainNumber);
            command.Parameters.AddWithValue("$trainStopTime", string.IsNullOrWhiteSpace(placement.TrainStopTime) ? DBNull.Value : placement.TrainStopTime);
            command.Parameters.AddWithValue("$comment", string.IsNullOrWhiteSpace(placement.Comment) ? DBNull.Value : placement.Comment);
            command.Parameters.AddWithValue("$updatedAt", placement.UpdatedAt.ToString("O"));
            command.ExecuteNonQuery();

            if (placement.Id == 0)
            {
                placement.Id = GetLastInsertRowId(connection);
            }
        }

        public void DeletePlacement(int placementId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pdf_placements WHERE id = $id;";
            command.Parameters.AddWithValue("$id", placementId);
            command.ExecuteNonQuery();
        }

        private static List<PdfTemplateRowMapping> LoadTemplateRows(SqliteConnection connection, int calibrationId)
        {
            var rows = new List<PdfTemplateRowMapping>();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, calibration_id, roulement_id, y_center
                                    FROM pdf_template_rows
                                    WHERE calibration_id = $id;";
            command.Parameters.AddWithValue("$id", calibrationId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new PdfTemplateRowMapping
                {
                    Id = reader.GetInt32(0),
                    CalibrationId = reader.GetInt32(1),
                    RoulementId = reader.GetString(2),
                    YCenter = reader.GetDouble(3)
                });
            }
            return rows;
        }

        private static List<PdfCalibrationLine> LoadCalibrationLines(SqliteConnection connection, int calibrationId)
        {
            var lines = new List<PdfCalibrationLine>();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, calibration_id, type, position, label, minute_of_day
                                    FROM pdf_calibration_lines
                                    WHERE calibration_id = $id;";
            command.Parameters.AddWithValue("$id", calibrationId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                lines.Add(new PdfCalibrationLine
                {
                    Id = reader.GetInt32(0),
                    CalibrationId = reader.GetInt32(1),
                    Type = Enum.Parse<CalibrationLineType>(reader.GetString(2)),
                    Position = reader.GetDouble(3),
                    Label = reader.GetString(4),
                    MinuteOfDay = reader.IsDBNull(5) ? null : reader.GetInt32(5)
                });
            }
            return lines;
        }

        public AppState LoadState()
        {
            var state = new AppState();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var series = new Dictionary<int, RollingStockSeries>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, start_number, end_number FROM series;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var item = new RollingStockSeries
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        StartNumber = reader.GetInt32(2),
                        EndNumber = reader.GetInt32(3)
                    };
                    series[item.Id] = item;
                    state.Series.Add(item);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, series_id, number, status, pool, traction_percent, hs_reason, defaut_info, traction_info, maintenance_date, is_forecast_origin, is_forecast_ghost FROM locomotives;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var seriesId = reader.GetInt32(1);
                    var seriesName = series.TryGetValue(seriesId, out var serie) ? serie.Name : "Serie";
                    var statusValue = reader.GetString(3);
                    var status = ParseLocomotiveStatus(statusValue);
                    state.Locomotives.Add(new LocomotiveModel
                    {
                        Id = reader.GetInt32(0),
                        SeriesId = seriesId,
                        SeriesName = seriesName,
                        Number = reader.GetInt32(2),
                        Status = status,
                        Pool = reader.IsDBNull(4) ? "Lineas" : reader.GetString(4),
                        TractionPercent = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        HsReason = reader.IsDBNull(6) ? null : reader.GetString(6),
                        DefautInfo = reader.IsDBNull(7) ? null : reader.GetString(7),
                        TractionInfo = reader.IsDBNull(8) ? null : reader.GetString(8),
                        MaintenanceDate = reader.IsDBNull(9) ? null : reader.GetString(9),
                        IsForecastOrigin = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
                        IsForecastGhost = !reader.IsDBNull(11) && reader.GetInt32(11) == 1
                    });
                }
            }

            var tiles = new Dictionary<int, TileModel>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, type, x, y, config_json FROM tiles;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var tile = new TileModel
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Type = Enum.Parse<TileType>(reader.GetString(2)),
                        X = reader.GetDouble(3),
                        Y = reader.GetDouble(4)
                    };

                    var configJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                    if (!string.IsNullOrWhiteSpace(configJson))
                    {
                        var config = JsonSerializer.Deserialize<TileConfig>(configJson);
                        if (config != null)
                        {
                            tile.LocationPreset = config.LocationPreset;
                            tile.GarageTrackNumber = config.GarageTrackNumber;
                            tile.RollingLineCount = config.RollingLineCount;
                            if (config.Width.HasValue)
                            {
                                tile.Width = config.Width.Value;
                            }
                            if (config.Height.HasValue)
                            {
                                tile.Height = config.Height.Value;
                            }
                        }
                    }

                    tiles[tile.Id] = tile;
                    state.Tiles.Add(tile);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, tile_id, name, position, type, config_json FROM tracks ORDER BY position;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var track = new TrackModel
                    {
                        Id = reader.GetInt32(0),
                        TileId = reader.GetInt32(1),
                        Name = reader.GetString(2),
                        Position = reader.GetInt32(3),
                        Kind = Enum.TryParse(reader.GetString(4), out TrackKind kind) ? kind : TrackKind.Main
                    };
                    var configJson = reader.IsDBNull(5) ? null : reader.GetString(5);
                    if (!string.IsNullOrWhiteSpace(configJson))
                    {
                        var config = JsonSerializer.Deserialize<TrackConfig>(configJson);
                        if (config != null)
                        {
                            track.IsOnTrain = config.IsOnTrain;
                            track.TrainNumber = config.TrainNumber;
                            track.StopTime = config.StopTime;
                            track.IssueReason = config.IssueReason;
                            track.IsLocomotiveHs = config.IsLocomotiveHs;
                            track.LeftLabel = config.LeftLabel;
                            track.RightLabel = config.RightLabel;
                            track.IsLeftBlocked = config.IsLeftBlocked;
                            track.IsRightBlocked = config.IsRightBlocked;
                        }
                    }
                    if (tiles.TryGetValue(track.TileId, out var tile))
                    {
                        tile.Tracks.Add(track);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT track_id, loco_id, position, offset_x FROM track_locomotives ORDER BY position;";
                using var reader = command.ExecuteReader();
                var locosById = state.Locomotives.ToDictionary(l => l.Id);
                var tracksById = state.Tiles.SelectMany(t => t.Tracks).ToDictionary(t => t.Id);

                while (reader.Read())
                {
                    var trackId = reader.GetInt32(0);
                    var locoId = reader.GetInt32(1);
                    if (tracksById.TryGetValue(trackId, out var track) && locosById.TryGetValue(locoId, out var loco))
                    {
                        track.Locomotives.Add(loco);
                        loco.AssignedTrackId = trackId;
                        var offset = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3);
                        loco.AssignedTrackOffsetX = track.Kind == TrackKind.Line || track.Kind == TrackKind.Zone || track.Kind == TrackKind.Output
                            ? offset
                            : null;
                    }
                }
            }

            foreach (var tile in state.Tiles)
            {
                tile.RefreshTrackCollections();
            }
            return state;
        }

        public void SeedDefaultDataIfNeeded()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM series;";
                var count = (long)command.ExecuteScalar()!;
                if (count > 0)
                {
                    return;
                }
            }

            using var transaction = connection.BeginTransaction();
            var seriesId = InsertSeries(connection, "1300", 1301, 1349);
            InsertSeries(connection, "37000", 37001, 37040);

            using (var insertLoco = connection.CreateCommand())
            {
                insertLoco.CommandText = "INSERT INTO locomotives (series_id, number, status, pool, is_forecast_origin, is_forecast_ghost) VALUES ($seriesId, $number, $status, $pool, 0, 0);";
                var seriesParam = insertLoco.CreateParameter();
                seriesParam.ParameterName = "$seriesId";
                insertLoco.Parameters.Add(seriesParam);
                var numberParam = insertLoco.CreateParameter();
                numberParam.ParameterName = "$number";
                insertLoco.Parameters.Add(numberParam);
                var statusParam = insertLoco.CreateParameter();
                statusParam.ParameterName = "$status";
                insertLoco.Parameters.Add(statusParam);
                var poolParam = insertLoco.CreateParameter();
                poolParam.ParameterName = "$pool";
                insertLoco.Parameters.Add(poolParam);

                for (var number = 1301; number <= 1349; number++)
                {
                    seriesParam.Value = seriesId;
                    numberParam.Value = number;
                    statusParam.Value = LocomotiveStatus.Ok.ToString();
                    poolParam.Value = "Lineas";
                    insertLoco.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        public void SaveState(AppState state)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, "DELETE FROM track_locomotives;");
            ExecuteNonQuery(connection, "DELETE FROM tracks;");
            ExecuteNonQuery(connection, "DELETE FROM tiles;");
            ExecuteNonQuery(connection, "DELETE FROM locomotives;");
            ExecuteNonQuery(connection, "DELETE FROM series;");

            var seriesIdMap = new Dictionary<int, int>();
            foreach (var series in state.Series)
            {
                var newId = InsertSeries(connection, series.Name, series.StartNumber, series.EndNumber);
                seriesIdMap[series.Id] = newId;
                series.Id = newId;
            }

            foreach (var loco in state.Locomotives)
            {
                if (seriesIdMap.TryGetValue(loco.SeriesId, out var newSeriesId))
                {
                    loco.SeriesId = newSeriesId;
                }
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO locomotives (series_id, number, status, pool, traction_percent, hs_reason, defaut_info, traction_info, maintenance_date, is_forecast_origin, is_forecast_ghost) VALUES ($seriesId, $number, $status, $pool, $traction, $reason, $defaut, $tractionInfo, $maintenance, $isOrigin, $isGhost);";
                command.Parameters.AddWithValue("$seriesId", loco.SeriesId);
                command.Parameters.AddWithValue("$number", loco.Number);
                command.Parameters.AddWithValue("$status", loco.Status.ToString());
                command.Parameters.AddWithValue("$pool", loco.Pool);
                command.Parameters.AddWithValue("$traction", (object?)loco.TractionPercent ?? DBNull.Value);
                command.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(loco.HsReason) ? DBNull.Value : loco.HsReason);
                command.Parameters.AddWithValue("$defaut", string.IsNullOrWhiteSpace(loco.DefautInfo) ? DBNull.Value : loco.DefautInfo);
                command.Parameters.AddWithValue("$tractionInfo", string.IsNullOrWhiteSpace(loco.TractionInfo) ? DBNull.Value : loco.TractionInfo);
                command.Parameters.AddWithValue("$maintenance", string.IsNullOrWhiteSpace(loco.MaintenanceDate) ? DBNull.Value : loco.MaintenanceDate);
                command.Parameters.AddWithValue("$isOrigin", loco.IsForecastOrigin ? 1 : 0);
                command.Parameters.AddWithValue("$isGhost", loco.IsForecastGhost ? 1 : 0);
                command.ExecuteNonQuery();
                loco.Id = GetLastInsertRowId(connection);
            }

            foreach (var tile in state.Tiles)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO tiles (name, type, x, y, config_json) VALUES ($name, $type, $x, $y, $config);";
                command.Parameters.AddWithValue("$name", tile.Name);
                command.Parameters.AddWithValue("$type", tile.Type.ToString());
                command.Parameters.AddWithValue("$x", tile.X);
                command.Parameters.AddWithValue("$y", tile.Y);
                var configJson = JsonSerializer.Serialize(new TileConfig
                {
                    LocationPreset = tile.LocationPreset,
                    GarageTrackNumber = tile.GarageTrackNumber,
                    RollingLineCount = tile.RollingLineCount,
                    Width = tile.Width,
                    Height = tile.Height
                });
                command.Parameters.AddWithValue("$config", configJson);
                command.ExecuteNonQuery();
                tile.Id = GetLastInsertRowId(connection);

                var trackPosition = 0;
                foreach (var track in tile.Tracks)
                {
                    using var trackCommand = connection.CreateCommand();
                    trackCommand.CommandText = "INSERT INTO tracks (tile_id, name, position, type, config_json) VALUES ($tileId, $name, $position, $type, $config);";
                    trackCommand.Parameters.AddWithValue("$tileId", tile.Id);
                    trackCommand.Parameters.AddWithValue("$name", track.Name);
                    trackCommand.Parameters.AddWithValue("$position", trackPosition++);
                    trackCommand.Parameters.AddWithValue("$type", track.Kind.ToString());
                    var trackConfigJson = JsonSerializer.Serialize(new TrackConfig
                    {
                        IsOnTrain = track.IsOnTrain,
                        TrainNumber = track.TrainNumber,
                        StopTime = track.StopTime,
                        IssueReason = track.IssueReason,
                        IsLocomotiveHs = track.IsLocomotiveHs,
                        LeftLabel = track.LeftLabel,
                        RightLabel = track.RightLabel,
                        IsLeftBlocked = track.IsLeftBlocked,
                        IsRightBlocked = track.IsRightBlocked
                    });
                    object configValue = track.Kind == TrackKind.Line
                        || !string.IsNullOrWhiteSpace(track.TrainNumber)
                        || !string.IsNullOrWhiteSpace(track.LeftLabel)
                        || !string.IsNullOrWhiteSpace(track.RightLabel)
                        || track.IsLeftBlocked
                        || track.IsRightBlocked
                        ? trackConfigJson
                        : DBNull.Value;
                    trackCommand.Parameters.AddWithValue("$config", configValue);
                    trackCommand.ExecuteNonQuery();
                    track.Id = GetLastInsertRowId(connection);

                    var locoPosition = 0;
                    foreach (var loco in track.Locomotives)
                    {
                        using var assignCommand = connection.CreateCommand();
                        assignCommand.CommandText = "INSERT INTO track_locomotives (track_id, loco_id, position, offset_x) VALUES ($trackId, $locoId, $position, $offsetX);";
                        assignCommand.Parameters.AddWithValue("$trackId", track.Id);
                        assignCommand.Parameters.AddWithValue("$locoId", loco.Id);
                        assignCommand.Parameters.AddWithValue("$position", locoPosition++);
                        object offsetValue = track.Kind == TrackKind.Line || track.Kind == TrackKind.Zone || track.Kind == TrackKind.Output
                            ? (object?)loco.AssignedTrackOffsetX ?? DBNull.Value
                            : DBNull.Value;
                        assignCommand.Parameters.AddWithValue("$offsetX", offsetValue);
                        assignCommand.ExecuteNonQuery();
                    }
                }
            }

            SavePlaces(connection, state.Tiles);
            transaction.Commit();
        }

        public void AddHistory(string action, string details)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO history (timestamp, action, details) VALUES ($timestamp, $action, $details);";
            command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$details", details);
            command.ExecuteNonQuery();
        }

        private static int InsertSeries(SqliteConnection connection, string name, int startNumber, int endNumber)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO series (name, start_number, end_number) VALUES ($name, $start, $end);";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$start", startNumber);
            command.Parameters.AddWithValue("$end", endNumber);
            command.ExecuteNonQuery();
            return GetLastInsertRowId(connection);
        }

        private static LocomotiveStatus ParseLocomotiveStatus(string value)
        {
            if (Enum.TryParse(value, out LocomotiveStatus status))
            {
                return status;
            }

            if (Enum.TryParse(value, out StatutLocomotive legacy))
            {
                return legacy switch
                {
                    StatutLocomotive.HS => LocomotiveStatus.HS,
                    StatutLocomotive.DefautMineur => LocomotiveStatus.ManqueTraction,
                    StatutLocomotive.AControler => LocomotiveStatus.ManqueTraction,
                    _ => LocomotiveStatus.Ok
                };
            }

            return LocomotiveStatus.Ok;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public List<HistoryEntry> LoadHistory()
        {
            var history = new List<HistoryEntry>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT timestamp, action, details FROM history ORDER BY timestamp DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                history.Add(new HistoryEntry
                {
                    Timestamp = DateTime.Parse(reader.GetString(0)),
                    Action = reader.GetString(1),
                    Details = reader.GetString(2)
                });
            }

            return history;
        }

        public Dictionary<string, int> GetTableCounts()
        {
            var tables = new[]
            {
                "series",
                "locomotives",
                "tiles",
                "tracks",
                "track_locomotives",
                "history",
                "places"
            };

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            foreach (var table in tables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM {table};";
                var count = Convert.ToInt32(command.ExecuteScalar());
                result[table] = count;
            }

            return result;
        }

        public Dictionary<TrackKind, int> GetTrackKindCounts()
        {
            var result = new Dictionary<TrackKind, int>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT type, COUNT(*) FROM tracks GROUP BY type;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kindText = reader.GetString(0);
                if (Enum.TryParse(kindText, out TrackKind kind))
                {
                    result[kind] = reader.GetInt32(1);
                }
            }

            return result;
        }

        public void ClearHistory()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            ExecuteNonQuery(connection, "DELETE FROM history;");
        }

        public void ResetOperationalState()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE locomotives SET traction_percent = NULL, hs_reason = NULL;";
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE tracks SET config_json = NULL WHERE type = 'Line';";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public void CopyDatabaseTo(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            System.IO.File.Copy(_databasePath, destinationPath, true);
        }

        public bool ReplaceDatabaseWith(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            if (!IsSqliteDatabase(sourcePath))
            {
                return false;
            }

            // Force SQLite logic to release any file locks from connection pools
            SqliteConnection.ClearAllPools();
            File.Copy(sourcePath, _databasePath, true);
            return true;
        }

        private static int GetLastInsertRowId(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private void EnsureDatabaseFile()
        {
            if (!File.Exists(_databasePath))
            {
                return;
            }

            if (IsSqliteDatabase(_databasePath))
            {
                return;
            }

            File.Delete(_databasePath);
        }

        private static bool IsSqliteDatabase(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var header = new byte[16];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Read(header, 0, header.Length) < header.Length)
            {
                return false;
            }

            var headerText = Encoding.ASCII.GetString(header);
            return headerText.StartsWith("SQLite format 3");
        }

        private static void SavePlaces(SqliteConnection connection, IEnumerable<TileModel> tiles)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO places (type, name) VALUES ($type, $name);";
            var typeParameter = command.CreateParameter();
            typeParameter.ParameterName = "$type";
            command.Parameters.Add(typeParameter);
            var nameParameter = command.CreateParameter();
            nameParameter.ParameterName = "$name";
            command.Parameters.Add(nameParameter);

            foreach (var tile in tiles)
            {
                typeParameter.Value = tile.Type.ToString();
                nameParameter.Value = tile.Name;
                command.ExecuteNonQuery();
            }
        }

        private class TileConfig
        {
            public string? LocationPreset { get; set; }
            public int? GarageTrackNumber { get; set; }
            public int? RollingLineCount { get; set; }
            public double? Width { get; set; }
            public double? Height { get; set; }
        }

        private class TrackConfig
        {
            public bool IsOnTrain { get; set; }
            public string? TrainNumber { get; set; }
            public string? StopTime { get; set; }
            public string? IssueReason { get; set; }
            public bool IsLocomotiveHs { get; set; }
            public string? LeftLabel { get; set; }
            public string? RightLabel { get; set; }
            public bool IsLeftBlocked { get; set; }
            public bool IsRightBlocked { get; set; }
        }
    }
}
