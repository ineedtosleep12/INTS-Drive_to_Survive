using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace IntsLiveCoach
{
    // A frozen native distance interval. The line from the calibration lap is used only
    // to distinguish layout/timing origins. A driver is free to change their racing line.
    public sealed class BrakePracticeCorner
    {
        readonly ReferenceLap calibration;
        public readonly string Name, Track, Car, GameVersion;
        public readonly double StartM, EndM, TrackLengthM;

        internal BrakePracticeCorner(ReferenceLap lap, string name, double start, double end)
        {
            calibration = lap;
            Name = name; StartM = start; EndM = end;
            Car = BrakePracticeCourse.NativeLabel(lap, "sourceCarLabel", lap.Car);
            Track = BrakePracticeCourse.NativeLabel(lap, "sourceTrackLabel", lap.Track);
            GameVersion = lap.GameVersion; TrackLengthM = lap.TrackLengthM;
        }

        public bool MatchesFrame(TelemetryFrame frame)
        {
            if (frame == null || frame.TimeIsCaptureClock || frame.Car != Car || frame.Track != Track ||
                frame.GameVersion != GameVersion || frame.TrackLength != TrackLengthM ||
                !ReferenceLap.Finite(frame.LapDistance)) return false;
            // Include approach and departure samples so the gates can be interpolated.
            if (frame.LapDistance < StartM - 40 || frame.LapDistance > EndM + 40) return true;
            var expected = ReferenceComparison.SampleAtDistance(calibration, frame.LapDistance);
            return ReferenceComparison.NearbyWorldPositions(expected, new ReferenceSample { X=frame.X, Z=frame.Z });
        }
        public override string ToString()
        {
            return String.Format(CultureInfo.CurrentCulture, "{0} · {1:N0}–{2:N0} m", Name, StartM, EndM);
        }
    }

    public static class BrakePracticeCourse
    {
        static readonly LmuCircuitCourse Monza = LmuCircuitGuide.Courses.First(c => c.Id == "monza");
        static readonly HashSet<string> PorscheModelLabels = new HashSet<string> {
            "porsche 911 gt3 r 992", "porsche 911 gt3 r 992 lmgt3", "porsche 911 gt3 r lmgt3 992",
            "porsche 992 gt3 r", "porsche 992 gt3r", "porsche 992 lmgt3 r", "porsche 992 lmgt3r",
            "porsche 992 lmgt3", "porsche 911 gt3 r 992 gt3", "911gt3r 2024"
        };
        static string Words(string value)
        { return Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim(); }
        public static bool IsIdentifiedPorsche992(string carLabel)
        { return PorscheModelLabels.Contains(Words(carLabel)); }

        public static bool IsKnownOtherCar(string carLabel)
        {
            string words = Words(carLabel);
            if (IsIdentifiedPorsche992(carLabel)) return false;
            // Exact community car labels are identified in LMU-COMMUNITY-SOURCES.md.
            if (words == "dkr engineering 4 elms25" || words == "vista af corse 2026 21 lm") return true;
            // A team alone is not a model identifier. Ambiguous labels require the
            // driver's explicit in-game model confirmation in the dedicated preset.
            words = Words(Regex.Replace(carLabel ?? "", @"#\s*\d+", ""));
            return Regex.IsMatch(words, @"\b(?:cup|rsr(?:19)?|963|991|992s pc|lmp[23]|hypercar|lmh|lmdh|ferrari|ginetta|oreca|corvette|bmw|aston martin|cadillac|alpine a424|duqueine|adess|ligier|ford mustang|genesis gmr001|isotta|lamborghini|lexus|mclaren|mercedes|peugeot|toyota gr010|vandervell)\b");
        }
        public static bool IsMonzaTrack(string track, double lengthM)
        { return Monza.MatchesPracticeLayout(track, lengthM); }
        internal static string NativeLabel(ReferenceLap lap, string key, string fallback)
        {
            string label;
            return lap.Conditions.TryGetValue(key,out label) ? label : fallback;
        }

        static bool Usable(ReferenceLap lap)
        {
            if (lap == null || lap.Game != "LMU" || !lap.Complete || lap.Validity != "valid" ||
                !lap.HasTime || lap.DistanceBasis != ReferenceLap.LmuDistanceBasis ||
                lap.CoordinateFrame != ReferenceLap.LmuCoordinateFrame || ReferenceLap.Unknown(lap.GameVersion) ||
                !ReferenceLap.Finite(lap.TrackLengthM)) return false;
            try { lap.Validate(); } catch (FormatException) { return false; }
            return !ReferenceLap.Unknown(NativeLabel(lap,"sourceCarLabel",lap.Car)) &&
                !ReferenceLap.Unknown(NativeLabel(lap,"sourceTrackLabel",lap.Track)) &&
                lap.Samples[0].DistanceM <= .01 && Math.Abs(lap.Samples.Last().DistanceM-lap.TrackLengthM) <= .01;
        }
        static bool ObservedRange(ReferenceLap lap,double start,double end)
        {
            if (start < 0 || end > lap.TrackLengthM || end <= start)
                return false;
            var a = ReferenceComparison.SampleAtDistance(lap,start);
            var b = ReferenceComparison.SampleAtDistance(lap,end);
            if (a == null || b == null || !ReferenceLap.Finite(a.X) || !ReferenceLap.Finite(b.X) ||
                !ReferenceLap.Finite(a.SpeedKph) || !ReferenceLap.Finite(b.SpeedKph) || !(a.Brake <= .10))
                return false;
            bool moved=false;
            for (int i=1; i<lap.Samples.Length; i++)
            {
                var previous=lap.Samples[i-1]; var current=lap.Samples[i];
                if (current.DistanceM<start) continue;
                if (previous.DistanceM>end) break;
                if (current.Time-previous.Time>.5 || current.DistanceM-previous.DistanceM>60 ||
                    !ReferenceLap.Finite(current.X) || !ReferenceLap.Finite(current.Z) ||
                    !ReferenceLap.Finite(current.SpeedKph) || !ReferenceLap.Finite(current.Brake)) return false;
                double dx=current.X-a.X, dz=current.Z-a.Z;
                if (dx*dx+dz*dz>15*15) moved=true;
            }
            return moved;
        }

        public static BrakePracticeCorner[] FromNativeLap(ReferenceLap lap)
        {
            if (!Usable(lap)) return new BrakePracticeCorner[0];
            var reader = new LiveReferenceCoach();
            reader.Configure(new ReferenceLiveSettings(lap,true,false,false));
            var starts = reader.PreparedZoneStarts; var ends = reader.PreparedZoneEnds;
            var corners = new List<BrakePracticeCorner>();
            for (int i=0; i<starts.Length; i++)
            {
                double start=starts[i], end=ends[i];
                if (!ObservedRange(lap,start,end)) continue;
                int heavy=FindHeld(lap.Samples,0,start,end,p=>p.Brake>=.6,.1);
                if (heavy<0 || FindHeld(lap.Samples,heavy,start,end,p=>p.Brake<=.1,.1)<0) continue;
                corners.Add(new BrakePracticeCorner(lap,"제동 구간 " + (i+1),start,end));
            }
            return corners.ToArray();
        }

        // The training gates are determined from native LMU distance and steering on
        // THIS lap. Do not transplant distances from the creator's MoTeC timing origin.
        public static BrakePracticeCorner MonzaFirstChicane(ReferenceLap lap)
        {
            if (!Usable(lap) || !IsMonzaTrack(NativeLabel(lap,"sourceTrackLabel",lap.Track),lap.TrackLengthM) ||
                IsKnownOtherCar(NativeLabel(lap,"sourceCarLabel",lap.Car)) ||
                (!ReferenceLap.Unknown(lap.Layout) && !IsMonzaTrack("Monza "+lap.Layout,lap.TrackLengthM))) return null;
            var samples=lap.Samples;
            // Broad search neighbourhood of the first braking complex only. These are
            // identification bounds, not an ideal brake marker or either timing gate.
            double limit=lap.TrackLengthM*.235;
            int heavy=FindHeld(samples,0,lap.TrackLengthM*.065,lap.TrackLengthM*.175,p=>p.Brake>=.6,.1);
            if (heavy<1) return null;
            int begin=heavy;
            while (begin>0 && samples[begin-1].Brake>.10) begin--;
            double start=samples[begin].DistanceM-85;
            var entry=ReferenceComparison.SampleAtDistance(lap,start);
            if (entry==null || !ReferenceLap.Finite(entry.SpeedKph)) return null;
            // A steering correction on the approach is not either half of the chicane.
            double slowTurn=entry.SpeedKph*.75;
            int first=FindHeld(samples,heavy,0,limit,p=>p.SpeedKph<=slowTurn && Math.Abs(p.Steering)>=.10,.15);
            if (first<0) return null;
            int firstSign=Math.Sign(samples[first].Steering);
            int opposite=FindHeld(samples,first+1,0,limit,p=>p.SpeedKph<=slowTurn && p.Steering*firstSign<=-.10,.15);
            if (opposite<0) return null;
            int clear=FindHeld(samples,opposite+1,0,limit,p=>Math.Abs(p.Steering)<=.08 && p.Brake<=.08 && p.Throttle>=.40,.5);
            if (clear<0) return null;
            double end=samples[clear].DistanceM+20;
            if (end>limit || !ObservedRange(lap,start,end) ||
                FindHeld(samples,heavy,0,end,p=>p.Brake<=.10,.10)<0) return null;
            var exit=ReferenceComparison.SampleAtDistance(lap,end);
            if (exit==null || !(exit.Brake<=.10 && exit.Throttle>=.40) || !(Math.Abs(exit.Steering)<=.13)) return null;
            return new BrakePracticeCorner(lap,"몬자 첫 시케인 · 레티필로",start,end);
        }

        // Return the FIRST sample of a sustained interval. A gap cannot connect two
        // isolated observations into a sustained brake, direction change or exit.
        static int FindHeld(ReferenceSample[] samples,int from,double minM,double maxM,
            Func<ReferenceSample,bool> matches,double seconds)
        {
            int began=-1;
            for (int i=from;i<samples.Length && samples[i].DistanceM<=maxM;i++)
            {
                var s=samples[i];
                if (s.DistanceM<minM || !matches(s)) {began=-1;continue;}
                if (began<0 || s.Time-samples[i-1].Time>.5 || s.DistanceM-samples[i-1].DistanceM>60) began=i;
                if (s.Time-samples[began].Time>=seconds) return began;
            }
            return -1;
        }
    }
}
