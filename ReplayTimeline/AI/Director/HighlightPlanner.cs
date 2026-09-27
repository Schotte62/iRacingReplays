using iRacingReplayDirector.AI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace iRacingReplayDirector.AI.Director
{
	public class HighlightInterval
	{
		public int StartFrame { get; set; }
		public int EndFrame { get; set; }
		public int DurationFrames => EndFrame - StartFrame;
	}

	/// <summary>Creates ordered, non-overlapping replay ranges; frame intervals are [start, end).</summary>
	public static class HighlightPlanner
	{
		public static List<HighlightInterval> Build(ReplayScanResult scan, int targetSeconds,
			int framesPerSecond = 60, int leadSeconds = 8, int tailSeconds = 6,
			int openingSeconds = 12, int finishSeconds = 12)
		{
			if (scan == null) throw new ArgumentNullException(nameof(scan));
			if (scan.EndFrame <= scan.StartFrame) throw new ArgumentException("Invalid replay frame range.", nameof(scan));
			if (targetSeconds <= 0 || framesPerSecond <= 0 || leadSeconds < 0 || tailSeconds < 0 ||
				openingSeconds < 0 || finishSeconds < 0)
				throw new ArgumentOutOfRangeException(nameof(targetSeconds), "Durations and frame rate must be valid.");

			long budget = Math.Min((long)scan.TotalFrames, (long)targetSeconds * framesPerSecond);
			var chosen = new List<HighlightInterval>();
			// Reserve an opening and ending even when no incidents or overtakes were detected.
			int openingFrames = (int)Math.Min((long)openingSeconds * framesPerSecond, (budget + 1) / 2);
			int finishFrames = (int)Math.Min((long)finishSeconds * framesPerSecond, budget - openingFrames);
			AddWithinBudget(chosen, scan.StartFrame, scan.StartFrame + openingFrames, budget);
			AddWithinBudget(chosen, scan.EndFrame - finishFrames, scan.EndFrame, budget);

			foreach (var raceEvent in (scan.Events ?? new List<RaceEvent>())
				.Where(e => e != null && e.Frame >= scan.StartFrame && e.Frame < scan.EndFrame)
				.OrderByDescending(e => e.ImportanceScore).ThenBy(e => e.Frame))
			{
				int start = Math.Max(scan.StartFrame, raceEvent.Frame - leadSeconds * framesPerSecond);
				int end = (int)Math.Min(scan.EndFrame,
					(long)raceEvent.Frame + Math.Max(0, raceEvent.DurationFrames) + (long)tailSeconds * framesPerSecond);
				AddWithinBudget(chosen, start, end, budget);
			}

			return Merge(chosen);
		}

		private static void AddWithinBudget(List<HighlightInterval> chosen, int start, int end, long budget)
		{
			if (end <= start) return;
			long remaining = budget - Merge(chosen).Sum(x => (long)x.DurationFrames);
			if (remaining <= 0) return;
			var existing = Merge(chosen);
			int cursor = start;
			foreach (var interval in existing)
			{
				if (interval.EndFrame <= cursor) continue;
				if (interval.StartFrame >= end) break;
				if (cursor < interval.StartFrame)
					AddPiece(chosen, cursor, Math.Min(end, interval.StartFrame), ref remaining);
				cursor = Math.Max(cursor, interval.EndFrame);
				if (cursor >= end || remaining <= 0) return;
			}
			if (cursor < end) AddPiece(chosen, cursor, end, ref remaining);
		}

		private static void AddPiece(List<HighlightInterval> chosen, int start, int end, ref long remaining)
		{
			int length = (int)Math.Min((long)end - start, remaining);
			if (length <= 0) return;
			chosen.Add(new HighlightInterval { StartFrame = start, EndFrame = start + length });
			remaining -= length;
		}

		private static List<HighlightInterval> Merge(IEnumerable<HighlightInterval> ranges)
		{
			var result = new List<HighlightInterval>();
			foreach (var range in ranges.OrderBy(x => x.StartFrame))
			{
				var last = result.LastOrDefault();
				if (last != null && range.StartFrame <= last.EndFrame)
					last.EndFrame = Math.Max(last.EndFrame, range.EndFrame);
				else result.Add(new HighlightInterval { StartFrame = range.StartFrame, EndFrame = range.EndFrame });
			}
			return result;
		}
	}
}
