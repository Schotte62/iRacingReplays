using iRacingReplayDirector.AI.Director;
using System;
using System.Windows;
using System.Windows.Input;

namespace iRacingReplayDirector
{
	/// <summary>Creates an eight-minute highlights recording using OBS pause/resume.</summary>
	public class CreateHighlightsVideoCommand : ICommand
	{
		private readonly ReplayDirectorVM _vm;
		private bool _working;
		private const int TargetSeconds = 8 * 60;

		public CreateHighlightsVideoCommand(ReplayDirectorVM vm) { _vm = vm; }

		public event EventHandler CanExecuteChanged
		{
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public bool CanExecute(object parameter)
		{
			return !_working && !_vm.VideoPreparationBusy && _vm.IsSessionReady() &&
				_vm.FinalFrame > 0 && !_vm.PlaybackEnabled && !_vm.IsCaptureActive() &&
				!_vm.IsBoundedRecordingPending && _vm.AIDirector != null &&
				!_vm.AIDirector.IsBusy && _vm.SelectedCaptureMode is CaptureMode_OBS &&
				_vm.SelectedCaptureMode.IsReadyToRecord();
		}

		public async void Execute(object parameter)
		{
			if (!CanExecute(parameter)) return;
			_working = true;
			CommandManager.InvalidateRequerySuggested();
			try
			{
				var scan = await _vm.CreateFullRaceVideoCommand.PrepareRaceCameraPlanAsync();
				var intervals = HighlightPlanner.Build(scan, TargetSeconds);
				if (intervals.Count == 0)
					throw new InvalidOperationException("No highlight scenes were selected.");
				_vm.StartHighlightsRecording(intervals);
				_vm.StatusBarText = $"Recording {intervals.Count} highlight scenes in OBS.";
			}
			catch (Exception ex)
			{
				_vm.StatusBarText = "Highlight preparation failed: " + ex.Message;
				MessageBox.Show(ex.Message, "Create Highlights Video", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				_working = false;
				CommandManager.InvalidateRequerySuggested();
			}
		}
	}
}
