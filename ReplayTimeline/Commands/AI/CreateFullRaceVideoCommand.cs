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
				App.LogDiagnostic("Create Full Race Video requested at replay frame " + _vm.CurrentFrame);
				var scan = await PrepareRaceCameraPlanAsync();
				App.LogDiagnostic($"Race scan complete: frames {scan.StartFrame} to {scan.EndFrame}; {scan.Events.Count} events");
				_vm.PrepareRaceRecording(scan.StartFrame, scan.EndFrame);
				_vm.StatusBarText = "Race camera plan ready. Click Record to start at the race session.";
			}
			catch (Exception ex)
			{
				App.LogDiagnostic("Create Full Race Video failed", ex);
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
				_vm.StatusBarText = "Finding race with iRacing session skip...";
				Sim.Instance.Sdk.Replay.Jump(iRSDKSharp.ReplaySearchModeTypes.ToStart);
				await Task.Delay(500);
				int raceStart = -1;
				int raceSession = -1;
				for (int session = 0; session < 32; session++)
				{
					int current = _vm.CurrentFrame;
					int sessionNum = Sim.Instance.Telemetry.SessionNum.Value;
					string type = Sim.Instance.SessionInfo["SessionInfo"]["Sessions"]
						["SessionNum", sessionNum]["SessionType"].GetValue("");
					if (IsRace(type)) { raceStart = current; raceSession = sessionNum; break; }
					Sim.Instance.Sdk.Replay.Jump(iRSDKSharp.ReplaySearchModeTypes.NextSession);
					bool moved = false;
					for (int attempt = 0; attempt < 30; attempt++)
					{
						await Task.Delay(100);
						if (_vm.CurrentFrame > current + 2) { moved = true; break; }
					}
					if (!moved) break;
				}
				if (raceStart < 0)
					throw new InvalidOperationException("iRacing session skip did not find a race. No recording was started.");
				App.LogDiagnostic($"iRacing race session {raceSession} begins at frame {raceStart}; replay ends at {endFrame}.");
				// The replay may contain post-race sessions; find their boundary
				// using the same session skip command as the existing UI.
				Sim.Instance.Sdk.Replay.Jump(iRSDKSharp.ReplaySearchModeTypes.NextSession);
				int raceEnd = endFrame;
				for (int attempt = 0; attempt < 30; attempt++)
				{
					await Task.Delay(100);
					if (_vm.CurrentFrame > raceStart + 2 && Sim.Instance.Telemetry.SessionNum.Value != raceSession)
					{
						raceEnd = _vm.CurrentFrame;
						break;
					}
				}
				if (raceEnd <= raceStart)
					throw new InvalidOperationException("Invalid race session boundaries. No recording was started.");
				_vm.StatusBarText = "Scanning race session for camera changes...";
				var scan = await _vm.AIDirector.ScanReplayAsync(raceStart, raceEnd);
				if (scan == null || scan.Snapshots.Count == 0)
					throw new InvalidOperationException("Replay scan did not return telemetry: " + _vm.AIDirector.StatusMessage + ". No recording was started.");
				App.LogDiagnostic($"Race scan samples: total {scan.Snapshots.Count}, replay end {endFrame}, sessions " +
					string.Join("; ", scan.Snapshots.GroupBy(s => new { s.SessionNum, s.SessionType })
						.Select(g => $"{g.Key.SessionNum}/{g.Key.SessionType}: {g.Count()} frames {g.Min(s => s.Frame)}-{g.Max(s => s.Frame)}")));
				scan.StartFrame = raceStart;
				scan.EndFrame = raceEnd;
				scan.SessionType = "Race";
				scan.DurationSeconds = (raceEnd - raceStart) / 60.0;
				scan.Snapshots = scan.Snapshots.Where(s => s.SessionNum == raceSession && s.Frame < raceEnd).ToList();
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
				if (!settled)
				{
					App.LogDiagnostic($"Race boundary refinement unavailable: requested {middle}, reached {_vm.CurrentFrame}; using race sample {firstRaceFrame}.");
					return firstRaceFrame;
				}
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
				if (!settled)
				{
					App.LogDiagnostic($"Race end refinement unavailable: requested {middle}, reached {_vm.CurrentFrame}; using last race sample {lastRaceFrame}.");
					return lastRaceFrame;
				}
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
