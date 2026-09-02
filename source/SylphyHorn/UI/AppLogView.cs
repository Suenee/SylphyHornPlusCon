using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MetroTrilithon.Threading.Tasks;
using Microsoft.Win32;
using SylphyHorn.Serialization;
using SylphyHorn.Services;

namespace SylphyHorn.UI
{
	internal sealed class AppLogView : Grid, IDisposable
	{
		private readonly ObservableCollection<LogRow> _rows = new ObservableCollection<LogRow>();
		private readonly List<LogEntry> _entries = new List<LogEntry>();
		private readonly DataGrid _grid;
		private readonly TextBox _search;
		private readonly ComboBox _mode;
		private readonly CheckBox _debug;
		private readonly CheckBox _info;
		private readonly CheckBox _warning;
		private readonly CheckBox _error;
		private readonly CheckBox _tail;
		private readonly TextBlock _detailHeader;
		private readonly TextBox _detailMessage;
		private readonly TextBox _detailDetails;
		private readonly TextBlock _path;
		private readonly IDisposable _subscription;

		internal AppLogView()
		{
			this.Margin = new Thickness(4);
			this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
			this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			var top = new Grid { Margin = new Thickness(8, 8, 8, 6) };
			top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
			top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
			top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			var modeLabel = Label("Logging:");
			Grid.SetColumn(modeLabel, 0);
			top.Children.Add(modeLabel);
			this._mode = new ComboBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
			this._mode.Items.Add("Off");
			this._mode.Items.Add("Single");
			this._mode.Items.Add("All");
			this._mode.SelectedIndex = LoggingService.Instance.Mode == LogMode.Off ? 0 : LoggingService.Instance.Mode == LogMode.All ? 2 : 1;
			this._mode.SelectionChanged += this.ModeChanged;
			Grid.SetColumn(this._mode, 1);
			top.Children.Add(this._mode);

			this._search = new TextBox { Height = 28, MinWidth = 220, Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
			this._search.ToolTip = "Search time, level, service, object, event, message and details";
			this._search.TextChanged += (_, _) => this.RefreshRows();
			Grid.SetColumn(this._search, 3);
			top.Children.Add(this._search);
			var searchHint = new TextBlock { Text = "Search", Foreground = new SolidColorBrush(Color.FromRgb(150, 157, 166)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsHitTestVisible = false };
			searchHint.SetBinding(VisibilityProperty, new Binding("Text.Length") { Source = this._search, Converter = new EmptyTextVisibilityConverter() });
			Grid.SetColumn(searchHint, 3);
			top.Children.Add(searchHint);

			var clearSearch = new Button { Content = "×", Width = 28, Height = 28, Margin = new Thickness(5, 0, 0, 0), ToolTip = "Clear search" };
			clearSearch.Click += (_, _) => this._search.Clear();
			Grid.SetColumn(clearSearch, 4);
			top.Children.Add(clearSearch);
			Grid.SetRow(top, 0);
			this.Children.Add(top);

			var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 8) };
			filters.Children.Add(new TextBlock { Text = "Levels:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
			this._debug = Filter("Debug", true);
			this._info = Filter("Info", true);
			this._warning = Filter("Warning", true);
			this._error = Filter("Error", true);
			filters.Children.Add(this._debug);
			filters.Children.Add(this._info);
			filters.Children.Add(this._warning);
			filters.Children.Add(this._error);
			this._tail = Filter("Always at end", true);
			this._tail.Margin = new Thickness(24, 0, 0, 0);
			filters.Children.Add(this._tail);
			Grid.SetRow(filters, 1);
			this.Children.Add(filters);

			this._grid = new DataGrid
			{
				ItemsSource = this._rows,
				AutoGenerateColumns = false,
				IsReadOnly = true,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				HeadersVisibility = DataGridHeadersVisibility.Column,
				SelectionMode = DataGridSelectionMode.Single,
				SelectionUnit = DataGridSelectionUnit.FullRow,
				GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
				Margin = new Thickness(8, 0, 8, 8),
				FontFamily = new FontFamily("Consolas"),
				FontSize = 12,
			};
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Timecode", Binding = new Binding(nameof(LogRow.Time)), Width = 170 });
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new Binding(nameof(LogRow.Level)), Width = 75 });
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Service", Binding = new Binding(nameof(LogRow.Service)), Width = 90 });
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Object", Binding = new Binding(nameof(LogRow.ObjectId)), Width = 110 });
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Event", Binding = new Binding(nameof(LogRow.Event)), Width = 150 });
			this._grid.Columns.Add(new DataGridTextColumn { Header = "Message", Binding = new Binding(nameof(LogRow.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
			this._grid.SelectionChanged += (_, _) => this.ShowSelectedDetail();
			Grid.SetRow(this._grid, 2);
			this.Children.Add(this._grid);

			var detail = new Grid { Margin = new Thickness(8, 0, 8, 8) };
			detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
			detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			this._detailHeader = new TextBlock { Text = "Select a log entry to view details.", Foreground = new SolidColorBrush(Color.FromRgb(190, 196, 204)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
			Grid.SetRow(this._detailHeader, 0);
			detail.Children.Add(this._detailHeader);
			this._detailMessage = DetailBox();
			Grid.SetRow(this._detailMessage, 1);
			detail.Children.Add(this._detailMessage);
			this._detailDetails = DetailBox();
			this._detailDetails.FontFamily = new FontFamily("Consolas");
			Grid.SetRow(this._detailDetails, 2);
			detail.Children.Add(this._detailDetails);
			Grid.SetRow(detail, 3);
			this.Children.Add(detail);

			var bottom = new Grid { Margin = new Thickness(8, 0, 8, 8) };
			bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			this._path = new TextBlock { Text = LoggingService.Instance.LogPath ?? "Persistent log is initialized after settings load.", Foreground = new SolidColorBrush(Color.FromRgb(145, 153, 163)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
			Grid.SetColumn(this._path, 0);
			bottom.Children.Add(this._path);
			var buttons = new StackPanel { Orientation = Orientation.Horizontal };
			var export = new Button { Content = "Export", MinWidth = 78, Height = 28, Margin = new Thickness(0, 0, 6, 0) };
			export.Click += (_, _) => this.Export();
			var clear = new Button { Content = "Clear", MinWidth = 78, Height = 28 };
			clear.Click += (_, _) => this.Clear();
			buttons.Children.Add(export);
			buttons.Children.Add(clear);
			Grid.SetColumn(buttons, 1);
			bottom.Children.Add(buttons);
			Grid.SetRow(bottom, 4);
			this.Children.Add(bottom);

			this._subscription = LoggingService.Instance.Subscribe(
				snapshot => this.Dispatcher.BeginInvoke((Action)(() => { this._entries.AddRange(snapshot); this.RefreshRows(); })),
				entry => this.Dispatcher.BeginInvoke((Action)(() => { this._entries.Add(entry); this.RefreshRows(); })));
		}

		private static TextBlock Label(string text) => new TextBlock { Text = text, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

		private CheckBox Filter(string text, bool value)
		{
			var check = new CheckBox { Content = text, IsChecked = value, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
			check.Checked += (_, _) => this.RefreshRows();
			check.Unchecked += (_, _) => this.RefreshRows();
			return check;
		}

		private static TextBox DetailBox() => new TextBox
		{
			IsReadOnly = true,
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Margin = new Thickness(0, 0, 0, 5),
		};

		private void RefreshRows()
		{
			if (this._grid == null) return;
			var query = this._search?.Text?.Trim();
			var selectedSequence = (this._grid.SelectedItem as LogRow)?.Sequence;
			var filtered = this._entries.Where(this.IsVisible).Where(entry => Matches(entry, query)).Select(LogRow.From).ToArray();
			this._rows.Clear();
			foreach (var row in filtered) this._rows.Add(row);
			if (selectedSequence.HasValue)
			{
				this._grid.SelectedItem = this._rows.FirstOrDefault(x => x.Sequence == selectedSequence.Value);
			}
			if (this._tail?.IsChecked == true && this._rows.Count > 0)
			{
				var last = this._rows[this._rows.Count - 1];
				this._grid.SelectedItem = last;
				this._grid.ScrollIntoView(last);
			}
		}

		private bool IsVisible(LogEntry entry)
		{
			return entry.Level switch
			{
				LogLevel.Debug => this._debug?.IsChecked == true,
				LogLevel.Info => this._info?.IsChecked == true,
				LogLevel.Warning => this._warning?.IsChecked == true,
				LogLevel.Error => this._error?.IsChecked == true,
				_ => true,
			};
		}

		private static bool Matches(LogEntry entry, string query)
		{
			if (string.IsNullOrWhiteSpace(query)) return true;
			return new[]
			{
				entry.Log.DateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff"),
				entry.Level.ToString(), entry.Service, entry.ObjectId, entry.Event, entry.Log.Content, entry.Details,
			}.Any(value => !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		private void ShowSelectedDetail()
		{
			if (this._grid.SelectedItem is not LogRow row)
			{
				this._detailHeader.Text = "Select a log entry to view details.";
				this._detailMessage.Clear();
				this._detailDetails.Clear();
				return;
			}
			this._detailHeader.Text = $"#{row.Sequence}  {row.Time}  {row.Level}  {row.Service}  {row.Event}" + (string.IsNullOrWhiteSpace(row.ObjectId) ? string.Empty : $"  [{row.ObjectId}]");
			this._detailMessage.Text = row.FullMessage;
			this._detailDetails.Text = row.Details ?? string.Empty;
		}

		private void ModeChanged(object sender, SelectionChangedEventArgs e)
		{
			if (this._mode.SelectedItem is not string selected) return;
			var mode = string.Equals(selected, "Off", StringComparison.OrdinalIgnoreCase) ? LogMode.Off : string.Equals(selected, "All", StringComparison.OrdinalIgnoreCase) ? LogMode.All : LogMode.Single;
			LoggingService.Instance.SetMode(mode);
			Settings.General.LoggingMode.Value = selected.ToLowerInvariant();
			LocalSettingsProvider.Instance.SaveAsync().Forget();
			this._path.Text = mode == LogMode.Off ? "Persistent file logging is off." : LoggingService.Instance.LogPath;
			LoggingService.Instance.Write(LogLevel.Info, "SETTINGS", "LoggingModeChanged", $"Logging mode changed to {selected}.");
		}

		private void Export()
		{
			var dialog = new SaveFileDialog
			{
				Title = "Export application log",
				Filter = "Text files (*.txt)|*.txt",
				DefaultExt = ".txt",
				FileName = $"sylphyhorn-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
			};
			if (dialog.ShowDialog() != true) return;
			try
			{
				var visible = this._rows.Select(row => this._entries.First(entry => entry.Sequence == row.Sequence)).ToArray();
				LoggingService.Instance.ExportText(dialog.FileName, visible);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Export application log", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void Clear()
		{
			if (MessageBox.Show("Clear the application log?", "App log", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
			LoggingService.Instance.Clear();
			this._entries.Clear();
			this._rows.Clear();
			this.ShowSelectedDetail();
		}

		public void Dispose()
		{
			this._subscription?.Dispose();
		}

		private sealed class LogRow
		{
			public long Sequence { get; init; }
			public string Time { get; init; }
			public string Level { get; init; }
			public string Service { get; init; }
			public string ObjectId { get; init; }
			public string Event { get; init; }
			public string Message { get; init; }
			public string FullMessage { get; init; }
			public string Details { get; init; }

			public static LogRow From(LogEntry entry)
			{
				var message = entry.Log.Content ?? string.Empty;
				var firstLine = message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').FirstOrDefault() ?? string.Empty;
				return new LogRow
				{
					Sequence = entry.Sequence,
					Time = entry.Log.DateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss.fff"),
					Level = entry.Level.ToString().ToUpperInvariant(),
					Service = entry.Service,
					ObjectId = entry.ObjectId ?? string.Empty,
					Event = entry.Event,
					Message = firstLine,
					FullMessage = message,
					Details = entry.Details,
				};
			}
		}

		private sealed class EmptyTextVisibilityConverter : System.Windows.Data.IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
			{
				return value is int length && length == 0 ? Visibility.Visible : Visibility.Collapsed;
			}
			public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
		}
	}
}
