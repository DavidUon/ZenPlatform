using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ZenPlatform.SessionManager.Backtest
{
    public sealed class BacktestRecorder : IDisposable
    {
        private readonly object _sync = new();
        private readonly SqliteConnection _connection;
        private SqliteTransaction _tx;
        private readonly SqliteCommand _insertRunCmd;
        private readonly SqliteCommand _insertBarCmd;
        private readonly SqliteCommand _insertEventCmd;
        private readonly SqliteCommand _insertTrendStateCmd;
        private readonly SqliteCommand _upsertSessionCmd;
        private readonly SqliteCommand _insertOrderMarkCmd;
        private readonly SqliteCommand _insertLogCmd;
        private readonly SqliteCommand _upsertEquityCurveCmd;
        private readonly SqliteCommand _upsertRunSummaryCmd;
        private readonly SqliteCommand _upsertStrategySnapshotCmd;
        private readonly SqliteCommand _endRunCmd;
        private readonly bool _enableDevDiagnostics;
        private long _messageSequence;
        private bool _disposed;

        public BacktestRecorder(string dbPath, bool? enableDevDiagnostics = null)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                throw new ArgumentException("dbPath is required.", nameof(dbPath));
            }

            var fullPath = Path.GetFullPath(dbPath);
            var folder = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            DbPath = fullPath;
            _enableDevDiagnostics = enableDevDiagnostics ?? DefaultDevDiagnosticsEnabled;
            _connection = new SqliteConnection($"Data Source={fullPath}");
            _connection.Open();

            using (var pragma = _connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }

            CreateSchema(_connection, _enableDevDiagnostics);
            _tx = _connection.BeginTransaction();

            _insertRunCmd = _connection.CreateCommand();
            _insertRunCmd.Transaction = _tx;
            _insertRunCmd.CommandText =
                @"INSERT INTO runs(
                    run_id, created_at_utc, start_time_utc, end_time_utc, product, mode, strategy_name, params_json, code_version, status, summary_json
                  ) VALUES (
                    $run_id, $created_at_utc, $start_time_utc, $end_time_utc, $product, $mode, $strategy_name, $params_json, $code_version, $status, $summary_json
                  );";
            _insertRunCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$created_at_utc", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$start_time_utc", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$end_time_utc", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$product", SqliteType.Integer);
            _insertRunCmd.Parameters.Add("$mode", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$strategy_name", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$params_json", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$code_version", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$status", SqliteType.Text);
            _insertRunCmd.Parameters.Add("$summary_json", SqliteType.Text);

            _insertBarCmd = _connection.CreateCommand();
            _insertBarCmd.Transaction = _tx;
            _insertBarCmd.CommandText =
                @"INSERT INTO bars(
                    run_id, time_utc, period, open, high, low, close, volume
                  ) VALUES (
                    $run_id, $time_utc, $period, $open, $high, $low, $close, $volume
                  )
                  ON CONFLICT(run_id, time_utc, period) DO UPDATE SET
                    open = excluded.open,
                    high = excluded.high,
                    low = excluded.low,
                    close = excluded.close,
                    volume = excluded.volume;";
            _insertBarCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertBarCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _insertBarCmd.Parameters.Add("$period", SqliteType.Integer);
            _insertBarCmd.Parameters.Add("$open", SqliteType.Real);
            _insertBarCmd.Parameters.Add("$high", SqliteType.Real);
            _insertBarCmd.Parameters.Add("$low", SqliteType.Real);
            _insertBarCmd.Parameters.Add("$close", SqliteType.Real);
            _insertBarCmd.Parameters.Add("$volume", SqliteType.Integer);

            _insertEventCmd = _connection.CreateCommand();
            _insertEventCmd.Transaction = _tx;
            _insertEventCmd.CommandText =
                @"INSERT INTO events(
                    run_id, time_utc, seq, level, event_type, message, session_index
                  ) VALUES (
                    $run_id, $time_utc, $seq, $level, $event_type, $message, $session_index
                  );";
            _insertEventCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertEventCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _insertEventCmd.Parameters.Add("$seq", SqliteType.Integer);
            _insertEventCmd.Parameters.Add("$level", SqliteType.Text);
            _insertEventCmd.Parameters.Add("$event_type", SqliteType.Text);
            _insertEventCmd.Parameters.Add("$message", SqliteType.Text);
            _insertEventCmd.Parameters.Add("$session_index", SqliteType.Integer);

            _insertTrendStateCmd = _connection.CreateCommand();
            _insertTrendStateCmd.Transaction = _tx;
            _insertTrendStateCmd.CommandText =
                @"INSERT INTO trend_states(
                    run_id, time_utc, period, side
                  ) VALUES (
                    $run_id, $time_utc, $period, $side
                  )
                  ON CONFLICT(run_id, time_utc, period) DO UPDATE SET
                    side = excluded.side;";
            _insertTrendStateCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertTrendStateCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _insertTrendStateCmd.Parameters.Add("$period", SqliteType.Integer);
            _insertTrendStateCmd.Parameters.Add("$side", SqliteType.Integer);

            _upsertSessionCmd = _connection.CreateCommand();
            _upsertSessionCmd.Transaction = _tx;
            _upsertSessionCmd.CommandText =
                @"INSERT INTO sessions(
                    run_id, session_id, start_time_utc, end_time_utc, start_position, is_finished, realized_profit, trade_count, max_total_loss
                  ) VALUES (
                    $run_id, $session_id, $start_time_utc, $end_time_utc, $start_position, $is_finished, $realized_profit, $trade_count, $max_total_loss
                  )
                  ON CONFLICT(run_id, session_id) DO UPDATE SET
                    start_time_utc = excluded.start_time_utc,
                    end_time_utc = excluded.end_time_utc,
                    start_position = excluded.start_position,
                    is_finished = excluded.is_finished,
                    realized_profit = excluded.realized_profit,
                    trade_count = excluded.trade_count,
                    max_total_loss = excluded.max_total_loss;";
            _upsertSessionCmd.Parameters.Add("$run_id", SqliteType.Text);
            _upsertSessionCmd.Parameters.Add("$session_id", SqliteType.Integer);
            _upsertSessionCmd.Parameters.Add("$start_time_utc", SqliteType.Text);
            _upsertSessionCmd.Parameters.Add("$end_time_utc", SqliteType.Text);
            _upsertSessionCmd.Parameters.Add("$start_position", SqliteType.Integer);
            _upsertSessionCmd.Parameters.Add("$is_finished", SqliteType.Integer);
            _upsertSessionCmd.Parameters.Add("$realized_profit", SqliteType.Real);
            _upsertSessionCmd.Parameters.Add("$trade_count", SqliteType.Integer);
            _upsertSessionCmd.Parameters.Add("$max_total_loss", SqliteType.Real);

            _insertOrderMarkCmd = _connection.CreateCommand();
            _insertOrderMarkCmd.Transaction = _tx;
            _insertOrderMarkCmd.CommandText =
                @"INSERT INTO order_marks(
                    run_id, time_utc, seq, session_id, event_type, side, qty, price, reason
                  ) VALUES (
                    $run_id, $time_utc, $seq, $session_id, $event_type, $side, $qty, $price, $reason
                  );";
            _insertOrderMarkCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertOrderMarkCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _insertOrderMarkCmd.Parameters.Add("$seq", SqliteType.Integer);
            _insertOrderMarkCmd.Parameters.Add("$session_id", SqliteType.Integer);
            _insertOrderMarkCmd.Parameters.Add("$event_type", SqliteType.Text);
            _insertOrderMarkCmd.Parameters.Add("$side", SqliteType.Integer);
            _insertOrderMarkCmd.Parameters.Add("$qty", SqliteType.Integer);
            _insertOrderMarkCmd.Parameters.Add("$price", SqliteType.Real);
            _insertOrderMarkCmd.Parameters.Add("$reason", SqliteType.Text);

            _insertLogCmd = _connection.CreateCommand();
            _insertLogCmd.Transaction = _tx;
            _insertLogCmd.CommandText =
                @"INSERT INTO logs(
                    run_id, time_utc, seq, session_id, scope, message
                  ) VALUES (
                    $run_id, $time_utc, $seq, $session_id, $scope, $message
                  );";
            _insertLogCmd.Parameters.Add("$run_id", SqliteType.Text);
            _insertLogCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _insertLogCmd.Parameters.Add("$seq", SqliteType.Integer);
            _insertLogCmd.Parameters.Add("$session_id", SqliteType.Integer);
            _insertLogCmd.Parameters.Add("$scope", SqliteType.Text);
            _insertLogCmd.Parameters.Add("$message", SqliteType.Text);

            _upsertEquityCurveCmd = _connection.CreateCommand();
            _upsertEquityCurveCmd.Transaction = _tx;
            _upsertEquityCurveCmd.CommandText =
                @"INSERT INTO equity_curve(
                    run_id, time_utc, total_profit, float_profit, realized_profit
                  ) VALUES (
                    $run_id, $time_utc, $total_profit, $float_profit, $realized_profit
                  )
                  ON CONFLICT(run_id, time_utc) DO UPDATE SET
                    total_profit = excluded.total_profit,
                    float_profit = excluded.float_profit,
                    realized_profit = excluded.realized_profit;";
            _upsertEquityCurveCmd.Parameters.Add("$run_id", SqliteType.Text);
            _upsertEquityCurveCmd.Parameters.Add("$time_utc", SqliteType.Text);
            _upsertEquityCurveCmd.Parameters.Add("$total_profit", SqliteType.Real);
            _upsertEquityCurveCmd.Parameters.Add("$float_profit", SqliteType.Real);
            _upsertEquityCurveCmd.Parameters.Add("$realized_profit", SqliteType.Real);

            _upsertRunSummaryCmd = _connection.CreateCommand();
            _upsertRunSummaryCmd.Transaction = _tx;
            _upsertRunSummaryCmd.CommandText =
                @"INSERT INTO run_summary(
                    run_id, summary_json
                  ) VALUES (
                    $run_id, $summary_json
                  )
                  ON CONFLICT(run_id) DO UPDATE SET
                    summary_json = excluded.summary_json;";
            _upsertRunSummaryCmd.Parameters.Add("$run_id", SqliteType.Text);
            _upsertRunSummaryCmd.Parameters.Add("$summary_json", SqliteType.Text);

            _upsertStrategySnapshotCmd = _connection.CreateCommand();
            _upsertStrategySnapshotCmd.Transaction = _tx;
            _upsertStrategySnapshotCmd.CommandText =
                @"INSERT INTO strategy_snapshot(
                    run_id, params_json
                  ) VALUES (
                    $run_id, $params_json
                  )
                  ON CONFLICT(run_id) DO UPDATE SET
                    params_json = excluded.params_json;";
            _upsertStrategySnapshotCmd.Parameters.Add("$run_id", SqliteType.Text);
            _upsertStrategySnapshotCmd.Parameters.Add("$params_json", SqliteType.Text);

            _endRunCmd = _connection.CreateCommand();
            _endRunCmd.Transaction = _tx;
            _endRunCmd.CommandText =
                @"UPDATE runs
                  SET end_time_utc = $end_time_utc,
                      status = $status,
                      summary_json = $summary_json
                  WHERE run_id = $run_id;";
            _endRunCmd.Parameters.Add("$end_time_utc", SqliteType.Text);
            _endRunCmd.Parameters.Add("$status", SqliteType.Text);
            _endRunCmd.Parameters.Add("$summary_json", SqliteType.Text);
            _endRunCmd.Parameters.Add("$run_id", SqliteType.Text);
        }

        public string DbPath { get; }
        public string? RunId { get; private set; }
        public bool EnableDevDiagnostics => _enableDevDiagnostics;

        private static bool DefaultDevDiagnosticsEnabled
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public string BeginRun(DateTime startTime, int? product, string mode, string strategyName, string? paramsJson, string? codeVersion)
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                var runId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
                RunId = runId;

                _insertRunCmd.Parameters["$run_id"].Value = runId;
                _insertRunCmd.Parameters["$created_at_utc"].Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                _insertRunCmd.Parameters["$start_time_utc"].Value = ToUtcText(startTime);
                _insertRunCmd.Parameters["$end_time_utc"].Value = DBNull.Value;
                _insertRunCmd.Parameters["$product"].Value = product.HasValue ? product.Value : DBNull.Value;
                _insertRunCmd.Parameters["$mode"].Value = mode ?? string.Empty;
                _insertRunCmd.Parameters["$strategy_name"].Value = strategyName ?? string.Empty;
                _insertRunCmd.Parameters["$params_json"].Value = string.IsNullOrWhiteSpace(paramsJson) ? DBNull.Value : paramsJson;
                _insertRunCmd.Parameters["$code_version"].Value = string.IsNullOrWhiteSpace(codeVersion) ? DBNull.Value : codeVersion;
                _insertRunCmd.Parameters["$status"].Value = "Running";
                _insertRunCmd.Parameters["$summary_json"].Value = DBNull.Value;
                _insertRunCmd.ExecuteNonQuery();
                UpsertStrategySnapshotLocked(runId, paramsJson);
                _messageSequence = 0;

                return runId;
            }
        }

        public void AppendBar(DateTime time, int period, decimal open, decimal high, decimal low, decimal close, long volume)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _insertBarCmd.Parameters["$run_id"].Value = runId;
                _insertBarCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _insertBarCmd.Parameters["$period"].Value = period;
                _insertBarCmd.Parameters["$open"].Value = (double)open;
                _insertBarCmd.Parameters["$high"].Value = (double)high;
                _insertBarCmd.Parameters["$low"].Value = (double)low;
                _insertBarCmd.Parameters["$close"].Value = (double)close;
                _insertBarCmd.Parameters["$volume"].Value = volume;
                _insertBarCmd.ExecuteNonQuery();
            }
        }

        public void AppendEvent(DateTime time, string level, string eventType, string message, int? sessionIndex = null)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _insertEventCmd.Parameters["$run_id"].Value = runId;
                _insertEventCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _insertEventCmd.Parameters["$seq"].Value = NextMessageSequence();
                _insertEventCmd.Parameters["$level"].Value = string.IsNullOrWhiteSpace(level) ? "Info" : level;
                _insertEventCmd.Parameters["$event_type"].Value = string.IsNullOrWhiteSpace(eventType) ? "General" : eventType;
                _insertEventCmd.Parameters["$message"].Value = message ?? string.Empty;
                _insertEventCmd.Parameters["$session_index"].Value = sessionIndex.HasValue ? sessionIndex.Value : DBNull.Value;
                _insertEventCmd.ExecuteNonQuery();
            }
        }

        public void AppendTrendState(DateTime time, int period, int side)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _insertTrendStateCmd.Parameters["$run_id"].Value = runId;
                _insertTrendStateCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _insertTrendStateCmd.Parameters["$period"].Value = period;
                _insertTrendStateCmd.Parameters["$side"].Value = side;
                _insertTrendStateCmd.ExecuteNonQuery();
            }
        }

        public void EndRun(DateTime endTime, string status = "Completed", string? summaryJson = null)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _endRunCmd.Parameters["$end_time_utc"].Value = ToUtcText(endTime);
                _endRunCmd.Parameters["$status"].Value = string.IsNullOrWhiteSpace(status) ? "Completed" : status;
                _endRunCmd.Parameters["$summary_json"].Value = string.IsNullOrWhiteSpace(summaryJson) ? DBNull.Value : summaryJson;
                _endRunCmd.Parameters["$run_id"].Value = runId;
                _endRunCmd.ExecuteNonQuery();
                UpsertRunSummaryLocked(runId, summaryJson);
            }
        }

        public void AppendSession(
            int sessionId,
            DateTime startTime,
            DateTime? endTime,
            int startPosition,
            bool isFinished,
            decimal realizedProfit,
            int tradeCount,
            decimal maxTotalLoss)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _upsertSessionCmd.Parameters["$run_id"].Value = runId;
                _upsertSessionCmd.Parameters["$session_id"].Value = sessionId;
                _upsertSessionCmd.Parameters["$start_time_utc"].Value = ToUtcText(startTime);
                _upsertSessionCmd.Parameters["$end_time_utc"].Value = endTime.HasValue ? ToUtcText(endTime.Value) : DBNull.Value;
                _upsertSessionCmd.Parameters["$start_position"].Value = startPosition;
                _upsertSessionCmd.Parameters["$is_finished"].Value = isFinished ? 1 : 0;
                _upsertSessionCmd.Parameters["$realized_profit"].Value = (double)realizedProfit;
                _upsertSessionCmd.Parameters["$trade_count"].Value = tradeCount;
                _upsertSessionCmd.Parameters["$max_total_loss"].Value = (double)maxTotalLoss;
                _upsertSessionCmd.ExecuteNonQuery();
            }
        }

        public void AppendOrderMark(DateTime time, int sessionId, string eventType, int side, int qty, decimal price, string? reason)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _insertOrderMarkCmd.Parameters["$run_id"].Value = runId;
                _insertOrderMarkCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _insertOrderMarkCmd.Parameters["$seq"].Value = NextMessageSequence();
                _insertOrderMarkCmd.Parameters["$session_id"].Value = sessionId;
                _insertOrderMarkCmd.Parameters["$event_type"].Value = string.IsNullOrWhiteSpace(eventType) ? "General" : eventType;
                _insertOrderMarkCmd.Parameters["$side"].Value = side;
                _insertOrderMarkCmd.Parameters["$qty"].Value = qty;
                _insertOrderMarkCmd.Parameters["$price"].Value = (double)price;
                _insertOrderMarkCmd.Parameters["$reason"].Value = string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason;
                _insertOrderMarkCmd.ExecuteNonQuery();
            }
        }

        public void AppendLog(DateTime time, string message, int? sessionId = null)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _insertLogCmd.Parameters["$run_id"].Value = runId;
                _insertLogCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _insertLogCmd.Parameters["$seq"].Value = NextMessageSequence();
                _insertLogCmd.Parameters["$session_id"].Value = sessionId.HasValue ? sessionId.Value : DBNull.Value;
                _insertLogCmd.Parameters["$scope"].Value = sessionId.HasValue ? "session" : "global";
                _insertLogCmd.Parameters["$message"].Value = message ?? string.Empty;
                _insertLogCmd.ExecuteNonQuery();
            }
        }

        public void AppendEquityCurve(DateTime time, decimal totalProfit, decimal floatProfit, decimal realizedProfit)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();

                _upsertEquityCurveCmd.Parameters["$run_id"].Value = runId;
                _upsertEquityCurveCmd.Parameters["$time_utc"].Value = ToUtcText(time);
                _upsertEquityCurveCmd.Parameters["$total_profit"].Value = (double)totalProfit;
                _upsertEquityCurveCmd.Parameters["$float_profit"].Value = (double)floatProfit;
                _upsertEquityCurveCmd.Parameters["$realized_profit"].Value = (double)realizedProfit;
                _upsertEquityCurveCmd.ExecuteNonQuery();
            }
        }

        // Debug-only diagnostics. Release build default is disabled via DefaultDevDiagnosticsEnabled.
        public void AppendIndicatorValue(DateTime time, int period, string name, decimal value)
        {
            if (!_enableDevDiagnostics)
            {
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = _tx;
                cmd.CommandText =
                    @"INSERT INTO indicator_values(run_id, time_utc, period, name, value)
                      VALUES($run_id, $time_utc, $period, $name, $value);";
                cmd.Parameters.AddWithValue("$run_id", runId);
                cmd.Parameters.AddWithValue("$time_utc", ToUtcText(time));
                cmd.Parameters.AddWithValue("$period", period);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? string.Empty : name);
                cmd.Parameters.AddWithValue("$value", (double)value);
                cmd.ExecuteNonQuery();
            }
        }

        // Debug-only diagnostics. Release build default is disabled via DefaultDevDiagnosticsEnabled.
        public void AppendTriggerDiagnostic(
            DateTime time,
            string triggerId,
            string source,
            int currentSide,
            int signalSide,
            string result,
            string? message = null)
        {
            if (!_enableDevDiagnostics)
            {
                return;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                var runId = EnsureRunStarted();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = _tx;
                cmd.CommandText =
                    @"INSERT INTO trigger_diagnostics(
                        run_id, time_utc, seq, trigger_id, source, current_side, signal_side, result, message
                      ) VALUES (
                        $run_id, $time_utc, $seq, $trigger_id, $source, $current_side, $signal_side, $result, $message
                      );";
                cmd.Parameters.AddWithValue("$run_id", runId);
                cmd.Parameters.AddWithValue("$time_utc", ToUtcText(time));
                cmd.Parameters.AddWithValue("$seq", NextMessageSequence());
                cmd.Parameters.AddWithValue("$trigger_id", string.IsNullOrWhiteSpace(triggerId) ? string.Empty : triggerId);
                cmd.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(source) ? string.Empty : source);
                cmd.Parameters.AddWithValue("$current_side", currentSide);
                cmd.Parameters.AddWithValue("$signal_side", signalSide);
                cmd.Parameters.AddWithValue("$result", string.IsNullOrWhiteSpace(result) ? string.Empty : result);
                cmd.Parameters.AddWithValue("$message", string.IsNullOrWhiteSpace(message) ? (object)DBNull.Value : message);
                cmd.ExecuteNonQuery();
            }
        }

        public void Flush()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _tx.Commit();
                _tx.Dispose();
                _tx = _connection.BeginTransaction();
                _insertRunCmd.Transaction = _tx;
                _insertBarCmd.Transaction = _tx;
                _insertEventCmd.Transaction = _tx;
                _insertTrendStateCmd.Transaction = _tx;
                _upsertSessionCmd.Transaction = _tx;
                _insertOrderMarkCmd.Transaction = _tx;
                _insertLogCmd.Transaction = _tx;
                _upsertEquityCurveCmd.Transaction = _tx;
                _upsertRunSummaryCmd.Transaction = _tx;
                _upsertStrategySnapshotCmd.Transaction = _tx;
                _endRunCmd.Transaction = _tx;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _tx.Commit();
                }
                catch
                {
                    // Best-effort commit on dispose.
                }
                finally
                {
                    try
                    {
                        _tx.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                }

                try
                {
                    // Ensure WAL contents are merged into the main .btdb file so file copy remains self-contained.
                    using var checkpoint = _connection.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                catch
                {
                    // Best-effort checkpoint.
                }

                try
                {
                    // Switch back to rollback journal mode before close to minimize orphaned sidecar risk.
                    using var journal = _connection.CreateCommand();
                    journal.CommandText = "PRAGMA journal_mode=DELETE;";
                    journal.ExecuteNonQuery();
                }
                catch
                {
                    // Best-effort journal mode fallback.
                }

                _endRunCmd.Dispose();
                _upsertStrategySnapshotCmd.Dispose();
                _upsertRunSummaryCmd.Dispose();
                _insertLogCmd.Dispose();
                _upsertEquityCurveCmd.Dispose();
                _insertOrderMarkCmd.Dispose();
                _upsertSessionCmd.Dispose();
                _insertTrendStateCmd.Dispose();
                _insertEventCmd.Dispose();
                _insertBarCmd.Dispose();
                _insertRunCmd.Dispose();
                _connection.Dispose();
                _disposed = true;
            }
        }

        private string EnsureRunStarted()
        {
            if (string.IsNullOrWhiteSpace(RunId))
            {
                throw new InvalidOperationException("Run has not started. Call BeginRun first.");
            }

            return RunId;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BacktestRecorder));
            }
        }

        private static string ToUtcText(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return utc.ToString("O", CultureInfo.InvariantCulture);
        }

        private static void CreateSchema(SqliteConnection connection, bool enableDevDiagnostics)
        {
            using var cmd = connection.CreateCommand();
            var coreSql =
                @"CREATE TABLE IF NOT EXISTS runs (
                    run_id TEXT PRIMARY KEY,
                    created_at_utc TEXT NOT NULL,
                    start_time_utc TEXT,
                    end_time_utc TEXT,
                    product INTEGER,
                    mode TEXT,
                    strategy_name TEXT,
                    params_json TEXT,
                    code_version TEXT,
                    status TEXT NOT NULL,
                    summary_json TEXT
                );

                CREATE TABLE IF NOT EXISTS bars (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    period INTEGER NOT NULL,
                    open REAL NOT NULL,
                    high REAL NOT NULL,
                    low REAL NOT NULL,
                    close REAL NOT NULL,
                    volume INTEGER NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );

                CREATE INDEX IF NOT EXISTS idx_bars_run_time ON bars(run_id, time_utc, period);
                CREATE UNIQUE INDEX IF NOT EXISTS uq_bars_run_time_period ON bars(run_id, time_utc, period);

                CREATE TABLE IF NOT EXISTS events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    seq INTEGER NOT NULL DEFAULT 0,
                    level TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    session_index INTEGER,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );

                CREATE INDEX IF NOT EXISTS idx_events_run_time ON events(run_id, time_utc, id);
                CREATE INDEX IF NOT EXISTS idx_events_run_seq ON events(run_id, seq, id);

                CREATE TABLE IF NOT EXISTS trend_states (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    period INTEGER NOT NULL,
                    side INTEGER NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );

                CREATE INDEX IF NOT EXISTS idx_trend_states_run_time ON trend_states(run_id, time_utc, period);
                CREATE UNIQUE INDEX IF NOT EXISTS uq_trend_states_run_time_period ON trend_states(run_id, time_utc, period);

                CREATE TABLE IF NOT EXISTS sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    session_id INTEGER NOT NULL,
                    start_time_utc TEXT NOT NULL,
                    end_time_utc TEXT,
                    start_position INTEGER NOT NULL,
                    is_finished INTEGER NOT NULL,
                    realized_profit REAL NOT NULL,
                    trade_count INTEGER NOT NULL,
                    max_total_loss REAL NOT NULL DEFAULT 0,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_sessions_run_session ON sessions(run_id, session_id);
                CREATE INDEX IF NOT EXISTS idx_sessions_run_start ON sessions(run_id, start_time_utc);

                CREATE TABLE IF NOT EXISTS order_marks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    seq INTEGER NOT NULL DEFAULT 0,
                    session_id INTEGER NOT NULL,
                    event_type TEXT NOT NULL,
                    side INTEGER NOT NULL,
                    qty INTEGER NOT NULL,
                    price REAL NOT NULL,
                    reason TEXT,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE INDEX IF NOT EXISTS idx_order_marks_run_time ON order_marks(run_id, time_utc, id);
                CREATE INDEX IF NOT EXISTS idx_order_marks_run_session_time ON order_marks(run_id, session_id, time_utc);
                CREATE INDEX IF NOT EXISTS idx_order_marks_run_seq ON order_marks(run_id, seq, id);

                CREATE TABLE IF NOT EXISTS logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    seq INTEGER NOT NULL DEFAULT 0,
                    session_id INTEGER,
                    scope TEXT NOT NULL DEFAULT 'global',
                    message TEXT NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE INDEX IF NOT EXISTS idx_logs_run_time ON logs(run_id, time_utc, id);
                CREATE INDEX IF NOT EXISTS idx_logs_run_seq ON logs(run_id, seq, id);

                CREATE TABLE IF NOT EXISTS equity_curve (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    total_profit REAL NOT NULL,
                    float_profit REAL NOT NULL,
                    realized_profit REAL NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_equity_curve_run_time ON equity_curve(run_id, time_utc);
                CREATE INDEX IF NOT EXISTS idx_equity_curve_run_time ON equity_curve(run_id, time_utc);

                CREATE TABLE IF NOT EXISTS run_summary (
                    run_id TEXT PRIMARY KEY,
                    summary_json TEXT,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );

                CREATE TABLE IF NOT EXISTS strategy_snapshot (
                    run_id TEXT PRIMARY KEY,
                    params_json TEXT,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );";
            var devSql =
                @"CREATE TABLE IF NOT EXISTS indicator_values (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    period INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    value REAL NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE INDEX IF NOT EXISTS idx_indicator_values_run_time ON indicator_values(run_id, time_utc, id);

                CREATE TABLE IF NOT EXISTS trigger_diagnostics (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id TEXT NOT NULL,
                    time_utc TEXT NOT NULL,
                    seq INTEGER NOT NULL DEFAULT 0,
                    trigger_id TEXT NOT NULL,
                    source TEXT NOT NULL,
                    current_side INTEGER NOT NULL,
                    signal_side INTEGER NOT NULL,
                    result TEXT NOT NULL,
                    message TEXT,
                    FOREIGN KEY(run_id) REFERENCES runs(run_id)
                );
                CREATE INDEX IF NOT EXISTS idx_trigger_diag_run_time ON trigger_diagnostics(run_id, time_utc, id);
                CREATE INDEX IF NOT EXISTS idx_trigger_diag_run_seq ON trigger_diagnostics(run_id, seq, id);";

            cmd.CommandText = enableDevDiagnostics ? coreSql + Environment.NewLine + devSql : coreSql;
            cmd.ExecuteNonQuery();

            EnsureColumn(connection, "logs", "session_id", "INTEGER");
            EnsureColumn(connection, "logs", "scope", "TEXT NOT NULL DEFAULT 'global'");
            EnsureColumn(connection, "events", "seq", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "order_marks", "seq", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "logs", "seq", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "sessions", "max_total_loss", "REAL NOT NULL DEFAULT 0");
        }

        private long NextMessageSequence()
        {
            _messageSequence++;
            return _messageSequence;
        }

        private void UpsertRunSummaryLocked(string runId, string? summaryJson)
        {
            _upsertRunSummaryCmd.Parameters["$run_id"].Value = runId;
            _upsertRunSummaryCmd.Parameters["$summary_json"].Value = string.IsNullOrWhiteSpace(summaryJson) ? DBNull.Value : summaryJson;
            _upsertRunSummaryCmd.ExecuteNonQuery();
        }

        private void UpsertStrategySnapshotLocked(string runId, string? paramsJson)
        {
            _upsertStrategySnapshotCmd.Parameters["$run_id"].Value = runId;
            _upsertStrategySnapshotCmd.Parameters["$params_json"].Value = string.IsNullOrWhiteSpace(paramsJson) ? DBNull.Value : paramsJson;
            _upsertStrategySnapshotCmd.ExecuteNonQuery();
        }

        private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
        {
            if (HasColumn(connection, tableName, columnName))
            {
                return;
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
            cmd.ExecuteNonQuery();
        }

        private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader["name"]?.ToString();
                if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
