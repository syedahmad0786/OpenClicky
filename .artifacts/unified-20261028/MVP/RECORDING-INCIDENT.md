# Recording test-safety review

28 October 2026. Worker explicitly reported an unintended hardware-launch attempt in its initial negative validation test. The test called StartAsync with a screen rectangle and a malformed DirectShow endpoint before the new validation guard existed. The retained red-subparser.log proves the expected exception was not thrown; it does not preserve enough native stderr/frame history to prove that zero frames were captured.

Worker reports no completed/published media and failed launch. Parent process inventory after worker exit found no ffmpeg or ffprobe process. Those are separate claims. No personal source inspection or cloud upload was requested. The current tests still include real-launch validation-negative cases; accepting this module is blocked until synthetic test isolation is structural and lifecycle/output cleanup review passes.

Action: independent GPT-6.1 Sol high-reasoning safety review. No further recorder tests are being run by the parent until fixes are applied. No source-account credentials were involved. No native acceptance is inferred.

Parent inspected the one remaining owned test partial on disk: 36 bytes containing only an MP4 ftyp box, no mdat/media payload. This establishes that file contains no audio/video frames; it does not reconstruct all transient process activity or independently prove no buffers were captured before failure. The partial was not opened as personal media or uploaded.
