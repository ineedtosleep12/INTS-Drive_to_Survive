using System;
using System.Globalization;

namespace IntsLiveCoach
{
    // Slip is the angle between the car body and its direction of travel.
    // AC provides the lateral magnitude, so this coach never infers a wheel
    // direction or a lateral side from the sign of that measurement.
    public sealed class DriftPracticeCoach
    {
        const double MinSlideSeconds = .35, TransitionWindow = 1.2;
        string context = "", phase = "";
        double lastTime = double.NaN, beganAt, lastSlideAt, previousEnd, previousDuration, best;
        double trendAt, trendAngle, previousTrend, turningSince = double.NaN;
        double peakForward, rotationSince = double.NaN;
        int direction, previousDirection, rotationDirection, sequence;
        bool linked;
        CoachPrincipleView lastView;

        public static bool Supports(string lessonId)
        {
            return lessonId == "drift-donut" || lessonId == "drift-balance" || lessonId == "drift-transition";
        }

        public void Reset()
        {
            context = phase = "";
            direction = previousDirection = 0;
            previousDuration = best = previousTrend = 0;
            previousEnd = lastTime = turningSince = double.NaN;
            rotationSince = double.NaN;
            rotationDirection = 0;
            peakForward = 0;
            linked = false;
            lastView = null;
        }

