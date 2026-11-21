using System;

namespace IntsLiveCoach
{
    // UI-thread session. Only the native recording pipeline may supply a completed lap here.
    internal sealed class LmuCoursePractice
    {
        public LmuCircuitCourse Course { get; private set; }
        public ReferenceLap Baseline { get; private set; }
        public int ComparisonLaps { get; private set; }
        public void Begin(LmuCircuitCourse course)
        {
            Course = course;
            Baseline = null;
            ComparisonLaps = 0;
        }

        public bool Accepts(string track, double length, string car, string version)
        {
            if (Course == null || !Course.MatchesPracticeLayout(track, length)) return false;
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
            if (Baseline == null) Baseline = lap;
            else ComparisonLaps++;
            return true;
        }
    }
}
