using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Format {
    public static class Pmws {
        public const string kApp = "printmov-web-synth";
        public const int kFormat = 2;
        const int PD_RES = 48;
        const int kResolution = 480;

        #region file model

        class PmwsVibrato {
            public double length;   // fraction of the note, 0-1
            public double rate;     // Hz
            public double depth;    // semitones
        }

        class PmwsNote {
            public int id;
            public double startBeat;
            public double lenBeat;
            public int midi;
            public string? lyric;
            public List<string>? phonemes;
            public PmwsVibrato? vib;
        }

        class PmwsAnchor {
            public double beat;
            public double midi;
            public string? shape;
            public bool main;
            public int noteId;
            public string? role;
            public double? dx;
            public bool hidden;
        }

        class PmwsLayers {
            public Dictionary<string, double>? preset;
            public Dictionary<string, double>? auto;
        }

        class PmwsProject {
            public string? app;
            public int version;
            public string? name;
            public double tempo;
            public string? singer;
            public List<PmwsNote>? notes;
            public PmwsLayers? pitchLayers;
            public List<PmwsAnchor>? anchors;
        }

        #endregion

        public static bool IsPmws(string contents) {
            return contents.Contains("\"" + kApp + "\"");
        }

        #region load

        public static UProject Load(string filePath) {
            var file = JsonConvert.DeserializeObject<PmwsProject>(
                File.ReadAllText(filePath, Encoding.UTF8));
            if (file == null || file.notes == null || file.notes.Count == 0) {
                throw new FileFormatException("No notes in pmws file.");
            }
            double bpm = file.tempo > 0 ? file.tempo : 120;
            double msPerBeat = 60000.0 / bpm;

            UProject project = Ustx.Create();
            project.name = string.IsNullOrEmpty(file.name)
                ? Path.GetFileNameWithoutExtension(filePath) : file.name!;
            project.tempos = new List<UTempo> { new UTempo(0, bpm) };
            project.timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };
            project.printmov = new UPrintmovOrigin { app = kApp, format = file.version };

            var track = new UTrack(project) { TrackNo = 0 };
            project.tracks.Add(track);
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var anchorsByNote = (file.anchors ?? new List<PmwsAnchor>())
                .Where(a => !a.hidden)
                .GroupBy(a => a.noteId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.beat).ToList());

            foreach (var n in file.notes) {
                var note = project.CreateNote(
                    n.midi,
                    (int)Math.Round(n.startBeat * kResolution),
                    Math.Max(10, (int)Math.Round(n.lenBeat * kResolution)));
                note.lyric = string.IsNullOrEmpty(n.lyric) ? "a" : n.lyric!;
                if (n.vib != null && n.vib.length > 0 && n.vib.depth > 0 && n.vib.rate > 0) {
                    note.vibrato.length = (float)Math.Clamp(n.vib.length * 100, 0, 100);
                    note.vibrato.period = (float)Math.Clamp(1000 / n.vib.rate, 5, 500);
                    note.vibrato.depth = (float)Math.Clamp(n.vib.depth * 100, 5, 200);
                } else {
                    note.vibrato.length = 0;
                }
                if (anchorsByNote.TryGetValue(n.id, out var pts) && pts.Count > 0) {
                    note.pitch.snapFirst = false;
                    note.pitch.data = pts.Select(a => new PitchPoint(
                        (float)((a.beat - n.startBeat) * msPerBeat),
                        (float)((a.midi - n.midi) * 10),
                        ParseShape(a.shape))).ToList();
                }
                if (n.phonemes != null && n.phonemes.Count > 0) {
                    for (int i = 0; i < n.phonemes.Count; i++) {
                        note.phonemeOverrides.Add(new UPhonemeOverride {
                            index = i,
                            phoneme = n.phonemes[i],
                        });
                    }
                }
                part.notes.Add(note);
            }

            var pitd = MergeLayers(file.pitchLayers);
            if (pitd.Count > 0 && project.expressions.TryGetValue(Ustx.PITD, out var descriptor)) {
                var curve = new UCurve(descriptor);
                foreach (var key in pitd.Keys.OrderBy(k => k)) {
                    curve.xs.Add((int)Math.Round((double)key / PD_RES * kResolution));
                    curve.ys.Add((int)Math.Round(pitd[key] * 100));
                }
                part.curves.Add(curve);
            }

            part.name = project.name;
            part.Duration = Math.Max(kResolution * 4, part.GetMinDurTick(project));
            project.FilePath = filePath;
            project.Saved = false;   // .pmws is not a ustx; force Save As
            project.AfterLoad();
            project.ValidateFull();
            if (!string.IsNullOrEmpty(file.singer)) {
                Log.Information($"pmws singer '{file.singer}' — not mapped to an OpenUtau singer.");
            }
            return project;
        }

        static PitchPointShape ParseShape(string? shape) {
            switch (shape) {
                case "i": return PitchPointShape.i;
                case "o": return PitchPointShape.o;
                case "l": return PitchPointShape.l;
                default: return PitchPointShape.io;
            }
        }

        static string ShapeString(PitchPointShape shape) {
            switch (shape) {
                case PitchPointShape.i: return "i";
                case PitchPointShape.o: return "o";
                case PitchPointShape.l: return "l";
                default: return "io";
            }
        }

        static Dictionary<int, double> MergeLayers(PmwsLayers? layers) {
            var merged = new Dictionary<int, double>();
            void Add(Dictionary<string, double>? layer) {
                if (layer == null) {
                    return;
                }
                foreach (var kv in layer) {
                    if (int.TryParse(kv.Key, out int k)) {
                        merged[k] = kv.Value;
                    }
                }
            }
            Add(layers?.auto);
            Add(layers?.preset);
            return merged;
        }

        #endregion

        #region save

        public static List<string> Save(string filePath, UProject project) {
            var parts = project.parts.OfType<UVoicePart>().Where(p => p.notes.Count > 0).ToList();
            if (parts.Count == 0) {
                throw new FileFormatException("No voice parts with notes to export.");
            }
            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(filePath);
            var written = new List<string>();
            for (int i = 0; i < parts.Count; i++) {
                string path = i == 0
                    ? Path.Combine(dir, stem + ".pmws")
                    : Path.Combine(dir, $"{stem}-{i + 1}.pmws");
                File.WriteAllText(path, JsonConvert.SerializeObject(
                    BuildPart(project, parts[i]), Formatting.Indented), Encoding.UTF8);
                written.Add(path);
            }
            return written;
        }

        static PmwsProject BuildPart(UProject project, UVoicePart part) {
            double bpm = project.tempos.Count > 0 ? project.tempos[0].bpm : 120;
            double msPerBeat = 60000.0 / bpm;
            double partBeat = (double)part.position / kResolution;

            var notes = new List<PmwsNote>();
            var anchors = new List<PmwsAnchor>();
            int id = 0;
            foreach (var note in part.notes) {
                id++;
                double startBeat = partBeat + (double)note.position / kResolution;
                var pn = new PmwsNote {
                    id = id,
                    startBeat = startBeat,
                    lenBeat = (double)note.duration / kResolution,
                    midi = note.tone,
                    lyric = note.lyric,
                };
                if (note.vibrato != null && note.vibrato.length > 0 && note.vibrato.period > 0) {
                    pn.vib = new PmwsVibrato {
                        length = note.vibrato.length / 100.0,
                        rate = 1000.0 / note.vibrato.period,
                        depth = note.vibrato.depth / 100.0,
                    };
                }
                var phonemes = note.phonemeOverrides
                    .Where(o => !string.IsNullOrWhiteSpace(o.phoneme))
                    .OrderBy(o => o.index)
                    .Select(o => o.phoneme!)
                    .ToList();
                if (phonemes.Count > 0) {
                    pn.phonemes = phonemes;
                }
                notes.Add(pn);

                var data = note.pitch?.data;
                if (data == null || data.Count == 0) {
                    continue;
                }
                for (int i = 0; i < data.Count; i++) {
                    double beat = startBeat + data[i].X / msPerBeat;
                    double midi = note.tone + data[i].Y / 10.0;
                    bool main = i < 2;
                    anchors.Add(new PmwsAnchor {
                        beat = beat,
                        midi = midi,
                        shape = ShapeString(data[i].shape),
                        main = main,
                        noteId = id,
                        role = main ? (i == 0 ? "in0" : "in1") : null,
                        dx = main ? beat - startBeat : (double?)null,
                    });
                }
            }

            var preset = new Dictionary<string, double>();
            var curve = part.curves.FirstOrDefault(c => c.abbr == Ustx.PITD && c.xs.Count > 0);
            if (curve != null && notes.Count > 0) {
                double first = notes.Min(n => n.startBeat);
                double last = notes.Max(n => n.startBeat + n.lenBeat);
                for (int k = (int)Math.Floor(first * PD_RES); k <= (int)Math.Ceiling(last * PD_RES); k++) {
                    int tick = (int)Math.Round((double)k / PD_RES * kResolution) - part.position;
                    double semitones = curve.Sample(tick) / 100.0;
                    if (Math.Abs(semitones) > 0.02) {
                        preset[k.ToString()] = Math.Round(semitones, 4);
                    }
                }
            }

            return new PmwsProject {
                app = kApp,
                version = kFormat,
                name = project.name,
                tempo = bpm,
                singer = string.Empty,
                notes = notes,
                pitchLayers = new PmwsLayers { preset = preset, auto = new Dictionary<string, double>() },
                anchors = anchors.Count > 0 ? anchors : null,
            };
        }

        #endregion
    }
}
