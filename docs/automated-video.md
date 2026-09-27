# Automated video: implementation notes

## Goal

Load an iRacing replay, select **Full Race**, **Highlights**, or **Both**, and
produce finished video files without editing the timeline or operating the
recorder manually. Keep the existing camera director and make the LLM optional.

## Verified starting point

- `Commands/AI/ScanReplayCommand.cs` scans from the current frame to the final
  frame. The automatic full-race command scans the loaded replay to identify
  the Race session and refines its first frame before capture.
- `AIDirector.ApplyPlanToNodeCollection()` adds camera changes to the timeline.
- `ReplayDirectorVM.StartRecording()` starts the selected capture mode and sets
  replay speed to 1. `StopRecording()` pauses playback and stops capture.
- `FrameSkipNode.ApplyNode()` seeks to the next node **while playback continues**.
  Recording through this seek may produce a visible discontinuity; a skip node
  alone is not a validated way to deliver a finished highlights video.
- The current "stop on final node" behavior stops at the last camera action,
  which can precede the actual race finish. An automated full-race job needs an
  explicit end frame instead.

## Implementation sequence

1. Define race start/end and highlight intervals from scan results. Preserve
   camera coverage across each interval and handle races with no detected events.
2. Build a job controller for scan, plan, capture, and completion; prevent
   concurrent jobs and restore replay/UI state after failure or cancellation.
3. Record Full Race through the race end frame. Record Highlights as separate
   clips across seeks, then concatenate clips into one output video without
   recompression where compatible. For Both, deliver two separate files.
4. Validate file creation and playback on Windows with iRacing and the chosen
   capture mode. Document installation, settings, and a one-button workflow.

## Current implementation

`AI/Director/HighlightPlanner.cs` produces ordered, non-overlapping replay
intervals from a scan. It reserves an opening and finish, then adds detected
events by importance with configurable lead and tail. Its output is capped by
the requested duration. `ReplayDirectorVM.StartBoundedRecording(startFrame,
endFrame)` can seek to a range, start the selected recorder once the replay
reaches its start, and stop at its end. The normal "final node" stop is bypassed
for that range. At each range start it selects the camera that was active at
that replay frame. `BoundedCaptureFinished` fires only when the planned end is
reached; manual stop does not report a successful clip. The **Auto Director → Create Full Race Video** menu command now
scans the loaded replay, finds the Race session rather than starting at frame 0,
generates and applies camera changes within that session, and starts bounded
capture using the selected capture mode. It requires a paused
race replay and an available recorder. In-Sim Capture and OBS still store files
in their own configured output folders. The command does not verify the video
file. The Race session boundary, including formation laps and standing starts,
must be checked against an actual replay on Windows before release.

### Start of the race

The original `MerlinCooper/iRacingReplayDirector` filters its telemetry feed
with `RaceOnly()`, then takes the first sample where `SessionState == Racing`
and backs up 20 seconds (`Phases/AnalyseRace.cs`). The new workflow uses the
telemetry `SessionNum` and its `SessionType` in iRacing session info to find
the first Race frame directly. This excludes Practice and Qualifying while
keeping the entire Race session, including any formation lap before the green
flag. The session transition and visible first frame need confirmation in an
actual replay; the two programs do not currently use identical start rules.

Highlights and Both are still under development: the next step is a job
controller that plays each interval, pauses OBS during seeks, and resumes into
the same output file. `CaptureMode_OBS` now exposes guarded PauseRecording and
ResumeRecording operations using Ctrl+Shift+P. Before this can work, **both**
Pause Recording and Unpause Recording must be assigned Ctrl+Shift+P in OBS.
The existing Start/Stop Recording hotkeys remain Ctrl+Shift+R. These new
operations are not yet called by a menu command. OBS reports no recording
state back to this application, so a real test must also check that OBS was
idle before starting and that pause/unpause actually happened.

## Verification needed on the user's PC

The project targets .NET Framework 4.7.2/WPF and references iRacing SDK DLLs
outside this repository. Build and capture behavior require a Windows machine
with those dependencies, iRacing, and the selected recorder installed.
