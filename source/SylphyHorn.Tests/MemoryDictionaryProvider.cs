using System.Collections.Generic;
using System.Threading.Tasks;
using SylphyHorn.Serialization;

namespace SylphyHorn.Tests
{
	internal sealed class MemoryDictionaryProvider : DictionaryProvider
	{
		private IDictionary<string, object> _loadValues;

		public MemoryDictionaryProvider(IDictionary<string, object> initialValues = null)
		{
			this._loadValues = Clone(initialValues);
		}

		public Task InitializeAsync()
		{
			return this.LoadAsync();
		}

		public Task ReloadAsync(IDictionary<string, object> values)
		{
			this._loadValues = Clone(values);
			return this.ImportAsync("memory");
		}

		protected override Task SaveAsyncCore(IDictionary<string, object> dic)
		{
			this._loadValues = Clone(dic);
			return Task.CompletedTask;
		}

		protected override Task SaveAsyncCore(IDictionary<string, object> dic, string path)
		{
			this._loadValues = Clone(dic);
			return Task.CompletedTask;
		}

		protected override Task<IDictionary<string, object>> LoadAsyncCore()
		{
			return Task.FromResult(Clone(this._loadValues));
		}

		protected override Task<IDictionary<string, object>> LoadAsyncCore(string path)
		{
			return Task.FromResult(Clone(this._loadValues));
		}

		private static IDictionary<string, object> Clone(IDictionary<string, object> values)
		{
			return values == null
				? new Dictionary<string, object>()
				: new Dictionary<string, object>(values);
		}
	}
}
