# Queue and animation tools

## Queue controls

- **Pause** lets the current queue item finish and prevents the next item from starting. **Pause and stop now** also cancels the current item. **Start / Resume** continues the queue immediately and clears any pending schedule.
- **Start at...** accepts today or tomorrow and a local 24-hour `HH:mm` time. The Queue header shows the due time and countdown. **Cancel schedule** disarms the timer and leaves the queue paused.
- Scheduling automatically saves the still-queued items. The scheduled time and pause state survive restart in `ClientSettings`; reopening the render window restores a scheduled queue, including an overdue start. BlendFarm must be running with its render window open to execute the schedule. An ordinary saved queue can still be restored with **Load**.
- **Edit** is available only before an item starts. It edits that item's independent settings snapshot, not the open project's settings. An edit is discarded if the item starts while its dialog is open. Use **Save** after editing an already-saved queue to persist the updated snapshot.
- Failed items offer **Retry**; **Retry all failed** re-queues every failed item. Start/resume the queue afterward. User-cancelled items are not treated as failures.
- **Total ETA** uses observed wall-clock render time per frame and the remaining frame count, including fractional progress. It displays `?` before a timing estimate is available. Mixed resolutions/engines may make this approximate.

## Frame selection and failure recovery

In **Frames**, **Step** renders every Nth frame within the inclusive start/end range. A non-empty **Frames** list, such as `1-50, 75, 100-120`, overrides start/end/step. Lists are sorted and deduplicated. Invalid entries display an inline error and prevent animation submission. These options persist with project settings and queue snapshots.

Both direct animation rendering and animation queue items use the exact selected frames, including single-frame animations. Skip-existing checks resolve each selected output filename individually.

After an unsuccessful animation, the activity log and animation progress panel identify missing frames, with failed-node names when known. Queue items also show the summary. **Re-render failed frames** runs only the missing frames, disables skip-existing, and retains the original run's timestamp and range tokens so the original filenames are reused. Automatically recovered attempts and user cancellation do not produce a failed-frame report.

## Optional MP4

Enable **Encode MP4 when done** and choose **MP4 FPS** (project FPS, or 24 when unspecified). FFmpeg is located on `PATH`, or through the optional executable-path field in **Frames**. Install FFmpeg from its official website and add its executable directory to `PATH` if it is missing.

After a successful animation, BlendFarm asynchronously encodes the actual ordered frame filenames, so sparse lists, `#` zero-padding and output naming tokens are supported. The result is an MP4 in the output folder, using the frame filename stem (or the project name for token-only patterns), H.264, `yuv420p`, CRF 18 and the chosen FPS. Odd dimensions are padded to even dimensions. Encoding uses FFmpeg's overwrite mode for an existing MP4 of the same name. Unsupported source formats or missing FFmpeg are logged without changing the render's successful status.

Frame-only recovery does not automatically encode another MP4. Once all frames exist, enable **Skip existing frames** and run the original selection with MP4 enabled to encode the completed sequence without rendering it again.

No server, wire-protocol or Blender Python-script changes are required.
