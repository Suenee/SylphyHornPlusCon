using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetroTrilithon.Linq;
using SylphyHorn.Interop;
using SylphyHorn.Properties;

namespace SylphyHorn
{
	partial class Application
	{
		public static CommandLineArgs Args { get; private set; }

		public new static Application Current => (Application)System.Windows.Application.Current;

		static Application()
		{
			AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
		}

		private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
		{
			if ((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMinutes >= 3)
			{
				// 3 分以上生きてたら安定稼働と見做して再起動させる
				Restart();
			}
			else
			{
				ReportException("AppDomain", sender, args.ExceptionObject as Exception);
			}
		}

		private static void Restart()
		{
			var startupScheduler = new StartupScheduler();
			if (startupScheduler.IsRunning)
			{
				startupScheduler.Restart();
				return;
			}

			if (Args != null)
			{
				var restartCount = Args.Restarted ?? 0;

				Process.Start(
					Environment.GetCommandLineArgs()[0],
					Args.Options
						.Where(x => x.Key != Args.GetKey(nameof(CommandLineArgs.Restarted)))
						.Concat(EnumerableEx.Return(Args.CreateOption(nameof(CommandLineArgs.Restarted), (restartCount + 1).ToString())))
						.Select(x => x.ToString())
						.JoinString(" "));
			}
		}


		private static void ReportException(string caller, object sender, Exception exception)
		{
			try
			{
				var now = DateTimeOffset.Now;
				var path = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					ProductInfo.Company,
					ProductInfo.Product,
					"ErrorReports",
					$"ErrorReport-{now:yyyyMMdd-HHmmss}-{now.Millisecond:000}.log");

				var message = $@"*** Error Report ({caller}) ***
{ProductInfo.Product} ver.{ProductInfo.VersionString}
{now}

{new SystemEnvironment()}

Sender:    {(sender is Type t ? t : sender?.GetType())?.FullName}
Exception: {exception?.GetType().FullName}

{exception}
";
				// ReSharper disable once AssignNullToNotNullAttribute
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.AppendAllText(path, message);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex);
			}

			Current.BeginShutdown();
		}
	}
}
