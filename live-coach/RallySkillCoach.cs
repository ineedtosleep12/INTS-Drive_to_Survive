using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntsLiveCoach
{
    // A repeatable input drill for a visible, slow virtual corner. Stage notes,
    // a route map, surface, tyre forces and the driver's desired line are unknown.
    public sealed class RallySkillCoach
    {
        internal sealed class CornerEvidence
        {
            public string Skill, Clock;
            public double? TurnInMetres, ReleaseAfterTurnInSeconds, ThrottleMinusUnwindSeconds;
            public double ApproachKph, TurnInKph, MinimumKph, ExitKph;
            public double FootBrakeAfterUnwindSeconds, LowPedalsAfterUnwindSeconds;
            public double? HandbrakeAfterUnwindSeconds;
        }
        internal CornerEvidence LastCorner { get; private set; }

        struct ApproachSample { public double Time, Speed, Brake; }
        readonly Queue<ApproachSample> approach = new Queue<ApproachSample>();
        readonly string sessionId = Guid.NewGuid().ToString("N");
        string context;
        double previousTime = double.NaN, previousDistance = double.NaN;
        double straightSince = double.NaN, turnSince = double.NaN, exitSince = double.NaN;
        double approachSpeed, entrySpeed, minimumSpeed, brakeReleasedAt;
        double minimumThrottle, peakSteer, throttleCandidate, unwindCandidate, throttleAt, unwindAt;
        double turnInMetres, releaseCandidate, initialBrakeDrop, pedalLowSince, handbrakeSince;
        double lowPedalDuration, handbrakeDuration;
        bool ready, sustainedTurn, brakedOnApproach, brakingAtTurnIn, releaseObserved;
        bool sawExitHandbrake;
        int turnDirection, sequence;
        CoachPrincipleView result, current;
        double resultUntil;

        const string Lesson = "rally-load";
        const string Limits = "속도·페달·조향 입력으로 구분한 연습 구간입니다. 코너 모양·노면·목표 속도는 미확인입니다.";
        static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        static bool Unit(double n) { return Finite(n) && n >= 0 && n <= 1; }
        static string Speed(double n) { return n.ToString("0", CultureInfo.InvariantCulture); }
        static CoachPrincipleView Cue(string title, string observed, string principle, string next, bool measured)
        {
            return new CoachPrincipleView {
                LessonId = Lesson, Title = title, Status = title, Observation = observed,
                PossibleMechanism = principle, NextDrill = next, Limits = Limits, HasObservation = measured
            };
        }

        public void Reset()
        {
            context = null; previousTime = previousDistance = double.NaN;
            result = current = null; LastCorner = null;
            ClearTurn();
        }
        void ClearTurn()
        {
            approach.Clear();
            ready = sustainedTurn = brakedOnApproach = brakingAtTurnIn = releaseObserved = false;
            straightSince = turnSince = exitSince = brakeReleasedAt = double.NaN;
            throttleCandidate = unwindCandidate = throttleAt = unwindAt = double.NaN;
            turnInMetres = releaseCandidate = initialBrakeDrop = pedalLowSince = handbrakeSince = double.NaN;
            lowPedalDuration = handbrakeDuration = 0; sawExitHandbrake = false;
        }
        static bool Usable(TelemetryFrame frame)
        {
            return frame != null && frame.OnTrack && !frame.InPit && !frame.IsReplay &&
                Finite(frame.Time) && frame.Time >= 0 && Finite(frame.SpeedKph) &&
                frame.SpeedKph >= 0 && frame.SpeedKph <= 400 && Unit(frame.Brake) &&
                Unit(frame.Throttle) && Finite(frame.Steering) && Math.Abs(frame.Steering) <= 1.01;
        }
        static CoachPrincipleView ApproachCue()
        {
            return Cue("진입 준비", "다음 느린 코너의 접근 구간을 관찰합니다.",
                "회전 전에 속도를 줄이면 타이어가 회전에 쓸 여유가 생깁니다.",
                "출구가 보이는 완만한 코너 하나를 골라 직선에서 감속해 보세요.", false);
        }
        public CoachPrincipleView Update(TelemetryFrame frame)
        {
            if (!Usable(frame))
            {
                Reset();
                return Cue("랠리 입력 대기", "", "", "스테이지에서 직접 주행하세요.", false);
            }
            string newContext = String.Join("|", frame.Car, frame.Track, frame.GameVersion, frame.TimeIsCaptureClock.ToString());
            bool restarted = Finite(previousDistance) && Finite(frame.LapDistance) && frame.LapDistance < previousDistance - 30;
            if (context != newContext || frame.Time < previousTime || frame.Time - previousTime > .5 || restarted)
                Reset();
            context = newContext;
            if (frame.Time == previousTime && current != null)
            {
                var held = current.Copy();
                held.NewFeedback = false;
                return held;
            }
            previousTime = frame.Time;
            previousDistance = frame.LapDistance;
            // Both rally readers supply a native gear with -1 denoting reverse.
            bool reversing = frame.Gear < 0 || (Finite(frame.LocalVelocityForward) && frame.LocalVelocityForward < -1);
            if (frame.SpeedKph < 8 || reversing)
            {
                ClearTurn(); result = null;
                return current = Cue("랠리 코너 연습 대기", "", "", "앞으로 주행해 접근 구간을 시작하세요.", false);
            }
            if (Finite(turnSince))
                return current = TrackTurn(frame);

            double steer = Math.Abs(frame.Steering);
            if (steer < .15 && frame.SpeedKph >= 15)
            {
                if (!Finite(straightSince)) straightSince = frame.Time;
                if (frame.Time - straightSince >= .35) ready = true;
                approach.Enqueue(new ApproachSample { Time = frame.Time, Speed = frame.SpeedKph, Brake = frame.Brake });
            }
            else straightSince = double.NaN;
            while (approach.Count > 0 && frame.Time - approach.Peek().Time > 2.5) approach.Dequeue();
            if (approach.Count == 0) ready = false;
            if (ready && steer >= .22)
            {
                // A new turn must immediately remove the previous result from the HUD.
                result = null;
                turnSince = frame.Time; turnDirection = Math.Sign(frame.Steering);
                turnInMetres = frame.LapDistance;
                entrySpeed = minimumSpeed = approachSpeed = frame.SpeedKph;
                minimumThrottle = frame.Throttle;
                peakSteer = steer;
                foreach (var p in approach)
                {
                    approachSpeed = Math.Max(approachSpeed, p.Speed);
                    if (p.Brake >= .18) brakedOnApproach = true;
                }
                brakingAtTurnIn = frame.Brake >= .18;
                return current = TrackTurn(frame);
            }
            if (result != null && frame.Time <= resultUntil) return current = result;
            return current = ApproachCue();
        }

        CoachPrincipleView TrackTurn(TelemetryFrame f)
        {
            double steer = Math.Abs(f.Steering);
            if (f.Time - turnSince > 10 || (steer >= .22 && Math.Sign(f.Steering) != turnDirection) ||
                (!sustainedTurn && steer < .15))
            {
                ClearTurn();
                return ApproachCue();
            }
            if (steer >= .22 && f.Time - turnSince >= .25) sustainedTurn = true;
            minimumSpeed = Math.Min(minimumSpeed, f.SpeedKph);
            TrackExitInputs(f, steer);
            if (brakingAtTurnIn && !releaseObserved)
            {
                if (!Finite(initialBrakeDrop) && f.Brake < .15) initialBrakeDrop = f.Time;
                if (f.Brake > .08) releaseCandidate = double.NaN;
                else if (!Finite(releaseCandidate)) releaseCandidate = f.Time;
                else if (f.Time - releaseCandidate >= .15)
                {
                    releaseObserved = true;
                    brakeReleasedAt = releaseCandidate;
                }
            }
            string principle = "속도를 줄이고 브레이크를 풀어 가면 앞뒤 하중과 회전에 쓸 수 있는 접지가 바뀝니다.";
            if (sustainedTurn && steer <= .12 && f.Brake <= .1)
            {
                if (!Finite(exitSince)) exitSince = f.Time;
                if (f.Time - exitSince >= .4) return Finish(f);
                return Cue("출구 · 핸들 풀기", "조향과 제동 입력이 작아졌습니다.", principle,
                    "출구가 열리는 동안 가속을 어떻게 다시 올리는지 느껴 보세요.", true);
            }
            exitSince = double.NaN;
            if (!sustainedTurn) return Cue("진입 · 조향 시작", "조향을 넣기 시작했습니다.", principle,
                "진입 때 남아 있는 브레이크 압력을 느껴 보세요.", true);
            if (releaseObserved) return Cue("회전 · 제동 해제 관측", "진입 때 밟던 브레이크를 풀었습니다.", principle,
                "같은 조향에서 차가 출구 쪽으로 얼마나 도는지 보세요.", true);
            return Cue("회전 · 제동과 조향", f.Brake >= .18 ? "조향 중 브레이크 입력이 이어지고 있습니다." : "조향이 이어지고 있습니다.",
                principle, "페달을 바꿀 때 차가 도는 느낌에 집중해 보세요.", true);
        }

        // Compare two observed threshold crossings. Holding a steady high pedal
        // through a bend is not a reapplication. Ignore one-frame pedal/steer spikes.
        void TrackExitInputs(TelemetryFrame f, double steer)
        {
            minimumThrottle = Math.Min(minimumThrottle, f.Throttle);
            peakSteer = Math.Max(peakSteer, steer);
            if (!Finite(throttleAt))
            {
                if (f.Throttle - minimumThrottle < .20) throttleCandidate = double.NaN;
                else if (!Finite(throttleCandidate)) throttleCandidate = f.Time;
                else if (f.Time - throttleCandidate >= .15) throttleAt = throttleCandidate;
            }
            if (!Finite(unwindAt))
            {
                if (!sustainedTurn || steer > peakSteer * .65) unwindCandidate = double.NaN;
                else if (!Finite(unwindCandidate)) unwindCandidate = f.Time;
                else if (f.Time - unwindCandidate >= .15) unwindAt = unwindCandidate;
            }
            // Only count intervals with reduced steering. A second steering build-up,
            // an unknown input or an isolated pulse cannot fill either interval.
            bool easing = Finite(unwindCandidate) && steer <= peakSteer * .65;
            if (easing && Unit(f.Handbrake)) sawExitHandbrake = true;
            TrackDuration(easing && f.Brake <= .08 && f.Throttle <= .10, f.Time, ref pedalLowSince, ref lowPedalDuration);
            TrackDuration(easing && Unit(f.Handbrake) && f.Handbrake >= .5, f.Time, ref handbrakeSince, ref handbrakeDuration);
        }
        static void TrackDuration(bool match, double now, ref double since, ref double longest)
        {
            if (!match) { since = double.NaN; return; }
            if (!Finite(since)) since = now;
            longest = Math.Max(longest, now - since);
        }
        static string Seconds(double value) { return value.ToString("0.0", CultureInfo.InvariantCulture) + "초"; }

        CoachPrincipleView Finish(TelemetryFrame f)
        {
            string principle = "진입 속도가 달라지면 같은 조향을 해도 회전에 필요한 접지가 달라집니다.";
            string next = "같은 코너에서 제동 시작점 하나를 정하고 진입 속도를 다시 맞춰 보세요.";
            string speeds = "접근 " + Speed(approachSpeed) + " → 조향 시작 " + Speed(entrySpeed) +
                " → 최저 " + Speed(minimumSpeed) + " → 탈출 " + Speed(f.SpeedKph) + " km/h.";
            string summary = speeds;
            if (releaseObserved)
            {
                summary += " 조향 시작 후 " + (brakeReleasedAt - turnSince).ToString("0.0", CultureInfo.InvariantCulture) + "초에 제동 해제.";
                if (f.TimeIsCaptureClock) summary += " (수신 시각 기준)";
                principle = "제동을 풀 때 감속 요구와 앞뒤 하중이 함께 바뀌어 차의 회전 반응이 달라집니다.";
                next = "같은 진입 속도로 브레이크를 조금 더 부드럽게 풀고 출구 방향을 비교하세요.";
            }
            else if (brakedOnApproach && !brakingAtTurnIn)
            {
                summary += " 접근 중 제동하고 조향 시작 때는 제동 입력이 작았습니다.";
                next = "다음에도 같은 진입 속도를 맞추고 핸들을 풀며 가속하는 시점만 바꿔 보세요.";
            }
            if (Finite(throttleAt) && Finite(unwindAt))
            {
                double difference = throttleAt - unwindAt;
                if (Math.Abs(difference) < .15) summary += " 조향 완화와 비슷한 때 가속 재입력.";
                else summary += " 조향 완화 " + Math.Abs(difference).ToString("0.0", CultureInfo.InvariantCulture) +
                    (difference < 0 ? "초 전" : "초 후") + " 가속 재입력.";
                if (!releaseObserved)
                {
                    principle = "핸들을 풀면 타이어가 회전에 써야 하는 힘이 줄어 가속에 쓸 여유가 생길 수 있습니다.";
                    next = "같은 코너에서 핸들을 풀 때 가속을 올리는 속도만 조금 바꿔 보세요.";
                }
            }
            var evidence = new CornerEvidence {
                Clock = f.TimeIsCaptureClock ? "capture" : "simulation",
                TurnInMetres = Finite(turnInMetres) && turnInMetres >= 0 ? (double?)turnInMetres : null,
                ApproachKph = approachSpeed, TurnInKph = entrySpeed, MinimumKph = minimumSpeed, ExitKph = f.SpeedKph,
                ReleaseAfterTurnInSeconds = releaseObserved ? (double?)(brakeReleasedAt-turnSince) : null,
                ThrottleMinusUnwindSeconds = Finite(throttleAt) && Finite(unwindAt) ? (double?)(throttleAt-unwindAt) : null,
                FootBrakeAfterUnwindSeconds = brakingAtTurnIn && Finite(initialBrakeDrop) && Finite(unwindAt)
                    ? Math.Max(0,initialBrakeDrop-unwindAt) : 0,
                LowPedalsAfterUnwindSeconds = lowPedalDuration,
                HandbrakeAfterUnwindSeconds = sawExitHandbrake ? (double?)handbrakeDuration : null
            };
            string title = "감속부터 탈출까지";
            if (handbrakeDuration >= .35)
            {
                evidence.Skill = "handbrake-through-unwind"; title = "핸들을 풀면서 핸드브레이크 유지";
                summary = "조향이 줄어든 뒤에도 핸드브레이크가 " + Seconds(handbrakeDuration) + " 동안 50% 이상이었습니다.";
                principle = "핸드브레이크를 놓으면 뒷바퀴가 다시 구르며 차를 출구 방향으로 안정시키기 쉬워질 수 있습니다.";
                next = "같은 코너에서 차가 출구 방향으로 돌 때 핸드브레이크를 놓고 반응을 비교하세요.";
            }
            else if (evidence.FootBrakeAfterUnwindSeconds >= .35)
            {
                evidence.Skill = "brake-through-unwind"; title = "핸들을 풀면서 제동 유지";
                summary = "조향 완화 뒤에도 진입 때부터 이어진 브레이크가 " + Seconds(evidence.FootBrakeAfterUnwindSeconds) + " 뒤 처음 15% 아래로 내려갔습니다.";
                principle = "조향을 줄이는 동안에도 제동을 유지하면 차는 감속을 이어갈 수 있습니다. 이 시점에 페달을 푸는 속도가 출구 속도에 영향을 줄 수 있습니다.";
                next = "같은 진입 속도에서 출구가 열릴 때 브레이크 압력을 조금 먼저 줄여 출구 속도를 비교하세요.";
            }
            else if (evidence.ThrottleMinusUnwindSeconds <= -.35)
            {
                evidence.Skill = "power-before-unwind"; title = "조향을 줄이기 전 재가속";
                summary = "조향 완화 " + Seconds(-evidence.ThrottleMinusUnwindSeconds.Value) + " 전부터 가속을 다시 올렸습니다.";
                principle = "조향이 큰 동안 구동력까지 요구하면 회전에 쓸 접지가 줄 수 있습니다. 노면과 구동 방식에 따라 반응은 달라집니다.";
                next = "같은 진입 속도로 다시 달려 이번에는 핸들을 풀기 시작할 때 가속을 보태고 출구 방향을 확인하세요.";
            }
            else if (lowPedalDuration >= .55)
            {
                evidence.Skill = "low-pedals-on-exit"; title = "출구에서 두 페달을 작게 유지";
                summary = "조향이 줄어든 뒤 " + Seconds(lowPedalDuration) + " 동안 브레이크는 8% 이하, 가속은 10% 이하였습니다.";
                principle = "두 페달이 작으면 구동력을 올리는 시점도 늦어질 수 있습니다. 출구 시야나 차의 자세를 기다릴 때도 이런 순서가 나타납니다.";
                next = "같은 출구에서 차가 안정된 표지를 정하고, 그곳부터 가속을 조금 보태 탈출 속도를 비교하세요.";
            }
            LastCorner = evidence;
            if (evidence.Skill != null) summary += " " + speeds;
            var delivered = Cue("코너 복기 · " + title, summary, principle, next, true);
            if (Finite(throttleAt) && Finite(unwindAt))
                delivered.Limits += " 가속 재입력은 선회 중 최저 페달값보다 20%p 이상 증가, 조향 완화는 관측 최대 입력의 65% 이하로 감소한 첫 지속 구간입니다.";
            if (f.TimeIsCaptureClock) delivered.Limits += " 조작 간 시간은 PC 수신 시각 기준입니다.";
            delivered.EventId = "rally-skill-" + sessionId + "-" + (++sequence);
            delivered.NewFeedback = true;
            ClearTurn();
            result = delivered.Copy(); result.NewFeedback = false;
            resultUntil = f.Time + 6;
            return delivered;
        }
    }
}
