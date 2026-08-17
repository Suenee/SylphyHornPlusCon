using CommunityToolkit.Mvvm.ComponentModel;
using SylphyHorn.Properties;
using SylphyHorn.Services;

namespace SylphyHorn.UI.Bindings
{
	public class HeaderContentViewModel : ObservableObject
	{
		#region Header 変更通知プロパティ

		private string _Header;

		public string Header
		{
			get => this._Header;
			set
			{
				if (this._Header != value)
				{
					this._Header = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		#region Content 変更通知プロパティ

		private string _Content;

		public string Content
		{
			get => this._Content;
			set
			{
				if (this._Content != value)
				{
					this._Content = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion
	}

	public class LogViewModel : HeaderContentViewModel
	{
		public LogViewModel(ILog log)
		{
			this.Header = $"{log.DateTime:G} {log.Header}";
			this.Content = log.Content;
		}
	}

	public class LicenseViewModel : HeaderContentViewModel
	{
		public LicenseViewModel(LicenseInfo license)
		{
			this.Header = license.ProductName;
			this.Content = license.LicenseBody;
		}
	}
}
