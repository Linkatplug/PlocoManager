using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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

        public async Task InitializeAsync()
        {
            EnsureDatabaseFile();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

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

        public async Task<PdfDocumentModel?> GetPdfDocumentAsync(string filePath, DateTime date)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, file_path, document_date, template_hash, page_count
                                    FROM pdf_documents
                                    WHERE file_path = $path AND document_date = $date;";
            command.Parameters.AddWithValue("$path", filePath);
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
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

        public async Task<PdfDocumentModel> SavePdfDocumentAsync(PdfDocumentModel document)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();

            if (document.Id > 0)
            {
                command.CommandText = @"UPDATE pdf_documents
                                        SET file_path = $path, document_date = $date, template_hash = $hash, page_count = $count
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
            await command.ExecuteNonQueryAsync();

            if (document.Id == 0)
            {
                document.Id = await GetLastInsertRowIdAsync(connection);
            }

            return document;
        }

        public async Task<List<PdfTemplateCalibrationModel>> LoadTemplateCalibrationsAsync(string templateHash)
        {
            var calibrations = new List<PdfTemplateCalibrationModel>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT id, template_hash, page_index, x_start, x_end
                                        FROM pdf_template_calibrations
                                        WHERE template_hash = $hash;";
                command.Parameters.AddWithValue("$hash", templateHash);
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
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
                calibration.Rows = await LoadTemplateRowsAsync(connection, calibration.Id);
                calibration.VisualLines = await LoadCalibrationLinesAsync(connection, calibration.Id);
            }

            return calibrations;
        }

        public async Task SaveTemplateCalibrationAsync(PdfTemplateCalibrationModel calibration)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
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
                await command.ExecuteNonQueryAsync();

                if (calibration.Id == 0)
                {
                    calibration.Id = await GetLastInsertRowIdAsync(connection);
                }
            }

            using (var deleteRows = connection.CreateCommand())
            {
                deleteRows.CommandText = "DELETE FROM pdf_template_rows WHERE calibration_id = $id;";
                deleteRows.Parameters.AddWithValue("$id", calibration.Id);
                await deleteRows.ExecuteNonQueryAsync();
            }

            foreach (var row in calibration.Rows)
            {
                using var insertRow = connection.CreateCommand();
                insertRow.CommandText = @"INSERT INTO pdf_template_rows (calibration_id, roulement_id, y_center)
                                          VALUES ($calibrationId, $roulementId, $yCenter);";
                insertRow.Parameters.AddWithValue("$calibrationId", calibration.Id);
                insertRow.Parameters.AddWithValue("$roulementId", row.RoulementId);
                insertRow.Parameters.AddWithValue("$yCenter", row.YCenter);
                await insertRow.ExecuteNonQueryAsync();
            }

            // Sauvegarder les lignes visuelles
            using (var deleteLines = connection.CreateCommand())
            {
                deleteLines.CommandText = "DELETE FROM pdf_calibration_lines WHERE calibration_id = $id;";
                deleteLines.Parameters.AddWithValue("$id", calibration.Id);
                await deleteLines.ExecuteNonQueryAsync();
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
                await insertLine.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        public async Task<List<PdfPlacementModel>> LoadPlacementsAsync(int pdfDocumentId)
        {
            var placements = new List<PdfPlacementModel>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, pdf_document_id, page_index, roulement_id, minute_of_day,
                                           loc_number, status, traction_percent, motors_hs_count, hs_reason,
                                           on_train, train_number, train_stop_time, comment, created_at, updated_at
                                    FROM pdf_placements
                                    WHERE pdf_document_id = $docId;";
            command.Parameters.AddWithValue("$docId", pdfDocumentId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
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

        public async Task SavePlacementAsync(PdfPlacementModel placement)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
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
            await command.ExecuteNonQueryAsync();

            if (placement.Id == 0)
            {
                placement.Id = await GetLastInsertRowIdAsync(connection);
            }
        }

        public async Task DeletePlacementAsync(int placementId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pdf_placements WHERE id = $id;";
            command.Parameters.AddWithValue("$id", placementId);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<List<PdfTemplateRowMapping>> LoadTemplateRowsAsync(SqliteConnection connection, int calibrationId)
        {
            var rows = new List<PdfTemplateRowMapping>();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, calibration_id, roulement_id, y_center
                                    FROM pdf_template_rows
                                    WHERE calibration_id = $id;";
            command.Parameters.AddWithValue("$id", calibrationId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
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

        private static async Task<List<PdfCalibrationLine>> LoadCalibrationLinesAsync(SqliteConnection connection, int calibrationId)
        {
            var lines = new List<PdfCalibrationLine>();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT id, calibration_id, type, position, label, minute_of_day
                                    FROM pdf_calibration_lines
                                    WHERE calibration_id = $id;";
            command.Parameters.AddWithValue("$id", calibrationId);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
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

        public async Task<AppState> LoadStateAsync()
        {
            var state = new AppState();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var series = new Dictionary<int, RollingStockSeries>();
                using (var seriesCmd = new SqliteCommand("SELECT id, name, start_number, end_number FROM series;", connection))
                {
                    using var seriesReader = await seriesCmd.ExecuteReaderAsync();
                    while (await seriesReader.ReadAsync())
                    {
                        var item = new RollingStockSeries
                        {
                            Id = seriesReader.GetInt32(0),
                            Name = seriesReader.GetString(1),
                            StartNumber = seriesReader.GetInt32(2),
                            EndNumber = seriesReader.GetInt32(3)
                        };
                        series[item.Id] = item;
                        state.Series.Add(item);
                    }
                }

                using (var locoCmd = new SqliteCommand(@"
                    SELECT l.id, l.series_id, s.name as series_name, l.number, l.status, l.pool, l.traction_percent, l.hs_reason, l.defaut_info, l.traction_info, l.maintenance_date, l.is_forecast_origin, l.is_forecast_ghost
                    FROM locomotives l 
                    JOIN series s ON l.series_id = s.id;", connection))
                {
                    using var locoReader = await locoCmd.ExecuteReaderAsync();
                    while (await locoReader.ReadAsync())
                    {
                        var seriesId = locoReader.GetInt32(1);
                        var seriesName = locoReader.GetString(2); // series_name from join
                        var statusValue = locoReader.GetString(4); // status is at index 4 now
                        var status = ParseLocomotiveStatus(statusValue);
                        state.Locomotives.Add(new LocomotiveModel
                        {
                            Id = locoReader.GetInt32(0),
                            SeriesId = seriesId,
                            SeriesName = seriesName,
                            Number = locoReader.GetInt32(3), // number is at index 3
                            Status = status,
                            Pool = locoReader.IsDBNull(5) ? "Lineas" : locoReader.GetString(5), // pool is at index 5
                            TractionPercent = locoReader.IsDBNull(6) ? null : locoReader.GetInt32(6), // traction_percent is at index 6
                            HsReason = locoReader.IsDBNull(7) ? null : locoReader.GetString(7), // hs_reason is at index 7
                            DefautInfo = locoReader.IsDBNull(8) ? null : locoReader.GetString(8), // defaut_info is at index 8
                            TractionInfo = locoReader.IsDBNull(9) ? null : locoReader.GetString(9), // traction_info is at index 9
                            MaintenanceDate = locoReader.IsDBNull(10) ? null : locoReader.GetString(10), // maintenance_date is at index 10
                            IsForecastOrigin = !locoReader.IsDBNull(11) && locoReader.GetInt32(11) == 1, // is_forecast_origin is at index 11
                            IsForecastGhost = !locoReader.IsDBNull(12) && locoReader.GetInt32(12) == 1 // is_forecast_ghost is at index 12
                        });
                    }
                }

                var tiles = new Dictionary<int, TileModel>();
                using (var tilesCmd = new SqliteCommand("SELECT id, name, type, x, y, config_json FROM tiles;", connection))
                {
                    using var tilesReader = await tilesCmd.ExecuteReaderAsync();
                    while (await tilesReader.ReadAsync())
                    {
                        var tile = new TileModel
                        {
                            Id = tilesReader.GetInt32(0),
                            Name = tilesReader.GetString(1),
                            Type = Enum.Parse<TileType>(tilesReader.GetString(2)),
                            X = tilesReader.GetDouble(3),
                            Y = tilesReader.GetDouble(4)
                        };

                        var configJson = tilesReader.IsDBNull(5) ? null : tilesReader.GetString(5);
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

                using (var tracksCmd = new SqliteCommand("SELECT id, tile_id, name, position, type, config_json FROM tracks ORDER BY position;", connection))
                {
                    using var tracksReader = await tracksCmd.ExecuteReaderAsync();
                    while (await tracksReader.ReadAsync())
                    {
                        var track = new TrackModel
                        {
                            Id = tracksReader.GetInt32(0),
                            TileId = tracksReader.GetInt32(1),
                            Name = tracksReader.GetString(2),
                            Position = tracksReader.GetInt32(3),
                            Kind = Enum.TryParse(tracksReader.GetString(4), out TrackKind kind) ? kind : TrackKind.Main
                        };
                        var configJson = tracksReader.IsDBNull(5) ? null : tracksReader.GetString(5);
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

                using (var tlCmd = new SqliteCommand(@"
                    SELECT track_id, loco_id, position, offset_x 
                    FROM track_locomotives 
                    ORDER BY position;", connection))
                {
                    using var tlReader = await tlCmd.ExecuteReaderAsync();
                    var locosById = state.Locomotives.ToDictionary(l => l.Id);
                    var tracksById = state.Tiles.SelectMany(t => t.Tracks).ToDictionary(t => t.Id);

                    while (await tlReader.ReadAsync())
                    {
                        var trackId = tlReader.GetInt32(0);
                        var locoId = tlReader.GetInt32(1);
                        if (tracksById.TryGetValue(trackId, out var track) && locosById.TryGetValue(locoId, out var loco))
                        {
                            track.Locomotives.Add(loco);
                            loco.AssignedTrackId = trackId;
                            var offset = tlReader.IsDBNull(3) ? (double?)null : tlReader.GetDouble(3);
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
            finally
            {
                sw.Stop();
                Console.WriteLine($"LoadStateAsync took {sw.ElapsedMilliseconds} ms");
            }
        }

        public async Task SeedDefaultDataIfNeededAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM series;";
                var count = (long)(await command.ExecuteScalarAsync())!;
                if (count > 0)
                {
                    return;
                }
            }

            using var transaction = connection.BeginTransaction();
            var seriesId = await InsertSeriesAsync(connection, "1300", 1301, 1349);
            await InsertSeriesAsync(connection, "37000", 37001, 37040);

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

        public async Task SaveStateAsync(AppState state)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            await ExecuteNonQueryAsync(connection, "DELETE FROM track_locomotives;");
            await ExecuteNonQueryAsync(connection, "DELETE FROM tracks;");
            await ExecuteNonQueryAsync(connection, "DELETE FROM tiles;");
            await ExecuteNonQueryAsync(connection, "DELETE FROM locomotives;");
            await ExecuteNonQueryAsync(connection, "DELETE FROM series;");

            var seriesIdMap = new Dictionary<int, int>();
            foreach (var series in state.Series)
            {
                var newId = await InsertSeriesAsync(connection, series.Name, series.StartNumber, series.EndNumber);
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
                await command.ExecuteNonQueryAsync();
                loco.Id = await GetLastInsertRowIdAsync(connection);
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
                await command.ExecuteNonQueryAsync();
                tile.Id = await GetLastInsertRowIdAsync(connection);

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
                    await trackCommand.ExecuteNonQueryAsync();
                    track.Id = await GetLastInsertRowIdAsync(connection);

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
                        await assignCommand.ExecuteNonQueryAsync();
                    }
                }
            }

            SavePlaces(connection, state.Tiles);
            transaction.Commit();
        }

        public async Task AddHistoryAsync(string action, string details)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO history (timestamp, action, details) VALUES ($timestamp, $action, $details);";
            command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$details", details);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<int> InsertSeriesAsync(SqliteConnection connection, string name, int startNumber, int endNumber)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO series (name, start_number, end_number) VALUES ($name, $start, $end);";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$start", startNumber);
            command.Parameters.AddWithValue("$end", endNumber);
            await command.ExecuteNonQueryAsync();
            return await GetLastInsertRowIdAsync(connection);
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

        private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<HistoryEntry>> LoadHistoryAsync()
        {
            var history = new List<HistoryEntry>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, timestamp, action, details FROM history ORDER BY id DESC LIMIT 50;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new HistoryEntry
                {
                    Id = reader.GetInt32(0),
                    Timestamp = DateTime.Parse(reader.GetString(1)),
                    Action = reader.GetString(2),
                    Details = reader.GetString(3)
                });
            }
            return history;
        }

        public async Task<Dictionary<string, int>> GetTableCountsAsync()
        {
            var counts = new Dictionary<string, int>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            var tables = new[] { "series", "locomotives", "tiles", "tracks", "track_locomotives", "history", "places", "pdf_documents", "pdf_template_calibrations", "pdf_template_rows", "pdf_calibration_lines", "pdf_placements" };

            foreach (var table in tables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM {table};";
                counts[table] = Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            return counts;
        }

        public async Task<Dictionary<TrackKind, int>> GetTrackKindCountsAsync()
        {
            var counts = new Dictionary<TrackKind, int>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT type, COUNT(*) FROM tracks GROUP BY type;";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var kindText = reader.GetString(0);
                if (Enum.TryParse(kindText, out TrackKind kind))
                {
                    counts[kind] = reader.GetInt32(1);
                }
            }

            return counts;
        }

        public async Task ClearHistoryAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await ExecuteNonQueryAsync(connection, "DELETE FROM history;");
        }

        public async Task ResetOperationalStateAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE locomotives SET traction_percent = NULL, hs_reason = NULL;";
                await command.ExecuteNonQueryAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE tracks SET config_json = NULL WHERE type = 'Line';";
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        public Task CopyDatabaseToAsync(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return Task.CompletedTask;
            }

            System.IO.File.Copy(_databasePath, destinationPath, true);
            return Task.CompletedTask;
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

        private static async Task<int> GetLastInsertRowIdAsync(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
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

        private static async void SavePlaces(SqliteConnection connection, IEnumerable<TileModel> tiles)
        {
            const string cmdText = "INSERT OR IGNORE INTO places (type, name) VALUES ($type, $name);";
            foreach (var tile in tiles)
            {
                using var command = new SqliteCommand(cmdText, connection);
                command.Parameters.AddWithValue("$type", tile.Type.ToString());
                command.Parameters.AddWithValue("$name", tile.Name);
                await command.ExecuteNonQueryAsync();
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
