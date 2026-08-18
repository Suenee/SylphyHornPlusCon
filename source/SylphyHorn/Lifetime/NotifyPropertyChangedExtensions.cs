using System;
using System.ComponentModel;
using MetroTrilithon.Lifetime;

namespace SylphyHorn.Lifetime
{
	internal static class NotifyPropertyChangedExtensions
	{
		internal static IDisposable Subscribe(this INotifyPropertyChanged source, Action<string> action)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (action == null) throw new ArgumentNullException(nameof(action));

			PropertyChangedEventHandler handler = (sender, args) => action(args.PropertyName);
			source.PropertyChanged += handler;
			return Disposable.Create(() => source.PropertyChanged -= handler);
		}
	}
}
