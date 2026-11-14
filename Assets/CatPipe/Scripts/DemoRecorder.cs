using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

namespace CatPipe
{
    // Autotest -record: films the game in real time for a demo video. Every frame is grabbed at the end of the frame
    // and written as a JPG on a worker thread (with its time stamp); the final audio mix is taped from the
    // AudioListener. Tools/make_video.py turns the folder into an MP4 with sound.
    public class DemoRecorder : MonoBehaviour
    {
        string dir;
        int frame, pending;
        bool on;
        float t0;
        readonly List<float> times = new List<float>();
        AudioTap tap;

        public void Begin(string folder)
        {
            dir = folder;
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            var listener = FindObjectOfType<AudioListener>();
            tap = listener.gameObject.AddComponent<AudioTap>();
            on = true;
            t0 = Time.realtimeSinceStartup;
            tap.Begin();
            StartCoroutine(Grab());
        }

        IEnumerator Grab()
        {
            var eof = new WaitForEndOfFrame();
            while (on)
            {
                yield return eof;
                if (!on) break;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] raw = tex.GetRawTextureData();
                int w = tex.width, h = tex.height;
                var fmt = tex.graphicsFormat;
                Destroy(tex);
                string path = Path.Combine(dir, $"f{frame:D6}.jpg");
                frame++;
                times.Add(Time.realtimeSinceStartup - t0);
                Interlocked.Increment(ref pending);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(raw, fmt, (uint)w, (uint)h, 0, 88)); }
                    finally { Interlocked.Decrement(ref pending); }
                });
            }
        }

        public IEnumerator End()
        {
            on = false;
            tap.End(Path.Combine(dir, "audio.wav"));
            while (Interlocked.CompareExchange(ref pending, 0, 0) > 0) yield return null;
            var lines = new List<string>();
            foreach (var t in times) lines.Add(t.ToString("0.0000", CultureInfo.InvariantCulture));
            File.WriteAllLines(Path.Combine(dir, "times.txt"), lines);
            Debug.Log($"RECORD: {frame} frames, {(times.Count > 0 ? times[times.Count - 1] : 0):0.0}s");
        }
    }

    // Copies the final mix (everything the listener hears) and writes it as a 16-bit WAV.
    public class AudioTap : MonoBehaviour
    {
        readonly List<float> buf = new List<float>(1 << 20);
        volatile bool rec;
        int channels = 2;

        public void Begin() { lock (buf) buf.Clear(); rec = true; }

        void OnAudioFilterRead(float[] data, int ch)
        {
            if (!rec) return;
            channels = ch;
            lock (buf) buf.AddRange(data);
        }

        public void End(string path)
        {
            rec = false;
            float[] d;
            lock (buf) d = buf.ToArray();
            int rate = AudioSettings.outputSampleRate;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = d.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var s in d) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
            }
        }
    }
}
