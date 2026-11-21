using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntsLiveCoach
{
    public sealed partial class LiveReferenceCoach
    {
        // A completed recording can be reviewed without replaying it through a fake
        // game clock. Use only the overlap admitted by the ordinary comparison UI.
        public static LiveReferenceView[] ReviewLap(ReferenceLap reference, ReferenceLap own, ComparisonResult comparison)
        {
            if (comparison == null || !comparison.Comparable || !ReferenceEquals(reference, comparison.ReferenceSource) ||
                !ReferenceEquals(own, comparison.OwnSource)) return new LiveReferenceView[0];
            var coach = new LiveReferenceCoach { reference = reference, warnings = comparison.Warnings };
            coach.BuildZones();
            var reviews = new List<LiveReferenceView>();
            foreach (var zone in coach.zones)
            {
                var first = ReferenceComparison.SampleAtDistance(own, zone.Start);
                var last = ReferenceComparison.SampleAtDistance(own, zone.End);
                if (first == null || last == null || ReferenceComparison.HasLargeGap(reference, zone.Start, zone.End) ||
                    ReferenceComparison.HasLargeGap(own, zone.Start, zone.End)) continue;
                var local = new ComparisonResult {
                    HasTimeComparison = comparison.HasTimeComparison, HasLineComparison = comparison.HasLineComparison,
                    HasLateralComparison = comparison.HasLateralComparison,
                    Points = comparison.Points.Where(p => p.DistanceM >= zone.Start && p.DistanceM <= zone.End).ToArray(),
                    BrakeZones = comparison.BrakeZones.Where(b => b.ReferenceStartM >= zone.Start && b.OwnStartM >= zone.Start &&
                        b.ReferenceReleaseM <= zone.End && b.OwnReleaseM <= zone.End).ToArray()
                };
                if (local.Points.Length < 5) continue;
                var clipped = new ReferenceLap { TimeBasis = own.TimeBasis, Samples = new[] { first }.Concat(
                    own.Samples.Where(p => p.DistanceM > zone.Start && p.DistanceM < zone.End)).Concat(new[] { last }).ToArray() };
                // Observe an unwind that runs past the reference exit. Exclude the
                // next corner and keep the elapsed time on fixed zone boundaries.
                double tail = Math.Min(zone.End + 180, Math.Min(own.Samples.Last().DistanceM, reference.Samples.Last().DistanceM));
                if (zone.Id + 1 < coach.zones.Count) tail = Math.Min(tail, coach.zones[zone.Id + 1].Start);
                if (ReferenceComparison.HasLargeGap(own, zone.End, tail) || ReferenceComparison.HasLargeGap(reference, zone.End, tail)) tail = zone.End;
                var review = coach.Diagnose(zone, clipped, own, local, first, last, tail);
                if (review != null) reviews.Add(review);
            }
            return reviews.ToArray();
        }

        sealed class InputMarker
        {
            public string Kind, Label, Principle, Drill;
            public double ReferenceM, OwnM, Resolution;
            public double Difference { get { return OwnM - ReferenceM; } }
            public double LastM { get { return Math.Max(ReferenceM, OwnM); } }
        }
        static string Meter(double value) { return value.ToString("0", CultureInfo.InvariantCulture) + " m"; }

        static double LargestDistanceStep(ReferenceLap lap, Zone zone)
        {
            double previous = double.NaN, largest = 0;
            foreach (var sample in lap.Samples.Where(p => p.DistanceM >= zone.Start && p.DistanceM <= zone.End))
            {
                if (Finite(previous)) largest = Math.Max(largest, sample.DistanceM - previous);
                previous = sample.DistanceM;
            }
            return largest;
        }
        // Detect the first sustained increase from a relatively straight approach
        // using each recording's own steering scale (wheel degrees or normalized).
        static double SteeringOnset(ReferenceLap lap, Zone zone, out double end)
        {
            end = zone.End;
            var samples = lap.Samples.Where(p => p.DistanceM >= zone.Start && p.DistanceM <= zone.End).ToArray();
            if (samples.Length < 4) return double.NaN;
            bool normalized = samples.All(p => Finite(p.Steering));
            if (!normalized && !samples.All(p => Finite(p.SteeringDegrees))) return double.NaN;
            var values = samples.Select(p => Math.Abs(normalized ? p.Steering : p.SteeringDegrees)).ToArray();
            double peak = values.Max();
            if (peak < (normalized ? .08 : 10)) return double.NaN;
            bool approach = false;
            double since = double.NaN, onset = double.NaN, settled = double.NaN;
            for (int i = 0; i < samples.Length; i++)
            {
                double distance = samples[i].DistanceM;
                if (Finite(onset))
                {
                    if (values[i] > peak * .12) { settled = double.NaN; continue; }
                    if (!Finite(settled)) settled = distance;
                    if (distance - settled >= 10) { end = settled; break; }
                    continue;
                }
                if (values[i] <= peak * .12) approach = true;
                if (!approach || values[i] < peak * .30) { since = double.NaN; continue; }
                if (!Finite(since)) since = distance;
                if (distance - since >= 10) onset = since;
            }
            return onset;
        }
        // A compound corner may include several short throttle pickups. Match
        // only a single sustained recovery in each trace following a measured lift.
        static double SingleThrottleReturn(ReferenceLap lap, Zone zone)
        {
            double liftedSince = double.NaN, highSince = double.NaN, returned = double.NaN;
            bool lifted = false;
            foreach (var p in lap.Samples.Where(s => s.DistanceM >= zone.Start && s.DistanceM <= zone.End))
            {
                if (!Finite(p.Throttle)) return double.NaN;
                if (p.Throttle <= .55)
                {
                    if (!Finite(liftedSince)) liftedSince = p.DistanceM;
                    if (p.DistanceM - liftedSince >= 10) lifted = true;
                }
                else liftedSince = double.NaN;
                if (!lifted || p.Throttle < .8) { highSince = double.NaN; continue; }
                if (!Finite(highSince)) highSince = p.DistanceM;
                if (p.DistanceM - highSince < 10) continue;
                if (Finite(returned)) return double.NaN;
                returned = highSince; lifted = false;
            }
            return returned;
        }
        static List<InputMarker> InputDifferences(ReferenceLap reference, ReferenceLap own, Zone zone, ComparisonResult result, double tolerance)
        {
            var candidates = new List<InputMarker>();
            foreach (var brake in result.BrakeZones)
            {
                if (brake.StartDifferenceResolved && Math.Abs(brake.StartDeltaM) > tolerance)
                    candidates.Add(new InputMarker { Kind = "brake-start", Label = "제동 시작", ReferenceM = brake.ReferenceStartM,
                        OwnM = brake.OwnStartM, Resolution = Math.Max(tolerance, brake.EstimatedUncertaintyM),
                        Principle = "제동을 시작하는 위치가 바뀌면 조향을 시작할 때까지 줄일 수 있는 속도가 달라져요.",
                        Drill = "같은 진입 속도에서 제동 표지 하나만 바꿔 보세요." });
                if (brake.ReleaseDifferenceResolved && Math.Abs(brake.ReleaseDeltaM) > tolerance)
                    candidates.Add(new InputMarker { Kind = "brake-release", Label = "제동 해제", ReferenceM = brake.ReferenceReleaseM,
                        OwnM = brake.OwnReleaseM, Resolution = Math.Max(tolerance, brake.EstimatedUncertaintyM),
                        Principle = "브레이크를 놓을 때 앞바퀴의 하중과 회전에 쓸 수 있는 접지가 함께 달라져요.",
                        Drill = "같은 제동 표지를 쓰고 브레이크 해제만 조금씩 바꿔 보세요." });
            }
            double refEnd, ownEnd;
            double refTurn = SteeringOnset(reference, zone, out refEnd), ownTurn = SteeringOnset(own, zone, out ownEnd);
            if (Finite(refTurn) && Finite(ownTurn) && Math.Abs(ownTurn - refTurn) > tolerance &&
                Math.Abs(ownTurn - refTurn) <= 100 && Math.Max(refTurn, ownTurn) < Math.Min(refEnd, ownEnd))
                candidates.Add(new InputMarker { Kind = "steer-onset", Label = "첫 조향", ReferenceM = refTurn, OwnM = ownTurn, Resolution = tolerance,
                    Principle = "비슷한 속도에서 조향을 시작하는 위치가 바뀌면 차가 안쪽으로 향하는 위치도 달라질 수 있어요.",
                    Drill = "진입 속도를 맞추고 첫 조향을 시작할 표지 하나만 정해 보세요." });
            double refGas = SingleThrottleReturn(reference, zone), ownGas = SingleThrottleReturn(own, zone);
            double laterGas = Math.Max(refGas, ownGas);
            var earlierTrace = refGas < ownGas ? reference : own;
            if (Finite(laterGas) && Math.Abs(ownGas - refGas) > tolerance && Math.Abs(ownGas - refGas) <= 100 &&
                earlierTrace.Samples.Where(p => p.DistanceM >= Math.Min(refGas, ownGas) && p.DistanceM <= laterGas).All(p => p.Throttle >= .65))
                candidates.Add(new InputMarker { Kind = "throttle-return", Label = "가속 복귀", ReferenceM = refGas, OwnM = ownGas, Resolution = tolerance,
                    Principle = "회전 중 가속을 올리면 타이어가 옆으로 버티면서 추진력도 내야 해 경로가 달라질 수 있어요.",
                    Drill = "같은 진입 속도에서 핸들을 풀어 가며 가속을 되살릴 지점 하나만 바꿔 보세요." });
            return candidates;
        }

        // Require a measured, initially close approach, then an enduring offset.
        // A constant offset or a single spike supplies no identifiable onset.
        static double SeparationOnset(ComparisonPoint[] points, bool world, double persistence)
        {
            double closeSince = double.NaN, farSince = double.NaN, previous = double.NaN, lastOffset = double.NaN;
            bool wasClose = false;
            foreach (var p in points)
            {
                double offset = world ? p.LineOffsetM : p.PathLateralDeltaM;
                double step = p.DistanceM - previous;
                if (!Finite(offset) || Math.Abs(offset) > 25 || step > 12 ||
                    (Finite(lastOffset) && Math.Abs(offset - lastOffset) > Math.Max(2, step * .6)))
                    return double.NaN;
                previous = p.DistanceM; lastOffset = offset;
                if (Math.Abs(offset) <= 1)
                {
                    if (!Finite(closeSince)) closeSince = p.DistanceM;
                    if (p.DistanceM - closeSince >= persistence) wasClose = true;
                }
                else closeSince = double.NaN;
                if (!wasClose || Math.Abs(offset) < 2) { farSince = double.NaN; continue; }
                if (!Finite(farSince)) farSince = p.DistanceM;
                if (p.DistanceM - farSince >= persistence) return farSince;
            }
            return double.NaN;
        }

        void ExplainDrivingDifference(LiveReferenceView view, Zone zone, ReferenceLap own, ComparisonResult result)
        {
            double refStep = LargestDistanceStep(reference, zone), ownStep = LargestDistanceStep(own, zone);
            var entry = result.Points[0];
            double approachSpeed = Math.Max(entry.Reference.SpeedKph, entry.Own.SpeedKph);
            double tolerance = Math.Max(15, refStep + ownStep) + (Finite(approachSpeed) ? approachSpeed / 3.6 * activeDistanceAge : 0);
            bool world = Finite(view.MeanAbsoluteLineOffsetM);
            if (world || result.HasLateralComparison)
                view.LineDivergenceM = SeparationOnset(result.Points, world, Math.Max(15, Math.Max(refStep, ownStep)));
            var markers = InputDifferences(reference, own, zone, result, tolerance);
            if (Finite(view.LineDivergenceM))
            {
                string path = world ? "주행선" : "게임 횡위치";
                view.LineObservation = Meter(view.LineDivergenceM) + " 부근부터 " + path + " 차이가 약 2 m 이상 이어졌습니다.";
                // Only describe an input as preceding the divergence when both
                // measured landmarks precede it by more than their resolution.
                bool similarEntry = Finite(entry.SpeedDeltaKph) && Math.Abs(entry.SpeedDeltaKph) <= Math.Max(5, entry.Reference.SpeedKph * .04);
                var preceding = markers.Where(m => similarEntry && m.LastM + m.Resolution < view.LineDivergenceM)
                    .OrderByDescending(m => m.LastM).Take(2).ToArray();
                var prior = preceding.FirstOrDefault();
                if (prior != null)
                {
                    view.EvidenceKind = prior.Kind + "-before-line";
                    view.Observation = prior.Label + " 위치가 기준보다 약 " + Meter(Math.Abs(prior.Difference)) +
                        (prior.Difference > 0 ? " 뒤였습니다. " : " 앞이었습니다. ") + view.LineObservation;
                    view.Principle = prior.Principle;
                    if (view.TimeLossSeconds > view.TimeResolutionSeconds || !result.HasTimeComparison) view.NextAction = prior.Drill;
                    else view.NextAction = "같은 표지로 이 라인을 재현해 구간 시간이 유지되는지 확인하세요.";
                    view.Estimate += " " + prior.Label + " 위치는 기준 " + Meter(prior.ReferenceM) + ", 내 주행 " + Meter(prior.OwnM) + "입니다.";
                    if (prior.Kind == "steer-onset") view.Estimate += " 각 랩에서 이 복합 구간 최대 조향 크기의 30% 이상을 10 m 이상 처음 유지한 지점입니다.";
                    if (prior.Kind == "throttle-return") view.Estimate += " 가속 페달을 55% 이하로 유지했다가 다시 80% 이상으로 10 m 동안 유지한 위치를 비교했습니다.";
                    if (preceding.Length == 2 && prior.LastM - preceding[1].LastM <= Math.Max(prior.Resolution, preceding[1].Resolution))
                    {
                        view.EvidenceKind = "overlapping-inputs-before-line";
                        view.Observation = prior.Label + "·" + preceding[1].Label + " 위치가 모두 달랐습니다. " + view.LineObservation;
                        view.Principle = "두 조작이 비슷한 위치에서 바뀌어 어느 쪽의 영향이 컸는지 나누기 어려워요.";
                        view.Estimate += " " + preceding[1].Label + " 위치는 기준 " + Meter(preceding[1].ReferenceM) + ", 내 주행 " + Meter(preceding[1].OwnM) + "입니다.";
                    }
                }
                else
                {
                    view.EvidenceKind = "line-onset";
                    if (!similarEntry && Finite(entry.SpeedDeltaKph))
                    {
                        view.Observation = "진입 속도가 약 " + Number(Math.Abs(entry.SpeedDeltaKph)) + " km/h 달랐습니다. " + view.LineObservation;
                        view.Principle = "진입 속도가 달라지면 같은 조향에서도 차가 그리는 선회 반경이 달라질 수 있어요.";
                        view.NextAction = "먼저 같은 진입 속도로 달려 두 라인이 갈라지는 위치를 다시 확인하세요.";
                    }
                    else
                    {
                        view.Observation = view.LineObservation + " 그 전에 달라진 조작은 구분하기 어렵습니다.";
                        view.Principle = "같은 코너에도 여러 경로가 가능하고 노면과 타이어에 따라 차의 반응이 달라져요.";
                        view.NextAction = "같은 진입 속도와 조향 표지로 이번 경로를 재현해 보세요.";
                    }
                }
                view.Estimate += " 가까웠던 두 기록이 벌어진 첫 지속 지점입니다. 입력 순서는 연관성을 보여주며 연료·그립·셋업도 경로에 영향을 줍니다.";
                return;
            }
            // When no comparable line is available, a delayed or early braking
            // marker can still be checked against measured speed at the same point.
            if (!result.HasTimeComparison || !(view.TimeLossSeconds > view.TimeResolutionSeconds) ||
                !Finite(entry.SpeedDeltaKph) || Math.Abs(entry.SpeedDeltaKph) > 5 || view.EvidenceKind.Length != 0) return;
            var onset = markers.FirstOrDefault(m => m.Kind == "brake-start");
            if (onset == null) return;
            var speeds = result.Points.Where(p => p.DistanceM >= onset.LastM && p.DistanceM <= onset.LastM + 15).ToArray();
            if (speeds.Length < 2 || !speeds.All(p => Finite(p.SpeedDeltaKph) && Math.Sign(onset.Difference) * p.SpeedDeltaKph > 4)) return;
            double speedDelta = speeds.Average(p => p.SpeedDeltaKph);
            view.EvidenceKind = "brake-start-speed";
            view.Observation = "제동을 약 " + Meter(Math.Abs(onset.Difference)) + (onset.Difference > 0 ? " 뒤에서" : " 앞에서") +
                " 시작했고, " + Meter(onset.LastM) + " 부근 속도는 약 " + Number(Math.Abs(speedDelta)) + (speedDelta > 0 ? " km/h 높았습니다." : " km/h 낮았습니다.");
            if (world) view.Observation += " " + view.LineObservation;
            view.Principle = onset.Principle;
            view.NextAction = onset.Drill;
        }
    }
}
