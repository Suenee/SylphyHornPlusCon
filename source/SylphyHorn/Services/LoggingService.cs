using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MetroTrilithon.Lifetime;
using MetroTrilithon.Threading.Tasks;

namespace SylphyHorn.Services
{
	public enum LogLevel
	{
		Debug,
		Info,
		Warning,
		Error,
	}

	public enum LogMode
	{
		Off,
		Single,
		All,
	}

	public interface ILog
	{
		DateTimeOffset DateTime { get; }
		string Header { get; }
		string Content { get; }
	}

	public readonly struct LogEntry
	{
		public long Sequence { get; }
		public ILog Log { get; }
		public LogLevel Level { get; }
		public string Service { get; }
		public string Event { get; }
		public string ObjectId { get; }
		public string Details { get; }

		public LogEntry(long sequence, ILog log)
			: this(sequence, log, LogLevel.Error, "APP", log?.Header ?? "Log", null, null)
		{
		}

		public LogEntry(long sequence, ILog log, LogLevel level, string service, string eventName, string objectId, string details)
		{
			this.Sequence = sequence;
			this.Log = log;
			this.Level = level;
			this.Service = string.IsNullOrWhiteSpace(service) ? "APP" : service;
			this.Event = string.IsNullOrWhiteSpace(eventName) ? "Log" : eventName;
			this.ObjectId = objectId;
			this.Details = details;
		}
	}

	public class LoggingService
	{
		private readonly object _gate = new object();
		private readonly List<LogEntry> _logs = new List<LogEntry>();
		private readonly List<Action<LogEntry>> _handlers = new List<Action<LogEntry>>();
		private long _sequence;
		private bool _configured;
		private string _logPath;
		private LogMode _mode = LogMode.Single;

		public static LoggingService Instance { get; } = new LoggingService();

		private LoggingService() { }

		public LogMode Mode
		{
			get
			{
				lock (this._gate) return this._mode;
			}
		}

		public string LogPath
		{
			get
			{
				lock (this._gate) return this._logPath;
			}
		}

		public void Configure(LogMode mode, string logPath)
		{
			if (string.IsNullOrWhiteSpace(logPath)) throw new ArgumentNullException(nameof(logPath));
			lock (this._gate)
			{
				this._logPath = Path.GetFullPath(logPath);
				this._mode = mode;
				Directory.CreateDirectory(Path.GetDirectoryName(this._logPath) ?? ".");

				if (!this._configured)
				{
					var currentSession = this._logs.ToArray();
					if (mode == LogMode.All) this.LoadPersistedUnsafe();
					else if (mode == LogMode.Single) this.TruncateFileUnsafe();

					if (mode != LogMode.Off)
					{
						foreach (var entry in currentSession) this.AppendPersistedUnsafe(entry);
					}
					this._configured = true;
				}
				else if (mode == LogMode.Single)
				{
					this.TruncateFileUnsafe();
					foreach (var entry in this._logs) this.AppendPersistedUnsafe(entry);
				}
			}
		}

		public void SetMode(LogMode mode)
		{
			lock (this._gate)
			{
				if (this._mode == mode) return;
				this._mode = mode;
				if (!this._configured || string.IsNullOrWhiteSpace(this._logPath)) return;
				if (mode == LogMode.Single)
				{
					this.TruncateFileUnsafe();
					foreach (var entry in this._logs) this.AppendPersistedUnsafe(entry);
				}
				else if (mode == LogMode.All && !File.Exists(this._logPath))
				{
					foreach (var entry in this._logs) this.AppendPersistedUnsafe(entry);
				}
			}
		}

		public void Write(LogLevel level, string service, string eventName, string message, string objectId = null, string details = null)
		{
			this.RegisterCore(new StructuredLog(message ?? string.Empty, eventName), level, service, eventName, objectId, details);
		}

		public void Register(ILog log)
		{
			if (log == null) return;
			this.RegisterCore(log, LogLevel.Error, "APP", log.Header, null, null);
		}

		private void RegisterCore(ILog log, LogLevel level, string service, string eventName, string objectId, string details)
		{
			lock (this._gate)
			{
				var entry = new LogEntry(++this._sequence, log, level, service, eventName, objectId, details);
				this._logs.Add(entry);
				if (this._configured && this._mode != LogMode.Off) this.AppendPersistedUnsafe(entry);
				foreach (var handler in this._handlers.ToArray()) handler(entry);
			}
		}

		public IDisposable Subscribe(Action<LogEntry[]> initialize, Action<LogEntry> handler)
		{
			if (initialize == null) throw new ArgumentNullException(nameof(initialize));
			if (handler == null) throw new ArgumentNullException(nameof(handler));

			lock (this._gate)
			{
				initialize(this._logs.ToArray());
				this._handlers.Add(handler);
			}

			return Disposable.Create(() =>
			{
				lock (this._gate) this._handlers.Remove(handler);
			});
		}

