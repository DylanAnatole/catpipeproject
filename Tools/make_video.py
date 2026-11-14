"""Turns an autotest -record folder (JPG frames + times.txt + audio.wav) into an MP4 with sound.
Usage: python Tools/make_video.py <rec dir> <out.mp4> [fps=30]
Frames were grabbed in real time, so they are resampled to a constant frame rate by their time stamps
(each output frame shows the latest grabbed frame), which keeps the picture in sync with the audio."""
import os, subprocess, sys
import imageio.v2 as imageio
import imageio_ffmpeg

rec, out = sys.argv[1], sys.argv[2]
fps = int(sys.argv[3]) if len(sys.argv) > 3 else 30
times = [float(t) for t in open(os.path.join(rec, "times.txt")) if t.strip()]
frames = [os.path.join(rec, f"f{i:06d}.jpg") for i in range(len(times))]
silent = out + ".silent.mp4"
w = imageio.get_writer(silent, fps=fps, codec="libx264", quality=8, macro_block_size=8, ffmpeg_params=["-pix_fmt", "yuv420p"])
k, n, cache = 0, 0, (None, None)
total = times[-1]
while n / fps <= total:
    t = n / fps
    while k + 1 < len(times) and times[k + 1] <= t: k += 1
    if cache[0] != k: cache = (k, imageio.imread(frames[k]))
    w.append_data(cache[1])
    n += 1
w.close()
ff = imageio_ffmpeg.get_ffmpeg_exe()
subprocess.run([ff, "-y", "-loglevel", "error", "-i", silent, "-i", os.path.join(rec, "audio.wav"),
                "-c:v", "copy", "-c:a", "aac", "-b:a", "160k", "-shortest", out], check=True)
os.remove(silent)
print(f"{out}: {n} frames, {n / fps:.1f}s from {len(times)} grabbed")
