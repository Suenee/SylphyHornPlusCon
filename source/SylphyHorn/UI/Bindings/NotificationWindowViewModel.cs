using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SylphyHorn.Serialization;
using SylphyHorn.Services;

namespace SylphyHorn.UI.Bindings
{
	public class NotificationWindowViewModel : ObservableObject
	{
		private readonly NotificationVisualSettings _visual;

		public NotificationWindowViewModel() : this(null, null, null, NotificationVisualSettings.Capture(Settings.General))
		{
		}

		internal NotificationWindowViewModel(string title, string header, string body, NotificationVisualSettings visual)
		{
			this._visual = visual ?? throw new System.ArgumentNullException(nameof(visual));
			this._Title = title;
			this._Header = header;
			this._Body = body;
		}

		#region Title 変更通知プロパティ

		private string _Title;

		public string Title
		{
			get { return this._Title; }
			set
			{
				if (this._Title != value)
				{
					this._Title = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		#region Header 変更通知プロパティ

		private string _Header;

		public string Header
		{
			get { return this._Header; }
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

		#region Body 変更通知プロパティ

		private string _Body;

		public string Body
		{
			get { return this._Body; }
			set
			{
				if (this._Body != value)
				{
					this._Body = value;
					this.OnPropertyChanged();
				}
			}
		}

		#endregion

		#region FontFamily 変更通知プロパティ

		public string FontFamily => this._visual.FontFamily;

		#endregion

		#region FontSize 変更通知プロパティ

		public int HeaderFontSize => this._visual.HeaderFontSize;

		public int BodyFontSize => this._visual.BodyFontSize;

		#endregion

		#region Margin 変更通知プロパティ

		public string HeaderMargin => this._visual.HeaderMargin;

		public string BodyMargin => this._visual.BodyMargin;

		#endregion

		#region Visibility 変更通知プロパティ

		public Visibility HeaderVisibility => string.IsNullOrEmpty(this.Header) ? Visibility.Collapsed : Visibility.Visible;

		public Visibility BodyVisibility => Visibility.Visible;

		#endregion

		#region Alignment 変更通知プロパティ

		public string HeaderAlignment => this._visual.HeaderAlignment.ToString();

		public string BodyAlignment => this._visual.BodyAlignment.ToString();

		#endregion

		#region WindowMinSize 変更通知プロパティ

		public int WindowMinWidth => this._visual.SimpleNotification ? this._visual.SimpleNotificationMinWidth : this._visual.NotificationMinWidth;

		public int PinWindowMinWidth => this._visual.PinWindowMinWidth;

		public int WindowMinHeight => this._visual.NotificationMinHeight;

		#endregion
	}
}
