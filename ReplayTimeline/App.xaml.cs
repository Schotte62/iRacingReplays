using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace iRacingReplayDirector
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private static readonly object DiagnosticLock = new object();
		private readonly System.Collections.Generic.HashSet<string> _reportedUiErrors =
			new System.Collections.Generic.HashSet<string>();
		public static string DiagnosticPath => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"iRacingSequenceDirector", "diagnostics.log");

		public App()
		{
			DispatcherUnhandledException += OnDispatcherUnhandledException;
			AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
				LogDiagnostic("Unhandled application exception", args.ExceptionObject as Exception);
			TaskScheduler.UnobservedTaskException += (sender, args) =>
			{
				LogDiagnostic("Unobserved task exception", args.Exception);
				args.SetObserved();
			};
		}

		private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
		{
			args.Handled = true;
			string signature = args.Exception.GetType().FullName + args.Exception.StackTrace;
			if (!_reportedUiErrors.Add(signature)) return;
			LogDiagnostic("UI exception", args.Exception);
			MessageBox.Show("The application encountered an error. Details were saved to:\n" +
				DiagnosticPath, "Sequence Director Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		public static void LogDiagnostic(string message, Exception error = null)
		{
			try
			{
				lock (DiagnosticLock)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath));
					File.AppendAllText(DiagnosticPath, DateTime.Now.ToString("O") + " " + message +
						(error == null ? "" : "\r\n" + error) + "\r\n");
				}
			}
			catch { /* Diagnostics must never cause another crash. */ }
		}
	}
}
