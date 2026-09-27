using System.Diagnostics;


namespace iRacingReplayDirector
{
	public class CaptureMode_OBS : CaptureModeBase
	{
		private Process _process;
		private string _recordHotkey = "^+(R)";
		private const string PauseResumeHotkey = "^+(P)";
		private bool _startedByDirector;
		private bool _pausedByDirector;

		public CaptureMode_OBS() : base()
		{
			Name = "OBS Studio";
			ProcessName = "obs64";
		}

		public override bool IsAvailable()
		{
			_process = ExternalProcessHelper.GetExternalProcess(ProcessName);

			CaptureModeAvailable = _process != null;
			CaptureAvailabilityMessage = CaptureModeAvailable ? "" : "Couldn't find OBS process, please ensure the application is running.";

			return CaptureModeAvailable;
		}

		public override bool IsReadyToRecord()
		{
			return IsAvailable();
		}

		public override void StartRecording()
		{
			if (_startedByDirector)
				throw new System.InvalidOperationException("OBS recording was already started by the director.");
			ToggleRecording();
			_startedByDirector = true;
			_pausedByDirector = false;
		}

		public override void StopRecording()
		{
			if (!_startedByDirector) return;
			ToggleRecording();
			_startedByDirector = false;
			_pausedByDirector = false;
		}

		/// <summary>Requires Ctrl+Shift+P for both Pause and Unpause Recording in OBS.</summary>
		public void PauseRecording()
		{
			if (!_startedByDirector || _pausedByDirector)
				throw new System.InvalidOperationException("OBS is not recording an active segment.");
			ExternalProcessHelper.SendToggleRecordHotkey(_process, PauseResumeHotkey);
			_pausedByDirector = true;
		}

		public void ResumeRecording()
		{
			if (!_startedByDirector || !_pausedByDirector)
				throw new System.InvalidOperationException("OBS recording is not paused by the director.");
			ExternalProcessHelper.SendToggleRecordHotkey(_process, PauseResumeHotkey);
			_pausedByDirector = false;
		}

		private void ToggleRecording()
		{
			ExternalProcessHelper.SendToggleRecordHotkey(_process, _recordHotkey);
		}
	}
}
