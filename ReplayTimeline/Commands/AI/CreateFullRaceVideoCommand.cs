using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using iRacingSimulator;
using iRacingReplayDirector.AI.Models;

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
			return !_working && !_vm.VideoPreparationBusy && _vm.IsSessionReady() && _vm.FinalFrame > 0 && !_vm.IsBoundedRecordingPending &&
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
				var scan = await PrepareRaceCameraPlanAsync();
				_vm.StartBoundedRecording(scan.StartFrame, scan.EndFrame);
				_vm.StatusBarText = "Race recording started at the race session; capture stops at its end.";
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

		public async Task<ReplayScanResult> PrepareRaceCameraPlanAsync()
		{
			if (_vm.VideoPreparationBusy)
				throw new InvalidOperationException("Another video preparation is in progress.");
			_vm.VideoPreparationBusy = true;
			CommandManager.InvalidateRequerySuggested();
			try
			{
				int endFrame = _vm.FinalFrame;
				_vm.StatusBarText = "Scanning full replay for camera changes...";
				var scan = await _vm.AIDirector.ScanReplayAsync(0, endFrame);
				if (scan == null || scan.Snapshots.Count == 0)
					throw new InvalidOperationException("Replay scan did not return telemetry. No recording was started.");
				var raceSnapshots = scan.Snapshots.Where(s => IsRace(s.SessionType)).OrderBy(s => s.Frame).ToList();
				if (raceSnapshots.Count == 0)
					throw new InvalidOperationException("No race session was found in this replay. No recording was started.");
				int firstRaceSample = raceSnapshots[0].Frame;
				int previousSample = scan.Snapshots.Where(s => s.Frame < firstRaceSample)
					.Select(s => s.Frame).DefaultIfEmpty(0).Max();
				int raceStart = await FindRaceStartAsync(previousSample, firstRaceSample);
				int raceSession = raceSnapshots[0].SessionNum;
				int firstLaterSession = scan.Snapshots
					.Where(s => s.Frame > firstRaceSample && s.SessionNum != raceSession)
					.Select(s => s.Frame).DefaultIfEmpty(endFrame).Min();
				int raceEnd = firstLaterSession == endFrame ? endFrame :
					await FindRaceEndAsync(raceSnapshots.Where(s => s.Frame < firstLaterSession).Max(s => s.Frame), firstLaterSession);
				if (raceEnd <= raceStart)
					throw new InvalidOperationException("Could not determine the race frame range. No recording was started.");
				scan.StartFrame = raceStart;
				scan.EndFrame = raceEnd;
				scan.SessionType = "Race";
				scan.DurationSeconds = (raceEnd - raceStart) / 60.0;
				scan.Snapshots = raceSnapshots.Where(s => s.SessionNum == raceSession && s.Frame < raceEnd).ToList();
				scan.Events = scan.Events.Where(e => e.Frame >= raceStart && e.Frame < raceEnd).ToList();
				if (!_vm.IsSessionReady() || _vm.FinalFrame < endFrame)
					throw new InvalidOperationException("Replay changed during scan. No recording was started.");

				_vm.StatusBarText = "Generating camera plan...";
				var plan = await _vm.AIDirector.GenerateCameraPlanAsync();
				if (plan == null || plan.CameraActions.Count == 0)
					throw new InvalidOperationException("No camera actions were generated. No recording was started.");
				int applied = _vm.AIDirector.ApplyPlanToNodeCollection();
				if (applied == 0)
					throw new InvalidOperationException("No camera nodes could be applied. No recording was started.");

				return scan;
			}
			finally
			{
				_vm.VideoPreparationBusy = false;
				CommandManager.InvalidateRequerySuggested();
			}
		}

		private static bool IsRace(string sessionType)
		{
			return sessionType != null && sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private async Task<int> FindRaceStartAsync(int previousFrame, int firstRaceFrame)
		{
			if (firstRaceFrame == 0) return 0;
			// Refine the one-second scan interval so the formation lap is not cut short.
			int low = previousFrame;
			int high = firstRaceFrame;
			while (high - low > 1)
			{
				int middle = low + (high - low) / 2;
				Sim.Instance.Sdk.Replay.SetPosition(middle);
				bool settled = false;
				for (int attempt = 0; attempt < 12; attempt++)
				{
					await Task.Delay(50);
					if (Math.Abs((long)_vm.CurrentFrame - middle) <= 2) { settled = true; break; }
				}
				if (!settled) throw new InvalidOperationException("Replay did not seek to the race boundary. No recording was started.");
				int sessionNum = Sim.Instance.Telemetry.SessionNum.Value;
				string type = Sim.Instance.SessionInfo["SessionInfo"]["Sessions"]
					["SessionNum", sessionNum]["SessionType"].GetValue("");
				if (IsRace(type)) high = middle;
				else low = middle;
			}
			return high;
		}

		private async Task<int> FindRaceEndAsync(int lastRaceFrame, int firstLaterFrame)
		{
			int low = lastRaceFrame;
			int high = firstLaterFrame;
			while (high - low > 1)
			{
				int middle = low + (high - low) / 2;
				Sim.Instance.Sdk.Replay.SetPosition(middle);
				bool settled = false;
				for (int attempt = 0; attempt < 12; attempt++)
				{
					await Task.Delay(50);
					if (Math.Abs((long)_vm.CurrentFrame - middle) <= 2) { settled = true; break; }
				}
				if (!settled) throw new InvalidOperationException("Replay did not seek to the race end. No recording was started.");
				int sessionNum = Sim.Instance.Telemetry.SessionNum.Value;
				string type = Sim.Instance.SessionInfo["SessionInfo"]["Sessions"]
					["SessionNum", sessionNum]["SessionType"].GetValue("");
				if (IsRace(type)) low = middle;
				else high = middle;
			}
			return high;
		}
	}
}
