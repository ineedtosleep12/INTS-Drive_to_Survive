using System;
using System.Globalization;

namespace IntsLiveCoach
{
    // Accept only the complete, boundary-delimited lap exposed by CoachEngine.
    // The selected reader supplies game identity; TelemetryFrame itself has no game field.
    public static class CapturedLap
    {
        public const string AccDistanceBasis = "acc-normalized-track-position-m";
        public const string AcDistanceBasis = "ac-normalized-track-position-m";
        public const string Ams2DistanceBasis = "ams2-current-lap-distance-m";
        public const string AccCoordinateFrame = "acc-world-xz-m";
        public const string AcCoordinateFrame = "ac-world-xz-m";
        public const string Ams2CoordinateFrame = "ams2-world-xz-m";

        static bool Finite(double value) { return ReferenceLap.Finite(value); }
        static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.Ordinal); }
        static void Require(bool accepted, string message)
        { if (!accepted) throw new FormatException(message); }
        static bool Unit(double value) { return Finite(value) && value >= 0 && value <= 1; }

        public static bool TryCreate(string game, TelemetryFrame[] frames, out ReferenceLap lap, out string error)
        {
            lap = null; error = "";
            try { lap = Create(game, frames); return true; }
            catch (FormatException ex) { error = ex.Message; return false; }
        }

        public static ReferenceLap Create(string game, TelemetryFrame[] frames)
        {
            string canonicalGame = (game ?? "").Trim().ToUpperInvariant();
            string distanceBasis, coordinateFrame;
            switch (canonicalGame)
            {
                case "LMU": distanceBasis = ReferenceLap.LmuDistanceBasis; coordinateFrame = ReferenceLap.LmuCoordinateFrame; break;
                case "ACC": distanceBasis = AccDistanceBasis; coordinateFrame = AccCoordinateFrame; break;
                case "AC": distanceBasis = AcDistanceBasis; coordinateFrame = AcCoordinateFrame; break;
                case "AMS2": distanceBasis = Ams2DistanceBasis; coordinateFrame = Ams2CoordinateFrame; break;
                default: throw new FormatException("이 게임은 완전한 서킷 랩 내보내기를 지원하지 않습니다.");
            }
            Require(frames != null && frames.Length > 20 && frames.Length <= ReferenceLap.MaximumSamples,
                "출발선부터 완주까지 기록한 유효 랩이 필요합니다.");
            TelemetryFrame first = frames[0], last = frames[frames.Length - 1];
            Require(first != null && last != null, "랩 경계 표본이 없습니다.");
            Require(!ReferenceLap.Unknown(first.Car) && !ReferenceLap.Unknown(first.Track),
                "차량과 코스 식별자가 확인되지 않았습니다.");
            Require(Finite(first.TrackLength) && first.TrackLength >= 100 && first.TrackLength <= 100000,
                "게임에서 확인한 코스 길이가 필요합니다.");
            // Engine creates these endpoints only after observing each line crossing.
            // Never stretch a mid-lap recording, or a near-finish sample, to a full lap.
            Require(Finite(first.LapDistance) && Math.Abs(first.LapDistance) <= .000001 &&
                Finite(last.LapDistance) && Math.Abs(last.LapDistance - first.TrackLength) <= .000001,
                "출발선과 결승선 경계가 모두 기록된 랩만 내보낼 수 있습니다.");

            var samples = new ReferenceSample[frames.Length];
            bool hasCoordinates = false, hasPathLateral = false;
            for (int i = 0; i < frames.Length; i++)
            {
                TelemetryFrame frame = frames[i];
                Require(frame != null, "주행 중 누락된 표본이 있습니다.");
                Require(Same(frame.Car, first.Car) && Same(frame.Track, first.Track) &&
                    Same(frame.GameVersion, first.GameVersion) && frame.TrackLength == first.TrackLength &&
                    frame.Lap == first.Lap && frame.Lap >= 0,
                    "한 랩 안에서 차량·코스·버전 또는 랩 번호가 바뀌었습니다.");
                Require(frame.ValidLapKnown && frame.IsValidLap && frame.OnTrack && !frame.InPit && !frame.IsReplay,
                    "무효·유효성 미확인·피트·리플레이 표본이 있는 랩은 내보내지 않습니다.");
                Require(!frame.TimeIsCaptureClock && Finite(frame.Time) && frame.Time >= 0,
                    "게임의 주행 시계가 필요합니다. PC 수신 시각은 랩 시간으로 사용하지 않습니다.");
                Require(Finite(frame.LapDistance) && frame.LapDistance >= 0 && frame.LapDistance <= first.TrackLength,
                    "코스 거리 표본이 확인되지 않았습니다.");
                Require(Finite(frame.SpeedKph) && frame.SpeedKph >= 0 && frame.SpeedKph <= 550 &&
                    Unit(frame.Brake) && Unit(frame.Throttle) && Finite(frame.Steering) && Math.Abs(frame.Steering) <= 1,
                    "속도·브레이크·가속·조향 표본의 단위 또는 값이 올바르지 않습니다.");
                Require(double.IsNaN(frame.DistanceAgeSeconds) ||
                    (Finite(frame.DistanceAgeSeconds) && frame.DistanceAgeSeconds >= 0 && frame.DistanceAgeSeconds <= .5),
                    "코스 위치의 갱신 지연이 너무 큽니다.");
                if (i > 0)
                {
                    TelemetryFrame previous = frames[i - 1];
                    double elapsed = frame.Time - previous.Time;
                    double travelled = frame.LapDistance - previous.LapDistance;
                    Require(elapsed > 0 && elapsed <= 1 && travelled >= 0,
                        "시간·거리의 역행 또는 1초를 넘는 기록 공백이 있습니다.");
                    double plausible = Math.Max(80, Math.Max(frame.SpeedKph, previous.SpeedKph) / 3.6 * elapsed * 3 + 25);
                    Require(travelled <= plausible, "위치가 불연속인 기록은 완전한 랩으로 사용할 수 없습니다.");
                }
                // All four approved readers expose world X/Y/Z in metres, Y up.
                // A half-missing position cannot become a zero-filled racing line.
                bool position = Finite(frame.X) && Finite(frame.Z);
                if (position) hasCoordinates = true;
                // Only LMU's verified scoring path-offset field has this basis.
                // A similarly named value from another reader is not interchangeable.
                double pathLateral = canonicalGame == "LMU" && Finite(frame.PathLateralM)
                    ? frame.PathLateralM : double.NaN;
                if (Finite(pathLateral)) hasPathLateral = true;
                samples[i] = new ReferenceSample {
                    Time = frame.Time - first.Time, DistanceM = frame.LapDistance,
                    SpeedKph = frame.SpeedKph, Brake = frame.Brake, Throttle = frame.Throttle, Steering = frame.Steering,
                    X = position ? frame.X : double.NaN, Z = position ? frame.Z : double.NaN,
                    PathLateralM = pathLateral
                };
            }
            var result = new ReferenceLap {
                Game = canonicalGame, Car = first.Car, Track = first.Track,
                Layout = "unknown", GameVersion = ReferenceLap.Unknown(first.GameVersion) ? "unknown" : first.GameVersion,
                TrackLengthM = first.TrackLength, DistanceBasis = distanceBasis,
                CoordinateFrame = hasCoordinates ? coordinateFrame : "unknown",
                LateralPositionBasis = hasPathLateral ? "lmu-scoring-path-lateral-m" : "unknown",
                Author = "local driver", RecordedAt = first.CaptureUtc == DateTime.MinValue ? "unknown" : first.CaptureUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Licence = "private-user-recording", Validity = "valid", Complete = true, Samples = samples,
                Provenance = "Recorded locally from the selected game's reader. Start and finish crossings were observed by CoachEngine; boundary samples use interpolation. No car-model or track-layout alias has been inferred."
            };
            // SDK names may describe a livery/team rather than a canonical model.
            // Keep both the comparison identity and original label until the user
            // explicitly verifies an alias; never match by Porsche/992 substrings.
            result.Conditions["sourceCarLabel"] = first.Car;
            result.Conditions["sourceTrackLabel"] = first.Track;
            result.Conditions["sourceLapNumber"] = first.Lap.ToString(CultureInfo.InvariantCulture);
            result.Conditions["sourceReader"] = canonicalGame + " live shared-memory reader";
            if(canonicalGame=="ACC")
            {
                result.Conditions["sourceCourseDistance"]="Native normalizedCarPosition multiplied by current-track official Broadcasting v4 TrackMeters; not integrated speed.";
                result.Conditions["sourcePositionTiming"]="Graphics packet-counter changes are observed using the host clock. The source position timestamp and physics/graphics synchronization remain unknown; observation age is not source age.";
            }
            result.Conditions["fuelStartLitres"] = Finite(first.FuelLitres) && first.FuelLitres >= 0
                ? first.FuelLitres.ToString("R", CultureInfo.InvariantCulture) : "unknown";
            result.Conditions["tyreCompound"] = "unknown";
            result.Conditions["weather"] = "unknown";
            result.Conditions["setup"] = "unknown";
            result.Validate();
            return result;
        }
    }
}
