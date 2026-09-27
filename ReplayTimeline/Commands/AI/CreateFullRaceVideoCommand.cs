using System;
using System.Windows;
using System.Windows.Input;

namespace iRacingReplayDirector
{
	/// <summary>Scans and directs a complete loaded replay, then starts bounded capture.</summary>
	public class CreateFullRaceVideoCommand : ICommand
	{
		private readonly ReplayDirectorVM _vm;
		private bool _working;

		public CreateFullRaceVideoCommand(ReplayDirectorVM vm) { _vm = vm; }

		public event EventHandler CanExecuteChanged
		{
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public bool CanExecute(object parameter)
		{
			return !_working && _vm.IsSessionReady() && _vm.FinalFrame > 0 && !_vm.IsBoundedRecordingPending &&
				!_vm.PlaybackEnabled && !_vm.IsCaptureActive() &&
				_vm.AIDirector != null && !_vm.AIDirector.IsBusy &&
				_vm.SelectedCaptureMode != null && _vm.SelectedCaptureMode.IsReadyToRecord();
		}

		public async void Execute(object parameter)
		{
			if (!CanExecute(parameter)) return;
			_working = true;
			CommandManager.InvalidateRequerySuggested();
			try
			{
				int endFrame = _vm.FinalFrame;
				_vm.StatusBarText = "Scanning full replay for camera changes...";
				var scan = await _vm.AIDirector.ScanReplayAsync(0, endFrame);
				if (scan == null || scan.Snapshots.Count == 0)
					throw new InvalidOperationException("Replay scan did not return telemetry. No recording was started.");
				if (scan.SessionType == null || scan.SessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) < 0)
					throw new InvalidOperationException("The current replay session is not a race. No recording was started.");
				if (!_vm.IsSessionReady() || _vm.FinalFrame < endFrame)
					throw new InvalidOperationException("Replay changed during scan. No recording was started.");

				_vm.StatusBarText = "Generating camera plan...";
				var plan = await _vm.AIDirector.GenerateCameraPlanAsync();
				if (plan == null || plan.CameraActions.Count == 0)
					throw new InvalidOperationException("No camera actions were generated. No recording was started.");
				int applied = _vm.AIDirector.ApplyPlanToNodeCollection();
				if (applied == 0)
					throw new InvalidOperationException("No camera nodes could be applied. No recording was started.");

				_vm.StartBoundedRecording(0, endFrame);
				_vm.StatusBarText = "Full replay recording started; capture stops at replay end.";
			}
			catch (Exception ex)
			{
				_vm.StatusBarText = "Full replay recording failed: " + ex.Message;
				MessageBox.Show(ex.Message, "Create Full Race Video", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				_working = false;
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}
}
