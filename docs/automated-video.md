# Automated video: implementation notes

## Goal

Load an iRacing replay, select **Full Race**, **Highlights**, or **Both**, and
produce finished video files without editing the timeline or operating the
recorder manually. Keep the existing camera director and make the LLM optional.

## Verified starting point

- `Commands/AI/ScanReplayCommand.cs` scans from the current frame to the final
  frame. A full-race workflow must explicitly seek to the race start first.
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
for that range. These components are not yet connected to a Create Video button;
no finished video is produced by this branch yet. The next step is a job
controller that records each interval and joins its files.

## Verification needed on the user's PC

The project targets .NET Framework 4.7.2/WPF and references iRacing SDK DLLs
outside this repository. Build and capture behavior require a Windows machine
with those dependencies, iRacing, and the selected recorder installed.