		public void Clear()
		{
			lock (this._gate)
			{
				this._logs.Clear();
				this._sequence = 0;
				if (this._configured && !string.IsNullOrWhiteSpace(this._logPath)) this.TruncateFileUnsafe();
			}
		}

		public void ExportText(string path, IEnumerable<LogEntry> entries)
		{
			using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
			writer.WriteLine("Timecode\tLevel\tService\tObject\tEvent\tMessage\tDetails");
			foreach (var entry in entries ?? Enumerable.Empty<LogEntry>())
			{
				writer.WriteLine(string.Join("\t", new[]
				{
					entry.Log.DateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff"),
					entry.Level.ToString().ToUpperInvariant(),
					entry.Service,
					entry.ObjectId ?? string.Empty,
					entry.Event,
					SingleLine(entry.Log.Content),
					SingleLine(entry.Details),
				}));
			}
		}

		public void Register(Exception exception)
		{
			if (exception == null) return;
			if (exception is AggregateException aggregateException)
			{
				foreach (var innerException in aggregateException.InnerExceptions) this.Register(innerException);
			}
			else
			{
				this.RegisterCore(new ExceptionLog(exception), LogLevel.Error, "APP", exception.GetType().Name, null, null);
			}
		}

		public void Register(TaskLog log)
		{
			if (log == null) return;
			this.RegisterCore(new TaskExceptionLog(log), LogLevel.Error, "APP", "UnhandledTaskException", null, null);
		}

		private void LoadPersistedUnsafe()
		{
			if (!File.Exists(this._logPath)) return;
			try
			{
				var persisted = new List<LogEntry>();
				foreach (var line in File.ReadLines(this._logPath, Encoding.UTF8))
				{
					if (string.IsNullOrWhiteSpace(line)) continue;
					try
					{
						var record = JsonSerializer.Deserialize<PersistedRecord>(line);
						if (record == null) continue;
						var log = new PersistedLog(record.Timestamp, record.Event, record.Message);
						persisted.Add(new LogEntry(++this._sequence, log, record.Level, record.Service, record.Event, record.ObjectId, record.Details));
					}
					catch { }
				}
				if (persisted.Count > 0) this._logs.InsertRange(0, persisted);
			}
			catch { }
		}

		private void AppendPersistedUnsafe(LogEntry entry)
		{
			if (string.IsNullOrWhiteSpace(this._logPath)) return;
			try
			{
				var record = new PersistedRecord
				{
					Timestamp = entry.Log.DateTime,
					Level = entry.Level,
					Service = entry.Service,
					ObjectId = entry.ObjectId,
					Event = entry.Event,
					Message = entry.Log.Content,
					Details = entry.Details,
				};
				File.AppendAllText(this._logPath, JsonSerializer.Serialize(record) + Environment.NewLine, new UTF8Encoding(false));
			}
			catch { }
		}

		private void TruncateFileUnsafe()
		{
			if (string.IsNullOrWhiteSpace(this._logPath)) return;
			try { File.WriteAllText(this._logPath, string.Empty, new UTF8Encoding(false)); } catch { }
		}

		private static string SingleLine(string value) => (value ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

		private sealed class StructuredLog : ILog
		{
			public StructuredLog(string content, string header)
			{
				this.DateTime = DateTimeOffset.Now;
				this.Header = string.IsNullOrWhiteSpace(header) ? "Log" : header;
				this.Content = content;
			}
			public DateTimeOffset DateTime { get; }
			public string Header { get; }
			public string Content { get; }
		}

		private sealed class PersistedLog : ILog
		{
			public PersistedLog(DateTimeOffset timestamp, string header, string content)
			{
				this.DateTime = timestamp;
				this.Header = header ?? "Log";
				this.Content = content ?? string.Empty;
			}
			public DateTimeOffset DateTime { get; }
			public string Header { get; }
			public string Content { get; }
		}

		private sealed class ExceptionLog : ILog
		{
			public DateTimeOffset DateTime { get; } = DateTimeOffset.Now;
			public string Header { get; }
			public string Content { get; }
			public ExceptionLog(Exception ex)
			{
				this.Header = ex.GetType().Name;
				this.Content = ex.ToString();
			}
		}

		private sealed class TaskExceptionLog : ILog
		{
			public DateTimeOffset DateTime { get; } = DateTimeOffset.Now;
			public string Header { get; }
			public string Content { get; }
			public TaskExceptionLog(TaskLog log)
			{
				this.Header = log.Exception.GetType().Name;
				this.Content = $@"Unhandled exception was thrown by Task<T>.
{log.CallerMemberName} ({System.IO.Path.GetFileName(log.CallerFilePath)}#{log.CallerLineNumber})
-----
{log.Exception}";
			}
		}

		private sealed class PersistedRecord
		{
			public DateTimeOffset Timestamp { get; set; }
			public LogLevel Level { get; set; }
			public string Service { get; set; }
			public string ObjectId { get; set; }
			public string Event { get; set; }
			public string Message { get; set; }
			public string Details { get; set; }
		}
	}
}
