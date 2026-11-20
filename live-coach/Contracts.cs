using System;

namespace IntsLiveCoach
{
    public interface ITelemetryReader : IDisposable
    {
        bool TryRead(out TelemetryFrame frame, out string status);
        bool LastReadTransient { get; }
    }
    // SI where stated; speed km/h, position metres, time simulation seconds.
    // Missing channels stay NaN, never an invented zero.
    public sealed class TelemetryFrame
    {
        public double Time = double.NaN;
        public double DistanceAgeSeconds = double.NaN;
        public double LapDistance = double.NaN;
        public double TrackLength = double.NaN;
        public double SpeedKph = double.NaN;
        public double Throttle = double.NaN;
        public double Brake = double.NaN;
        public double Clutch = double.NaN; // 1 = pedal pressed / disengaged; unknown polarity stays NaN.
        public double Steering = double.NaN;
        public double FuelLitres = double.NaN;
        public double X = double.NaN, Y = double.NaN, Z = double.NaN;
        public double YawRate = double.NaN, LateralG = double.NaN;
        public double Handbrake = double.NaN, LongitudinalG = double.NaN;
        // Right-positive vehicle-local lateral velocity, forward-positive longitudinal velocity, m/s.
        // Populate only after the source coordinate convention has been verified.
        public double LocalVelocityRight = double.NaN, LocalVelocityForward = double.NaN;
        public double LocalVelocitySideAbs = double.NaN;
        public bool TimeIsCaptureClock;
        public int Lap = -1, Gear = -1;
        public string Track = "", Car = "", GameVersion = "";
        public bool InPit, IsReplay, IsValidLap, OnTrack, ValidLapKnown;
        public DateTime CaptureUtc;
    }
}
