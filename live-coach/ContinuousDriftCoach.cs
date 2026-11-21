using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntsLiveCoach
{
    // Opposite sustained yaw while travelling sideways identifies a transition.
    // The game supplies lateral speed magnitude, so the coach does not assign left/right.
    public sealed class ContinuousDriftCoach
    {
        const double ConfirmSeconds = .35, MaxTransitionGap = 1.2;
        const string Lesson = "drift-chain";
        const string Limits = "전진 중인 차체의 옆이동·회전으로 연결을 셉니다. 코너 이름, 경계, 목표 주행선은 운전자가 확인하세요.";

        public sealed class Chain
        {
            public int Attempt, Transitions;
            public double StartTime, LastSlideTime, SlideSeconds;
            public double DurationSeconds { get { return LastSlideTime - StartTime; } }
            public string EndReason = "", Clock, Track, Car;
            internal Chain Copy() { return (Chain)MemberwiseClone(); }
        }
        public sealed class Session
        {
            public Chain Current, Last, Best;
            public Chain[] Recent;
            public int FinishedChains;
        }
        readonly Queue<Chain> recent = new Queue<Chain>();
        Chain active, last, best;
        int finished, direction, candidate, previousSlide, eventNumber;
        int completedTransitions;
        double completedSlideSeconds;
        double candidateSince, lastTime = double.NaN, trendAt, trendAngle, angleChange, linkedUntil;
        string context, phase;
        CoachPrincipleView view;

        // The route exercise spans multiple independent chains, including long straights.
        internal int TotalTransitions { get { return completedTransitions + (active == null ? 0 : active.Transitions); } }
        internal double TotalSlideSeconds { get { return completedSlideSeconds + (active == null ? 0 : active.SlideSeconds); } }
        internal int LongestTransitions { get { return Math.Max(best == null ? 0 : best.Transitions, active == null ? 0 : active.Transitions); } }
        internal bool BetweenAttempts { get { return phase=="ready" || phase=="finished"; } }

        public Session Snapshot()
        {
            return new Session { Current = active == null ? null : active.Copy(), Last = last == null ? null : last.Copy(),
                Best = best == null ? null : best.Copy(), Recent = Array.ConvertAll(recent.ToArray(), item => item.Copy()),
                FinishedChains = finished };
        }
        public string ReviewText()
        {
            var lines = new List<string>(3);
            if (active != null) lines.Add("진행 중 · " + Progress(active));
            if (last != null)
            {
                string label = last == best ? "직전 · 최고" : "직전";
                lines.Add(label + " · " + Progress(last) + " · " + last.EndReason);
            }
            if (best != null && best != last)
                lines.Add("최고 · " + Progress(best) + " · " + best.EndReason);
            return string.Join(Environment.NewLine, lines.ToArray());
        }
        public CoachPrincipleView LastReport
        {
            get
            {
                if (last == null) return null;
                return new CoachPrincipleView { LessonId = Lesson, Status = Progress(last), Title = "최근 연결 주행",
                    Observation = Progress(last) + " · " + last.EndReason, HasObservation = true,
                    NextDrill = "다음 시도에서는 끊어진 굽이에서 카운터를 푸는 시점 하나만 바꿔 보세요.",
                    PossibleMechanism = "같은 진입 속도로 반복하면 조향을 바꿨을 때의 차이를 느끼기 쉬워요.", Limits = Limits };
            }
        }

        public void Reset()
        {
            active = last = best = null;
            recent.Clear(); finished = completedTransitions = 0; completedSlideSeconds = 0;
            direction = candidate = previousSlide = 0;
            lastTime = double.NaN; context = phase = null; view = null;
        }

        void Finish(string reason)
        {
            if (active != null)
            {
                active.EndReason = reason; last = active; finished++;
                completedTransitions += active.Transitions; completedSlideSeconds += active.SlideSeconds;
                if (best == null || active.Transitions > best.Transitions ||
                    (active.Transitions == best.Transitions && active.SlideSeconds > best.SlideSeconds))
                    best = active;
                recent.Enqueue(active);
                if (recent.Count > 30) recent.Dequeue();
            }
            active = null;
            direction = candidate = previousSlide = 0;
        }

        static bool Range(double n, double lower, double upper) { return n >= lower && n <= upper; }
        static string Number(double n) { return n.ToString("0.0", CultureInfo.InvariantCulture); }
        static string Progress(Chain c)
        {
            return "연결 " + c.Transitions + "회 · 옆미끄러짐 " + Number(c.SlideSeconds) + "초";
        }
        internal static bool ReadAngle(TelemetryFrame f, out double angle)
        {
            angle = double.NaN;
            if (!Range(f.Time, 0, double.MaxValue) || !Range(f.SpeedKph, 0, 550) ||
                !Range(f.LocalVelocitySideAbs, 0, 150) || !Range(f.LocalVelocityForward, -150, 200) ||
                !Range(f.YawRate, -10, 10)) return false;
            double speed = Math.Sqrt(f.LocalVelocitySideAbs*f.LocalVelocitySideAbs + f.LocalVelocityForward*f.LocalVelocityForward);
            if (!Range(speed, f.SpeedKph / 3.6 * .65 - 1, f.SpeedKph / 3.6 * 1.25 + 2)) return false;
            angle = Math.Atan2(f.LocalVelocitySideAbs, f.LocalVelocityForward) * 180 / Math.PI;
            return true;
        }
        CoachPrincipleView Cue(string id, string title, string observed, string next, string why, bool evidence)
        {
            bool changed = id != phase;
            if (changed) eventNumber++;
            phase = id;
            string status = active != null ? Progress(active) : last == null ? "연속 드리프트" : "직전 " + Progress(last);
            view = new CoachPrincipleView { LessonId = Lesson, Status = status, Title = title, Observation = observed,
                NextDrill = next, PossibleMechanism = why, Limits = Limits, HasObservation = evidence,
                NewFeedback = changed && evidence, EventId = "chain-" + eventNumber };
            return view.Copy();
        }

        public CoachPrincipleView Update(TelemetryFrame f)
        {
            double angle;
            if (f == null || !f.OnTrack || f.InPit || f.IsReplay || !ReadAngle(f, out angle))
            {
                Finish("주행 신호 중단");
                lastTime = double.NaN;
                return Cue("wait", "직접 주행 신호 대기", "", "차량에 탑승해 앞쪽으로 주행하세요.", "", false);
            }
            string key = string.Join("|", new[] {f.Car, f.Track, f.GameVersion, f.TimeIsCaptureClock.ToString()});
            if (context != key) { Reset(); context = key; }
            if (f.Time == lastTime && view != null)
            {
                var duplicate = view.Copy(); duplicate.NewFeedback = false; return duplicate;
            }
            double dt = f.Time - lastTime;
            if (dt < 0 || dt > .5) { Finish("시계 또는 수신 연속성 중단"); dt = 0; }
            lastTime = f.Time;
            if (f.SpeedKph < 12 || f.LocalVelocityForward <= 2)
            {
                Finish(f.SpeedKph < 12 ? "속도 감소" : "차체 앞방향 속도 감소");
                return Between();
            }

            int sign = Math.Sign(f.YawRate);
            double threshold = active != null && direction == sign ? 9 : 12;
            bool sliding = angle >= threshold && Math.Abs(f.YawRate) >= .18;
            // Confirm a candidate that began within the transition allowance even
            // if its confirmation falls slightly beyond that window.
            if (active != null && f.Time - active.LastSlideTime > MaxTransitionGap &&
                !(sliding && candidate == sign && candidateSince - active.LastSlideTime <= MaxTransitionGap))
                Finish("전환 시간 안에 옆미끄러짐이 이어지지 않음");

            if (!sliding)
            {
                previousSlide = candidate = 0;
                if (active == null) return Between();
                return Cue("bridge", "전환 중 · 옆미끄러짐 신호 감소", "전환 대기 " + Number(f.Time-active.LastSlideTime) + "초 · 차체각 " + Number(angle) + "°",
                    "다음 출구를 보고 차가 펴지는 만큼 기존 카운터를 풀어 보세요.",
                    "두 회전 사이에서 차체각이 작아지는 동안 앞바퀴도 새 진행 방향으로 돌아갑니다.", true);
            }

            if (active == null || direction != sign)
            {
                if (candidate != sign) { candidate = sign; candidateSince = f.Time; }
                previousSlide = 0;
                if (f.Time - candidateSince < ConfirmSeconds)
                    return Cue("candidate", active == null ? "첫 옆미끄러짐" : "반대 회전으로 넘어가는 중",
                        "차체각 " + Number(angle) + "°", "새 진행 방향을 보고 앞바퀴를 맞춰 보세요.",
                        "회전 방향이 바뀌는 순간에는 기존 카운터를 풀어야 새 방향으로 조향할 여유가 생깁니다.", true);
                bool transfer = active != null;
                if (transfer) active.Transitions++;
                else active = new Chain { Attempt = finished + 1, StartTime = candidateSince, Track = f.Track, Car = f.Car,
                    Clock = f.TimeIsCaptureClock ? "capture" : "simulation" };
                active.SlideSeconds += f.Time - candidateSince;
                active.LastSlideTime = f.Time;
                direction = previousSlide = sign; candidate = 0;
                trendAt = f.Time; trendAngle = angle; angleChange = 0;
                linkedUntil = transfer ? f.Time + 1 : double.NegativeInfinity;
            }
            else
            {
                if (previousSlide == sign) active.SlideSeconds += dt;
                active.LastSlideTime = f.Time;
                candidate = 0; previousSlide = sign;
            }
            if (f.Time - trendAt >= .4)
            {
                angleChange = angle - trendAngle;
                trendAt = f.Time; trendAngle = angle;
            }
            string evidence = "차체각 " + Number(angle) + "°";
            if (f.Time < linkedUntil)
                return Cue("linked", "다음 방향으로 연결", evidence, "새 방향에서 각도가 더 커지는지 보고 가속을 조금씩 조절하세요.",
                    "가속을 바꾸면 뒷바퀴가 옆으로 미끄러지는 정도도 달라질 수 있어요.", true);
            if (angleChange >= 7)
                return Cue("opening", "한 방향 유지 · 각도가 커져요", evidence, "가속을 더 보태기 전에 앞바퀴가 진행 방향을 따르는지 보세요.",
                    "차체 회전이 진행 방향보다 빨라지면 다음 전환을 준비할 공간이 줄어들 수 있어요.", true);
            if (angleChange <= -7)
                return Cue("closing", "한 방향 유지 · 각도가 줄어요", evidence, "다음 방향을 보며 카운터를 푸는 시점을 맞춰 보세요.",
                    "차가 펴질 때 남은 반대 조향이 다음 회전에 영향을 줘요.", true);
            return Cue("hold", "연속 주행 · 한 방향 유지", evidence, "출구를 보며 가속을 조금씩 조절하세요. 다음 굽이에서 기존 카운터를 풀어 보세요.",
                "연결은 한 방향 회전을 마치고 반대 방향의 옆미끄러짐이 이어질 때 기록됩니다.", true);
        }

        CoachPrincipleView Between()
        {
            if (last == null)
                return Cue("ready", "연속 주행 시작", "첫 옆미끄러짐을 기다립니다.", "게임 연습장에서 두 굽이가 이어지는 구간을 정하고 첫 굽이로 진입하세요.", "", false);
            return Cue("finished", "연결 주행 종료 · 다음 시도 준비", Progress(last) + " · " + last.EndReason,
                "다음 시도에서는 끊어진 굽이에서 카운터를 푸는 시점 하나만 바꿔 보세요.",
                "같은 진입 속도로 반복하면 조향을 바꿨을 때의 차이를 느끼기 쉬워요.", true);
        }
    }
}
