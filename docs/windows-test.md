# Windows test build

This is an early test build for iRacing on Windows. It has not yet been tested
against a live simulator or OBS installation. Keep the existing installation.

1. Extract the entire `automated-video-windows-test` artifact to a new folder.
   All included DLL files must remain alongside `iRacingSequenceDirector.exe`.
2. Open a saved race replay in iRacing and pause playback.
3. Run `iRacingSequenceDirector.exe` from the extracted folder.
4. For Full Race, select **In-Sim Capture** or **OBS Studio** in Recording,
   then select **Auto Director → Create Full Race Video**.
5. For Highlights, set OBS hotkeys `Ctrl+Shift+R` for both start and stop and
   `Ctrl+Shift+P` for both pause and unpause; select **OBS Studio** in Recording.
   Enter the target duration under **Auto Director → Settings... → Video**,
   then select **Auto Director → Create Highlights Video (OBS)**.

Before testing, confirm that OBS is not already recording. Start with a short
replay. Inspect the first scene, the transitions and the end of the output
file. iRacing's in-sim recordings appear under Documents/iRacing/videos; OBS
uses the recording path set in its own settings. Report the output filename,
the first and last visible replay moments, and any error shown by the program.
