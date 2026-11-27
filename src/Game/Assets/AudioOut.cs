using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ShorelandsMods.Core.Areas;
using UnityEngine;
using UnityEngine.Networking;

namespace ShorelandsMods.Game.Assets
{
    // Clips from mod folders (the engine's own .ogg/.wav decoder, kept compressed in memory), one-shot voices in
    // groups, loops with fades, and the AudioListener the game does not have. All 2D; AudioListener.volume is the
    // game's master volume and scales everything.
    internal sealed class AudioOut : IAudioOut
    {
        internal sealed class Entry
        {
            public AudioClip Clip;
            public ClipStatus Status = ClipStatus.Loading;
            public string Problem;
        }

        internal sealed class LoopVoice
        {
            public AudioSource Source;
            public float Target,
                Rate;
            public bool Stopping;
        }

        readonly MonoBehaviour runner;
        readonly Transform root;
        internal readonly Dictionary<int, Entry> clips = new Dictionary<int, Entry>();
        internal readonly Dictionary<string, List<AudioSource>> groups = new Dictionary<string, List<AudioSource>>();
        internal readonly Dictionary<int, LoopVoice> loops = new Dictionary<int, LoopVoice>();
        int nextClip = 1,
            nextLoop = 1;
        internal AudioListener listener;
        float listenerCheckAt;

        public AudioOut(MonoBehaviour runner, Transform root)
        {
            this.runner = runner;
            this.root = root;
        }

        public int Load(string fullPath)
        {
            int id = nextClip++;
            var e = new Entry();
            clips[id] = e;
            runner.StartCoroutine(LoadClip(e, fullPath));
            return id;
        }

        IEnumerator LoadClip(Entry e, string path)
        {
            var type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                var dh = (DownloadHandlerAudioClip)req.downloadHandler;
                dh.streamAudio = false;
                dh.compressed = true;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    e.Status = ClipStatus.Failed;
                    e.Problem = "the game could not read it (" + req.error + ")";
                    yield break;
                }
                AudioClip clip = null;
                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(req);
                }
                catch (Exception ex)
                {
                    e.Problem = "the game could not decode it (" + ex.Message + ")";
                }
                if (e.Status != ClipStatus.Loading)
                {
                    // Unloaded while it loaded.
                    if (clip != null)
                        UnityEngine.Object.Destroy(clip);
                    yield break;
                }
                if (clip == null || clip.samples == 0)
                {
                    e.Status = ClipStatus.Failed;
                    e.Problem = e.Problem ?? "the game could not decode it";
                    if (clip != null)
                        UnityEngine.Object.Destroy(clip);
                    yield break;
                }
                clip.name = "mods:" + Path.GetFileName(path);
                e.Clip = clip;
                e.Status = ClipStatus.Ready;
            }
        }

        public ClipStatus Status(int clip, out string problem)
        {
            if (!clips.TryGetValue(clip, out var e))
            {
                problem = "it was unloaded";
                return ClipStatus.Failed;
            }
            problem = e.Problem;
            return e.Status;
        }

        public void Unload(int clip)
        {
            if (!clips.TryGetValue(clip, out var e))
                return;
            clips.Remove(clip);
            e.Status = ClipStatus.Failed;
            if (e.Clip != null)
                UnityEngine.Object.Destroy(e.Clip);
        }

        public void Play(int clip, double volume, string group, int voices)
        {
            if (!clips.TryGetValue(clip, out var e) || e.Clip == null)
                return;
            EnsureListener();
            if (!groups.TryGetValue(group, out var list))
                groups[group] = list = new List<AudioSource>();
            list.RemoveAll(s => s == null);
            AudioSource src = null;
            foreach (var s in list)
                if (!s.isPlaying)
                {
                    src = s;
                    break;
                }
            if (src == null && list.Count < voices)
            {
                src = NewSource(group);
                list.Add(src);
            }
            if (src == null)
            {
                src = list[0];
                foreach (var s in list)
                    if (s.time > src.time)
                        src = s;
                src.Stop();
            }
            src.clip = e.Clip;
            src.loop = false;
            src.volume = (float)volume;
            src.Play();
        }

        public void Stop(string group)
        {
            if (groups.TryGetValue(group, out var list))
                foreach (var s in list)
                    if (s != null)
                        s.Stop();
        }

        public void Drop(string group)
        {
            if (!groups.TryGetValue(group, out var list))
                return;
            groups.Remove(group);
            foreach (var s in list)
                if (s != null)
                    UnityEngine.Object.Destroy(s.gameObject);
        }

        public double MasterVolume => AudioListener.volume;

        public bool IsPlaying(string group) =>
            groups.TryGetValue(group, out var list) && list.Exists(s => s != null && s.isPlaying);

        public int StartLoop(int clip, double volume, double fadeSeconds)
        {
            int id = nextLoop++;
            if (!clips.TryGetValue(clip, out var e) || e.Clip == null)
                return id;
            EnsureListener();
            var src = NewSource("loop " + id);
            src.clip = e.Clip;
            src.loop = true;
            src.volume = fadeSeconds > 0 ? 0f : (float)volume;
            src.Play();
            loops[id] = new LoopVoice
            {
                Source = src,
                Target = (float)volume,
                Rate = fadeSeconds > 0 ? (float)(volume / fadeSeconds) : float.PositiveInfinity,
            };
            return id;
        }

        public void StopLoop(int loop, double fadeSeconds)
        {
            if (!loops.TryGetValue(loop, out var v))
                return;
            v.Stopping = true;
            v.Target = 0;
            v.Rate = fadeSeconds > 0 ? (float)(Math.Max(v.Source.volume, 0.01) / fadeSeconds) : float.PositiveInfinity;
            Tick(0);
        }

        // Every frame: fades.
        public void Tick(float dt)
        {
            if (loops.Count > 0)
                EnsureListener();
            List<int> done = null;
            foreach (var kv in loops)
            {
                var v = kv.Value;
                if (v.Source == null)
                {
                    (done = done ?? new List<int>()).Add(kv.Key);
                    continue;
                }
                v.Source.volume = Mathf.MoveTowards(v.Source.volume, v.Target, v.Rate * dt);
                if (v.Stopping && (v.Source.volume <= 0 || float.IsPositiveInfinity(v.Rate)))
                {
                    UnityEngine.Object.Destroy(v.Source.gameObject);
                    (done = done ?? new List<int>()).Add(kv.Key);
                }
            }
            if (done != null)
                foreach (var id in done)
                    loops.Remove(id);
        }

        AudioSource NewSource(string name)
        {
            var o = new GameObject(name);
            o.transform.SetParent(root, false);
            var s = o.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        // The game has no listener. One is added when none is enabled; ShorelandsBillboard removes its own when it
        // sees a second one, so with both installed this one stays.
        void EnsureListener()
        {
            if (listener != null || Time.unscaledTime < listenerCheckAt)
                return;
            listenerCheckAt = Time.unscaledTime + 1;
            foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l.enabled && l.gameObject.activeInHierarchy)
                    return;
            listener = root.gameObject.AddComponent<AudioListener>();
        }

        public int LoopCount => loops.Count;
    }
}
