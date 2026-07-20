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
        public const string App = "printmov-web-synth";
        public const int Format = 2;
        const int PD_RES = 48;
        const int Resolution = 480;

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

        class PmwsSettings {
            public string pitchMode = "preset";
            public string porta = "standard";
        }

        class PmwsLayers {
            public Dictionary<string, double>? preset;
            public Dictionary<string, double>? auto;
        }

        class PmwsProject {
            public string? app;
            public int version;
            public string? name;
            // Bundles only, label for the web synth's part picker.
            public string? track;
            public double tempo;
            public string? singer;
            public List<PmwsNote>? notes;
            public PmwsLayers? pitchLayers;
            public List<PmwsAnchor>? anchors;
            public PmwsSettings? settings;
        }

        class PmwsBundle {
            public string? app;
            public int version;
            public int bundle;
            public string? name;
            public List<PmwsProject>? parts;
        }

        #endregion

        // Nulls mean nothing to the reader and the payload has to fit a QR code.
        static readonly JsonSerializerSettings Compact = new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static bool IsPmws(string contents) {
            return contents.Contains("\"" + App + "\"");
        }

        #region load

        public static UProject Load(string filePath) {
            return LoadJson(File.ReadAllText(filePath, Encoding.UTF8),
                Path.GetFileNameWithoutExtension(filePath), filePath);
        }

        // Single part or bundle; a bundle becomes one track per part.
        public static UProject LoadJson(string text, string nameHint, string filePath = "") {
            var bundle = JsonConvert.DeserializeObject<PmwsBundle>(text);
            var files = bundle?.parts != null && bundle.parts.Count > 0
                ? bundle.parts
                : new List<PmwsProject?> { JsonConvert.DeserializeObject<PmwsProject>(text) }
                    .Where(p => p != null).Select(p => p!).ToList();
            if (files.Count == 0 || files.All(f => f.notes == null || f.notes.Count == 0)) {
                throw new FileFormatException("No notes in pmws data.");
            }

            var first = files[0];
            double bpm = first.tempo > 0 ? first.tempo : 120;

            UProject project = Ustx.Create();
            project.name = !string.IsNullOrEmpty(bundle?.name) ? bundle!.name!
                : (string.IsNullOrEmpty(first.name) ? nameHint : first.name!);
            project.tempos = new List<UTempo> { new UTempo(0, bpm) };
            project.timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };
            project.printmov = new UPrintmovOrigin { app = App, format = first.version };
            project.tracks.Clear();

            for (int i = 0; i < files.Count; i++) {
                if (files[i].notes != null && files[i].notes!.Count > 0) {
                    AddPart(project, files[i], bpm);
                }
            }

            project.FilePath = filePath;
            project.Saved = false;   // .pmws is not a ustx; force Save As
            project.AfterLoad();
            project.ValidateFull();
            return project;
        }

        static void AddPart(UProject project, PmwsProject file, double bpm) {
            double msPerBeat = 60000.0 / bpm;
            int trackNo = project.tracks.Count;
            var track = new UTrack(project) { TrackNo = trackNo };
            if (!string.IsNullOrEmpty(file.track)) {
                track.TrackName = file.track!;
            }
            project.tracks.Add(track);
            var part = new UVoicePart { trackNo = trackNo, position = 0 };
            project.parts.Add(part);

            var anchorsByNote = (file.anchors ?? new List<PmwsAnchor>())
                .Where(a => !a.hidden)
                .GroupBy(a => a.noteId)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.beat).ToList());

            foreach (var n in file.notes!) {
                var note = project.CreateNote(
                    n.midi,
                    (int)Math.Round(n.startBeat * Resolution),
                    Math.Max(10, (int)Math.Round(n.lenBeat * Resolution)));
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
                int lastTick = int.MinValue;
                void Put(int tick, int cents) {
                    if (tick <= lastTick) {
                        return;         // keep xs strictly increasing
                    }
                    curve.xs.Add(tick);
                    curve.ys.Add(cents);
                    lastTick = tick;
                }
                var keys = pitd.Keys.OrderBy(k => k).ToList();
                for (int i = 0; i < keys.Count; i++) {
                    int key = keys[i];
                    if (i > 0 && keys[i - 1] != key - 1) {
                        Put(KeyTick(keys[i - 1] + 1), 0);
                        Put(KeyTick(key - 1), 0);
                    }
                    Put(KeyTick(key), (int)Math.Round(pitd[key] * 100));
                }
                part.curves.Add(curve);
            }

            part.name = string.IsNullOrEmpty(file.name) ? project.name : file.name!;
            part.Duration = Math.Max(Resolution * 4, part.GetMinDurTick(project));
            if (!string.IsNullOrEmpty(file.singer)) {
                Log.Information($"pmws singer '{file.singer}' — not mapped to an OpenUtau singer.");
            }
        }

        // Pen-layer key (beat * PD_RES) to part tick.
        static int KeyTick(int key) {
            return (int)Math.Round((double)key / PD_RES * Resolution);
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
                // Encoding.UTF8 writes a BOM, which the web synth's JSON.parse rejects.
                File.WriteAllText(path, JsonConvert.SerializeObject(
                    BuildPart(project, parts[i]), Formatting.Indented, Compact),
                    new UTF8Encoding(false));
                written.Add(path);
            }
            return written;
        }

        // Null part picks the first one with notes.
        public static string SerializePart(UProject project, UVoicePart? part = null) {
            part ??= project.parts.OfType<UVoicePart>().FirstOrDefault(p => p.notes.Count > 0);
            if (part == null) {
                throw new FileFormatException("No voice parts with notes to export.");
            }
            return JsonConvert.SerializeObject(BuildPart(project, part), Formatting.None, Compact);
        }

        public static List<UVoicePart> VoiceParts(UProject project) {
            return project.parts.OfType<UVoicePart>().Where(p => p.notes.Count > 0).ToList();
        }

        public static string SerializeBundle(UProject project) {
            var parts = VoiceParts(project);
            if (parts.Count == 0) {
                throw new FileFormatException("No voice parts with notes to export.");
            }
            var bundle = new PmwsBundle {
                app = App,
                version = Format,
                bundle = 1,
                name = project.name,
                parts = parts.Select(p => {
                    var built = BuildPart(project, p);
                    built.track = TrackLabel(project, p);
                    built.name = built.track;
                    return built;
                }).ToList(),
            };
            return JsonConvert.SerializeObject(bundle, Formatting.None, Compact);
        }

        public static string TrackLabel(UProject project, UVoicePart part) {
            var track = project.tracks.FirstOrDefault(t => t.TrackNo == part.trackNo);
            string name = track?.TrackName ?? $"Track {part.trackNo + 1}";
            return part.DisplayName == name ? name : $"{name} — {part.DisplayName}";
        }

        const double PortaStart = -40.0 / 480;
        const double PortaLen = 80.0 / 480;

        // in1 is pitch-locked to the note tone, so the landing must be found by pitch.
        static void EmitAnchors(List<PmwsAnchor> anchors, UNote note, int id,
                double startBeat, double partBeat, double msPerBeat, UNote? prev, UNote? next) {
            double lenBeat = (double)note.duration / Resolution;
            double endBeat = startBeat + lenBeat;
            double prevEndBeat = prev == null ? double.NegativeInfinity
                : partBeat + (double)(prev.position + prev.duration) / Resolution;
            double gap = prev == null ? double.PositiveInfinity : startBeat - prevEndBeat;
            // in0 uses the previous note's pitch only when it is close enough to glide.
            bool glide = prev != null && gap < 0.5 && prev.tone != note.tone;

            var data = (note.pitch?.data ?? new List<PitchPoint>())
                .OrderBy(pt => pt.X).ToList();

            double in0dx, in1dx;
            var userPoints = new List<(double Beat, double Midi, string Shape)>();
            if (data.Count == 0) {
                // no pitch data, fall back to the preset transition
                in0dx = PortaStart;
                in1dx = PortaStart + PortaLen;
            } else {
                var dBeats = data.Select(pt => pt.X / msPerBeat).ToList();
                var midis = data.Select(pt => note.tone + pt.Y / 10.0).ToList();

                int land = -1;
                for (int k = 1; k < data.Count; k++) {
                    if (Math.Abs(midis[k] - note.tone) < 0.05) { land = k; break; }
                }
                if (land < 0) {
                    land = 0;
                    for (int k = 1; k < data.Count; k++) {
                        if (Math.Abs(dBeats[k]) < Math.Abs(dBeats[land])) { land = k; }
                    }
                }

                double lead = Math.Min(1, Math.Max(0.02, gap > 0 && !double.IsInfinity(gap) ? gap : 1));
                in0dx = Math.Max(-lead, Math.Min(lenBeat, dBeats[0]));
                in1dx = land > 0
                    ? Math.Max(in0dx, Math.Min(lenBeat, dBeats[land]))
                    : Math.Max(in0dx, PortaStart + PortaLen);

                for (int k = 1; k < data.Count; k++) {
                    if (k == land) { continue; }
                    userPoints.Add((startBeat + dBeats[k], midis[k], ShapeString(data[k].shape)));
                }
            }

            anchors.Add(new PmwsAnchor {
                beat = startBeat + in0dx,
                midi = glide ? prev!.tone : note.tone,
                shape = "io", main = true, noteId = id, role = "in0", dx = in0dx,
            });
            anchors.Add(new PmwsAnchor {
                beat = startBeat + in1dx,
                midi = note.tone,
                shape = "io", main = true, noteId = id, role = "in1", dx = in1dx,
            });
            foreach (var up in userPoints) {
                anchors.Add(new PmwsAnchor {
                    beat = up.Beat, midi = up.Midi, shape = up.Shape, noteId = id,
                });
            }

            // A note before a rest needs a hold anchor, or the curve slides into the gap.
            bool tailDefined = userPoints.Any(up => up.Beat >= endBeat - 0.08);
            double nextStartBeat = next == null ? double.PositiveInfinity
                : partBeat + (double)next.position / Resolution;
            if (!tailDefined && (next == null || nextStartBeat - endBeat >= 0.5)) {
                anchors.Add(new PmwsAnchor {
                    beat = endBeat, midi = note.tone, shape = "io",
                    main = true, noteId = id, role = "hold", hidden = true,
                });
            }
        }

        static PmwsProject BuildPart(UProject project, UVoicePart part) {
            double bpm = project.tempos.Count > 0 ? project.tempos[0].bpm : 120;
            double msPerBeat = 60000.0 / bpm;
            double partBeat = (double)part.position / Resolution;

            var notes = new List<PmwsNote>();
            var anchors = new List<PmwsAnchor>();
            var source = part.notes.ToList();
            int id = 0;
            for (int n = 0; n < source.Count; n++) {
                var note = source[n];
                id++;
                double startBeat = partBeat + (double)note.position / Resolution;
                var pn = new PmwsNote {
                    id = id,
                    startBeat = startBeat,
                    lenBeat = (double)note.duration / Resolution,
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

                EmitAnchors(anchors, note, id, startBeat, partBeat, msPerBeat,
                    n > 0 ? source[n - 1] : null,
                    n + 1 < source.Count ? source[n + 1] : null);
            }

            var preset = new Dictionary<string, double>();
            var curve = part.curves.FirstOrDefault(c => c.abbr == Ustx.PITD && c.xs.Count > 0);
            if (curve != null && notes.Count > 0) {
                double first = notes.Min(n => n.startBeat);
                double last = notes.Max(n => n.startBeat + n.lenBeat);
                var spans = notes
                    .Select(x => (Start: x.startBeat, End: x.startBeat + x.lenBeat))
                    .OrderBy(x => x.Start)
                    .ToList();
                int span = 0;
                for (int k = (int)Math.Floor(first * PD_RES); k <= (int)Math.Ceiling(last * PD_RES); k++) {
                    double beat = (double)k / PD_RES;
                    while (span < spans.Count && spans[span].End <= beat) {
                        span++;
                    }
                    if (span >= spans.Count) {
                        break;
                    }
                    if (beat < spans[span].Start) {
                        continue;   // inside a rest
                    }
                    int tick = (int)Math.Round((double)k / PD_RES * Resolution) - part.position;
                    double semitones = curve.Sample(tick) / 100.0;
                    if (Math.Abs(semitones) > 0.02) {
                        preset[k.ToString()] = Math.Round(semitones, 4);
                    }
                }
            }

            return new PmwsProject {
                app = App,
                version = Format,
                name = project.name,
                tempo = bpm,
                singer = string.Empty,
                notes = notes,
                pitchLayers = new PmwsLayers { preset = preset, auto = new Dictionary<string, double>() },
                settings = new PmwsSettings(),
                anchors = anchors.Count > 0 ? anchors : null,
            };
        }

        #endregion
    }
}
