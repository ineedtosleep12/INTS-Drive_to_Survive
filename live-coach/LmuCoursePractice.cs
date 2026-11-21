using System;

namespace IntsLiveCoach
{
    // UI-thread session. Only the native recording pipeline may supply a completed lap here.
    internal sealed class LmuCoursePractice
    {
        public LmuCircuitCourse Course { get; private set; }
        public ReferenceLap Baseline { get; private set; }
        public int ComparisonLaps { get; private set; }
        public string LiveTrack { get; private set; }
        public string LiveCar { get; private set; }
        public double LiveLengthMeters { get; private set; }
        string liveVersion;

        public void Begin(LmuCircuitCourse course)
        {
            Course = course;
            Baseline = null;
            ComparisonLaps = 0;
            LiveTrack = LiveCar = liveVersion = null;
            LiveLengthMeters = double.NaN;
        }

        // Called on fresh frames from the LMU reader after the user arms live-layout practice.
        // Return true only on the transition to a bound session.
        public bool TryBindLiveLayout(TelemetryFrame frame)
        {
            if (Course == null || !Course.IsLiveLayout || LiveTrack != null || frame == null ||
                !frame.OnTrack || frame.InPit || frame.IsReplay || frame.TimeIsCaptureClock ||
                ReferenceLap.Unknown(frame.Track) || ReferenceLap.Unknown(frame.Car) ||
                ReferenceLap.Unknown(frame.GameVersion) || !ReferenceLap.Finite(frame.TrackLength) ||
                frame.TrackLength < 100 || frame.TrackLength > 100000) return false;
            LiveTrack = frame.Track;
            LiveCar = frame.Car;
            liveVersion = frame.GameVersion;
            LiveLengthMeters = frame.TrackLength;
            return true;
        }

        public bool Accepts(string track, double length, string car, string version)
        {
            if (Course == null) return false;
            if (Course.IsLiveLayout)
                return LiveTrack != null && track == LiveTrack && car == LiveCar &&
                    version == liveVersion && length == LiveLengthMeters;
            if (!Course.MatchesPracticeLayout(track, length)) return false;
            // Family and nominal length select the exercise. A comparison also needs the exact
            // native course, livery, scoring length and build used on the recorded baseline.
            return Baseline == null || (track == Baseline.Track && car == Baseline.Car &&
                version == Baseline.GameVersion && Math.Abs(length - Baseline.TrackLengthM) < .01);
        }

        public bool Record(ReferenceLap lap)
        {
            if (lap == null || lap.Game != "LMU" || lap.DistanceBasis != ReferenceLap.LmuDistanceBasis ||
                !lap.Complete || lap.Validity != "valid" || !Accepts(lap.Track, lap.TrackLengthM, lap.Car, lap.GameVersion))
                return false;
            if (Baseline == null)
            {
                if (Course.IsLiveLayout) lap.Conditions["sourceLayoutSelection"] = "game-reported";
                Baseline = lap;
            }
            else
            {
                if (Course.IsLiveLayout && !ReferenceComparison.MatchesNativeWorldAnchors(Baseline, lap, 0, LiveLengthMeters))
                    return false;
                ComparisonLaps++;
            }
            return true;
        }
    }
}
