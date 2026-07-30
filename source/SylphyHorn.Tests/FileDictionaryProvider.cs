using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Xml;
using SylphyHorn.Serialization;

namespace SylphyHorn.Tests
{
	internal sealed class FileDictionaryProvider : DictionaryProvider
	{
		private readonly string _path;
		private IDictionary<string, object> _lastReadValues = new Dictionary<string, object>();

		public FileDictionaryProvider(string path)
		{
			this._path = Path.GetFullPath(path);
		}

		public Task InitializeAsync()
		{
			return this.LoadAsync();
		}

		internal IDictionary<string, object> LastReadValues => this._lastReadValues;

		protected override Task SaveAsyncCore(IDictionary<string, object> dic)
		{
			return this.WriteAsync(dic, this._path);
		}

		protected override Task SaveAsyncCore(IDictionary<string, object> dic, string path)
		{
			return this.WriteAsync(dic, Path.GetFullPath(path));
		}

		protected override Task<IDictionary<string, object>> LoadAsyncCore()
		{
			return this.ReadAsync(this._path);
		}

		protected override Task<IDictionary<string, object>> LoadAsyncCore(string path)
		{
			return this.ReadAsync(Path.GetFullPath(path));
		}

		private Task WriteAsync(IDictionary<string, object> values, string path)
		{
			return Task.Run(() =>
			{
				var directory = Path.GetDirectoryName(path);
				if (directory == null)
				{
					throw new InvalidOperationException($"The settings path has no directory: {path}");
				}

				Directory.CreateDirectory(directory);
				var serializer = new DataContractSerializer(values.GetType(), this.KnownTypes);
				var settings = new XmlWriterSettings
				{
					Indent = true,
				};

				using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
				using (var writer = XmlWriter.Create(stream, settings))
				{
					serializer.WriteObject(writer, values);
				}
			});
		}

		private Task<IDictionary<string, object>> ReadAsync(string path)
		{
			return Task.Run(() =>
			{
				if (!File.Exists(path))
				{
					this._lastReadValues = new Dictionary<string, object>();
					return null;
				}

				var serializer = new DataContractSerializer(
					typeof(IDictionary<string, object>),
					this.KnownTypes);
				using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					var values = serializer.ReadObject(stream) as IDictionary<string, object>;
					this._lastReadValues = values ?? new Dictionary<string, object>();
					return values;
				}
			});
		}
	}
}
