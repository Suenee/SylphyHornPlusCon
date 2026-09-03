using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SylphyHorn.Services
{
	internal static class DesktopControlServiceDispatcherExtensions
	{
		// Action overload for owner-dispatcher commands that intentionally return no value.
		// DesktopControlService binds to the application Dispatcher, so this is the same owner
		// context used by its generic Func<T> dispatcher helper.
		internal static Task InvokeOnOwnerAsync(this DesktopControlService service, Action action)
		{
			if (service == null) throw new ArgumentNullException(nameof(service));
			if (action == null) throw new ArgumentNullException(nameof(action));
			var dispatcher = global::SylphyHorn.Application.Current?.Dispatcher
				?? throw new InvalidOperationException("The application Dispatcher is unavailable.");
			if (dispatcher.CheckAccess())
			{
				action();
				return Task.CompletedTask;
			}
			return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
		}
	}
}