        static bool InRange(double v, double min, double max) { return v >= min && v <= max; }
        static string Display(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        CoachPrincipleView Show(string lessonId, string nextPhase, string title, string observation, string action, string principle, bool measured)
        {
            bool changed = phase != nextPhase;
            if (changed) sequence++;
            phase = nextPhase;
            lastView = new CoachPrincipleView {
                LessonId = lessonId, Status = "드리프트 실시간 연습", Title = title,
                Observation = observation, NextDrill = action, PossibleMechanism = principle,
                Limits = "차체와 진행 방향 사이의 각도 크기와 차체 회전을 관측합니다. 목표 원의 크기와 타이어 슬립은 별도 측정이 필요합니다.",
                HasObservation = measured, NewFeedback = measured && changed, EventId = "drift-live-" + sequence
            };
            return lastView.Copy();
        }

        CoachPrincipleView Wait(string lessonId, string reason)
        {
            Reset();
            return Show(lessonId, "wait", "차량 신호 대기", reason, "AC에서 차에 탑승하고 연습장에 진입하세요.", "", false);
        }

        static bool Motion(TelemetryFrame f, out double angle)
        {
            angle = double.NaN;
            if (!InRange(f.LocalVelocitySideAbs, 0, 150) || !InRange(f.LocalVelocityForward, -150, 200) ||
                !InRange(f.YawRate, -10, 10)) return false;
            double planar = Math.Sqrt(f.LocalVelocitySideAbs * f.LocalVelocitySideAbs + f.LocalVelocityForward * f.LocalVelocityForward);
            double groundSpeed = f.SpeedKph / 3.6;
            if (planar > groundSpeed * 1.25 + 2 || planar < groundSpeed * .65 - 1) return false;
            angle = Math.Atan2(f.LocalVelocitySideAbs, f.LocalVelocityForward) * 180 / Math.PI;
            return true;
        }

        void EndSide()
        {
            if (direction == 0) return;
            double duration = lastSlideAt - beganAt;
            if (duration >= MinSlideSeconds)
            {
                previousEnd = lastSlideAt;
                previousDirection = direction;
                previousDuration = duration;
                best = Math.Max(best, duration);
            }
            direction = 0;
            linked = false;
        }

        public CoachPrincipleView Update(TelemetryFrame frame, string lessonId)
        {
            if (!Supports(lessonId) || frame == null || !frame.OnTrack || frame.IsReplay || frame.InPit)
                return Wait(lessonId, "현재 직접 주행 중인 차량 신호를 기다립니다.");
            double angle;
            if (!InRange(frame.Time, 0, double.MaxValue) || !InRange(frame.SpeedKph, 0, 550) ||
                !InRange(frame.Throttle, 0, 1) || !Motion(frame, out angle))
                return Wait(lessonId, "속도·가속 페달·차체 속도·회전 신호를 확인 중입니다.");

            string key = string.Join("|", new[] {lessonId, frame.Car, frame.Track, frame.GameVersion, frame.TimeIsCaptureClock.ToString()});
            double now = frame.Time;
            if (context != key || now < lastTime || now - lastTime > .5)
            {
                Reset();
                context = key;
            }
            if (now == lastTime && lastView != null)
            {
                var unchanged = lastView.Copy();
                unchanged.NewFeedback = false;
                return unchanged;
            }
            lastTime = now;

            bool movingForward = frame.SpeedKph >= 12 && frame.LocalVelocityForward > 2;
            double threshold = direction == 0 ? 12 : 9; // Hysteresis around the lesson trigger.
            bool sliding = movingForward && angle >= threshold && Math.Abs(frame.YawRate) >= .18;
            if (!sliding)
            {
                bool forwardLost = frame.SpeedKph >= 12 && frame.LocalVelocityForward <= 2 &&
                    angle >= 60 && Math.Abs(frame.YawRate) >= .55;
                int currentRotation = Math.Sign(frame.YawRate);
                if (!forwardLost || currentRotation != rotationDirection) rotationSince = double.NaN;
                if (forwardLost && direction == currentRotation && peakForward >= 4 && lastSlideAt - beganAt >= MinSlideSeconds)
                {
                    rotationSince = now;
                    rotationDirection = direction;
                }
                EndSide();
                // A stop or travel in reverse interrupts a figure-eight.
                if (!movingForward) previousDirection = 0;
                bool turning = movingForward && angle < 9 && Math.Abs(frame.YawRate) >= .18;
                if (!turning) turningSince = double.NaN;
                else if (double.IsNaN(turningSince)) turningSince = now;
                if (!double.IsNaN(rotationSince) && now - rotationSince >= .2)
                    return Show(lessonId, "rotating", "전진이 줄고 차가 계속 돌아요",
                        "차체 앞쪽 속도 " + Display(frame.LocalVelocityForward * 3.6) + " km/h · 차체각 " + Display(angle) + "°",
                        "다음에는 각도가 커지기 시작할 때 가속을 더 보태지 말고 목표 원을 보세요.",
                        "회전이 계속 커지면 속도가 남아 있어도 차체 앞쪽으로 나아가는 속도는 줄어요.", true);
                if (previousDuration >= MinSlideSeconds && now - previousEnd < 3)
                {
                    string record = "직전 " + Display(previousDuration) + "초 · 이번 연습 최고 " + Display(best) + "초";
                    if (frame.SpeedKph < 12)
                        return Show(lessonId, "slow", "속도가 낮아져 시도를 마쳤어요",
                            record + " · 현재 " + Display(frame.SpeedKph) + " km/h",
                            "다음에는 일정한 속도로 원부터 잡고 작은 가속 변화 하나를 시험하세요.",
                            "아주 느릴 때는 작은 속도 변화에도 차체각의 측정값이 크게 달라져요.", true);
                    if (movingForward && angle <= 6)
                        return Show(lessonId, "straightened", "차가 진행 방향으로 펴졌어요",
                            record + " · 차체각 " + Display(angle) + "°",
                            "다음에는 차가 펴지는 만큼 카운터를 부드럽게 되돌려 보세요.",
                            "접지가 돌아올 때 반대 조향을 오래 남기면 차가 반대쪽으로 튈 수 있어요.", true);
                    return Show(lessonId, "ended", "옆미끄러짐 구간 종료",
                        record,
                        lessonId == "drift-transition" ? "다음 시도에서 첫 원을 작게 마무리하고 반대 원으로 이어 보세요." : "같은 진입에서 가속을 바꾸는 양만 조금 줄여 다시 시도하세요.",
                        "가속과 조향을 한꺼번에 바꾸면 어느 입력에 차가 반응했는지 구분하기 어려워요.", true);
                }
                if (turning && now - turningSince >= .7 && lessonId == "drift-donut")
                    return Show(lessonId, "circle", "선회 중 · 차체각이 작아요",
                        "차체각 " + Display(angle) + "° · 가속 " + Math.Round(frame.Throttle * 100) + "%",
                        frame.Throttle < .75 ? "같은 원을 그리며 가속을 조금만 보태고 뒤의 움직임을 느껴 보세요." : "같은 기어로 원의 크기를 먼저 일정하게 만들어 보세요.",
                        "후륜 구동차는 뒷바퀴가 가속과 선회를 함께 담당해 가속 변화에 따라 뒤의 움직임이 달라져요.", true);
                string goal = lessonId == "drift-transition" ? "8자 전환 준비" : lessonId == "drift-balance" ? "옆미끄러짐 유지 준비" : "원돌이 시작";
                return Show(lessonId, "prepare", goal, "속도 " + Display(frame.SpeedKph) + " km/h",
                    "빈 평지에서 한쪽 원을 그리세요. 원이 잡히면 가속을 조금씩 보태세요.",
                    "차가 향한 방향과 실제 진행 방향이 벌어지면 옆미끄러짐 각도가 커져요.", false);
            }

            turningSince = double.NaN;
            rotationSince = double.NaN;
            int nextDirection = Math.Sign(frame.YawRate);
            if (nextDirection != direction)
            {
                EndSide();
                linked = previousDirection == -nextDirection && now - previousEnd <= TransitionWindow;
                direction = nextDirection;
                beganAt = trendAt = now;
                trendAngle = angle;
                previousTrend = 0;
                peakForward = 0;
            }
            peakForward = Math.Max(peakForward, frame.LocalVelocityForward);
            lastSlideAt = now;
            if (now - trendAt >= .4)
            {
                previousTrend = angle - trendAngle;
                trendAngle = angle;
                trendAt = now;
            }
            double durationNow = now - beganAt;
            string evidence = "차체각 " + Display(angle) + "° · 한 방향 " + Display(durationNow) +
                "초 · 가속 " + Math.Round(frame.Throttle * 100) + "%";
            if (durationNow < MinSlideSeconds)
                return Show(lessonId, "onset", "차가 옆으로 움직이기 시작", evidence,
                    "가려는 원을 보면서 앞바퀴가 진행 방향을 따르도록 조향을 풀어 보세요.",
                    "뒤가 흐를 때 앞바퀴를 진행 방향에 맞추면 차가 도는 속도를 조절할 수 있어요.", true);
            if (lessonId == "drift-transition")
                return linked
                    ? Show(lessonId, "linked", "반대 방향 회전 관측", evidence,
                        "새 원에서 각도가 잡히면 가속 변화를 작게 유지하세요.",
                        "반대 회전이 자리를 잡으면 새 방향의 미끄러짐을 조절할 수 있어요.", true)
                    : Show(lessonId, "first", "첫 방향 유지", evidence,
                        "전환 지점에서 가속을 부드럽게 덜고 첫 카운터를 풀 준비를 하세요.",
                        "앞선 회전이 줄어드는 동안 조향을 풀면 반대 회전으로 이어갈 공간이 생겨요.", true);
            if (previousTrend >= 6)
                return Show(lessonId, "opening", "차체각이 커지는 중", evidence,
                    "가속을 더 보태기 전에 진행 방향과 앞바퀴가 맞는지 보세요.",
                    "차체각이 계속 커지면 차가 목표 원 안쪽으로 말려들 수 있어요.", true);
            if (previousTrend <= -6)
                return Show(lessonId, "closing", "차체각이 줄어드는 중", evidence,
                    "차가 펴지는 만큼 카운터도 부드럽게 되돌려 보세요.",
                    "차가 펴질 때까지 반대 조향을 남겨 두면 반대편으로 튈 수 있어요.", true);
            return Show(lessonId, "hold", "한 방향 옆미끄러짐 유지", evidence,
                "원 위에서 다음에 지나갈 지점을 보며 가속을 조금씩 조절하세요.",
                "가속은 뒷바퀴의 미끄러짐을, 카운터스티어는 차가 도는 정도를 함께 바꿔요.", true);
        }
    }
}
