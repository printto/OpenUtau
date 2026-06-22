using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Thai G2P Phonemizer", "TH VCCV & CVVC", "PRINTmov", language: "TH")]
    public class ThaiVCCVPhonemizer : Phonemizer {

        readonly string[] vowels = new string[] {
            "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua", "I", "8"
        };

        readonly string[] diphthongs = new string[] {
            "r", "l", "w"
        };

        readonly string[] consonants = new string[] {
            "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y"
        };

        readonly string[] endingConsonants = new string[] {
            "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y"
        };

        // Maps ThaiG2p output (DiffSinger notation) to this voicebank's UTAU/VCCV notation.
        private static readonly Dictionary<string, string> G2pToVCCV = new Dictionary<string, string> {
            // onset consonants
            {"b", "b"}, {"ch", "ch"}, {"d", "d"}, {"f", "f"}, {"h", "h"}, {"j", "j"},
            {"kk", "k"}, {"k", "kh"}, {"l", "l"}, {"m", "m"}, {"n", "n"}, {"ng", "g"},
            {"pp", "p"}, {"p", "ph"}, {"r", "r"}, {"s", "s"}, {"tt", "t"}, {"t", "th"},
            {"w", "w"}, {"y", "y"},
            // vowels
            {"A", "@"}, {"E", "3"}, {"I", "I"}, {"O", "Q"}, {"U", "1"}, {"Ua", "6"},
            {"a", "a"}, {"au", "8"}, {"e", "e"}, {"i", "i"}, {"ia", "ia"}, {"o", "o"},
            {"u", "u"}, {"ua", "ua"},
            // ending consonants
            {"B", "b"}, {"D", "d"}, {"K", "k"}, {"W", "w"}, {"Y", "y"},
        };

        private USinger singer;
        private IG2p g2p;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (g2p == null) {
                try {
                    g2p = new ThaiG2p();
                } catch (Exception e) {
                    Log.Error(e, "Failed to load Thai G2p.");
                }
            }
        }

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;

            foreach (string test in input) {
                if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    oto = otoCandidacy;
                    return true;
                }
            }
            return false;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize();
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                currentLyric = note.phoneticHint.Normalize();
            }

            var phonemes = new List<Phoneme>();

            List<string> tests = new List<string>();

            string prevTemp = "";
            if (prevNeighbour != null) {
                prevTemp = prevNeighbour.Value.lyric;
            }
            var prevTh = ParseInput(prevTemp);

            var noteTh = ParseInput(currentLyric);

            if (noteTh.Consonant != null && noteTh.Dipthong == null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            } else if (noteTh.Consonant != null && noteTh.Dipthong != null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Dipthong + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                } else {
                    if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Dipthong }, note, out tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                    if (checkOtoUntilHit(new string[] { noteTh.Dipthong + noteTh.Vowel }, note, out tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                }
            }

            if (noteTh.Consonant == null && noteTh.Vowel != null) {
                if (prevTh.EndingConsonant != null && checkOtoUntilHit(new string[] { prevTh.EndingConsonant + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                } else if (prevTh.Vowel != null && checkOtoUntilHit(new string[] { prevTh.Vowel + noteTh.Vowel }, note, out tempOto)) {
                    tests.Add(tempOto.Alias);
                } else if (checkOtoUntilHit(new string[] { noteTh.Vowel }, note, out tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            }

            if (noteTh.EndingConsonant != null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Vowel + noteTh.EndingConsonant }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            } else if (nextNeighbour != null && noteTh.Vowel != null) {
                var nextTh = ParseInput(nextNeighbour.Value.lyric);
                if (checkOtoUntilHit(new string[] { noteTh.Vowel + " " + nextTh.Consonant }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            }

            if (prevNeighbour == null && tests.Count >= 1) {
                if (checkOtoUntilHit(new string[] { "-" + tests[0] }, note, out var tempOto)) {
                    tests[0] = (tempOto.Alias);
                }
            }

            if (nextNeighbour == null && tests.Count >= 1) {
                if (noteTh.EndingConsonant == null) {
                    if (checkOtoUntilHit(new string[] { noteTh.Vowel + "-" }, note, out var tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                } else {
                    if (checkOtoUntilHit(new string[] { tests[tests.Count - 1] + "-" }, note, out var tempOto)) {
                        tests[tests.Count - 1] = (tempOto.Alias);
                    }
                }
            }

            if (tests.Count <= 0) {
                if (checkOtoUntilHit(new string[] { currentLyric }, note, out var tempOto)) {
                    tests.Add(currentLyric);
                }
            }

            if (checkOtoUntilHit(tests.ToArray(), note, out var oto)) {

                var noteDuration = notes.Sum(n => n.duration);

                for (int i = 0; i < tests.ToArray().Length; i++) {

                    int position = 0;
                    int vcPosition = noteDuration - 120;

                    if (nextNeighbour != null && tests[i].Contains(" ")) {
                        var nextLyric = nextNeighbour.Value.lyric.Normalize();
                        if (!string.IsNullOrEmpty(nextNeighbour.Value.phoneticHint)) {
                            nextLyric = nextNeighbour.Value.phoneticHint.Normalize();
                        }
                        var nextTh = ParseInput(nextLyric);
                        var nextCheck = nextTh.Vowel;
                        if (nextTh.Consonant != null) {
                            nextCheck = nextTh.Consonant + nextTh.Vowel;
                        }
                        if (nextTh.Dipthong != null) {
                            nextCheck = nextTh.Consonant + nextTh.Dipthong + nextTh.Vowel;
                        }
                        var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                        if (singer.TryGetMappedOto(nextCheck, nextNeighbour.Value.tone + nextAttr.toneShift, nextAttr.voiceColor, out var nextOto)) {
                            if (oto.Overlap > 0) {
                                vcPosition = noteDuration - MsToTick(nextOto.Overlap) - MsToTick(nextOto.Preutter);
                            }
                        }
                    }


                    if (noteTh.Dipthong == null || tests.Count <= 2) {
                        if (i == 1) {
                            position = Math.Max((int)(noteDuration * 0.75), vcPosition);
                        }
                    } else {
                        if (i == 1) {
                            position = Math.Min((int)(noteDuration * 0.1), 60);
                        } else if (i == 2) {
                            position = Math.Max((int)(noteDuration * 0.75), vcPosition);
                        }
                    }

                    phonemes.Add(new Phoneme { phoneme = tests[i], position = position });
                }

            }

            return new Result {
                phonemes = phonemes.ToArray()
            };
        }

        (string Consonant, string Dipthong, string Vowel, string EndingConsonant) ParseInput(string input) {
            input = WordToPhonemes(input);

            string consonant = null;
            string diphthong = null;
            string vowel = null;
            string endingConsonant = null;

            if (input == null) {
                return (null, null, null, null);
            }

            foreach (var con in consonants) {
                if (input.StartsWith(con)) {
                    if (consonant == null || consonant.Length < con.Length) {
                        consonant = con;
                    }
                }
            }

            int startIdx = consonant?.Length ?? 0;
            foreach (var dip in diphthongs) {
                if (input.Substring(startIdx).StartsWith(dip)) {
                    if (diphthong == null || diphthong.Length < dip.Length) {
                        diphthong = dip;
                    }
                }
            }

            startIdx += diphthong?.Length ?? 0;
            foreach (var vow in vowels) {
                if (input.Substring(startIdx).StartsWith(vow)) {
                    if (vowel == null || vowel.Length < vow.Length) {
                        vowel = vow;
                    }
                }
            }

            foreach (var con in endingConsonants) {
                if (input.EndsWith(con)) {
                    if (endingConsonant == null || endingConsonant.Length < con.Length) {
                        endingConsonant = con;
                    }
                }
            }

            return (consonant, diphthong, vowel, endingConsonant);
        }

        // Convert a Thai lyric into VCCV phonemes using OpenUtau's Thai G2p,
        // then remap the G2p (DiffSinger) phonemes to this voicebank's notation.
        public string WordToPhonemes(string input) {
            input = input.Replace(" ", "");
            if (!Regex.IsMatch(input, "[ก-ฮ]")) {
                return input; // not Thai text (e.g. an already-phonemized hint)
            }
            var result = g2p?.Query(input);
            if (result == null || result.Length == 0) {
                return input;
            }
            var sb = new StringBuilder();
            foreach (var phoneme in result) {
                sb.Append(G2pToVCCV.TryGetValue(phoneme, out var mapped) ? mapped : phoneme);
            }
            return sb.ToString();
        }

    }
}
