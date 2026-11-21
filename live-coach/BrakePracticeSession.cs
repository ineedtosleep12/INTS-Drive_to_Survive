using System;

namespace IntsLiveCoach
{
    internal sealed partial class CoachWindow
    {
        readonly BrakeReleaseExperiment brakeExperiment = new BrakeReleaseExperiment();
        BrakePracticeCorner activeBrakeCorner;
        bool brakePracticeRunning;
        string brakeMismatchMessage = "";
        double brakeResultAt = double.NaN;
        int brakeResultLap = -1;
        TelemetryFrame lastBrakeContext;
        ReferenceLap latestBrakeCapture;

        // The reader owns the game clock. A previous session's last lap may still be
        // useful for choosing gates, but must never be called the previous attempt.
        bool CheckBrakeCaptureContext(TelemetryFrame frame)
        {
            var old = lastBrakeContext;
            lastBrakeContext = frame;
            bool changed = old != null && (old.Track != frame.Track || old.Car != frame.Car ||
                old.GameVersion != frame.GameVersion || old.TrackLength != frame.TrackLength ||
                frame.Time < old.Time || frame.Lap < old.Lap);
            if (changed || frame.InPit || frame.IsReplay || !frame.OnTrack) latestBrakeCapture = null;
            return changed;
        }

        void BeginBrakePractice(BrakePracticeCorner corner, ReferenceLap nativeLap, bool useAsPrevious)
        {
            if (corner == null || selectedGame != "LMU" || selectedDiscipline != DrivingDiscipline.Race) return;
            lock (engineLock)
            {
                if (!brakeExperiment.Arm(corner.Name, corner.StartM, corner.EndM)) return;
                activeBrakeCorner = corner;
                brakePracticeRunning = true;
                brakeMismatchMessage = "";
                brakeResultAt = double.NaN;
                brakeResultLap = -1;
                if (useAsPrevious && nativeLap != null && ReferenceEquals(nativeLap, latestBrakeCapture) && corner.MatchesFrame(lastBrakeContext))
                    brakeExperiment.SeedFromLap(nativeLap);
                generation++;
            }
            lastPrinciple = null;
            feedbackUntil = DateTime.MinValue;
            speech.SpeakAsyncCancelAll();
            if (!overlay.Visible) overlay.Show();
            show.Text = "오버레이 숨기기";
            DisplayBrakePractice(disconnected ? null : lastBrakeContext);
        }

        void EndBrakePractice()
        {
            lock (engineLock)
            {
                if (!brakePracticeRunning) return;
                brakeExperiment.Stop();
                brakePracticeRunning = false;
                activeBrakeCorner = null;
                brakeMismatchMessage = "";
                brakeResultAt = double.NaN;
                generation++;
            }
            ClearLiveOverlay();
        }

        void ResetBrakePractice()
        {
            EndBrakePractice();
            lock (engineLock) brakeExperiment.Reset();
        }

        // Called from the polling thread while engineLock is held.
        void InterruptBrakePracticeObservation()
        {
            lastBrakeContext = null;
            latestBrakeCapture = null;
            if (!brakePracticeRunning) return;
            brakeExperiment.Interrupt();
            brakeMismatchMessage = "";
            brakeResultAt = double.NaN;
        }

        void ObserveBrakePracticeFrame(TelemetryFrame frame)
        {
            if (!brakePracticeRunning) return;
            if (!activeBrakeCorner.MatchesFrame(frame))
            {
                brakeExperiment.Interrupt();
                brakeResultAt = double.NaN;
                brakeMismatchMessage = "선택한 차량·레이아웃·게임 버전과 같은 코스 위치를 기다립니다.";
                return;
            }
            brakeMismatchMessage = "";
            if (brakeExperiment.Update(frame))
            {
                brakeResultAt = frame.Time;
                brakeResultLap = brakeExperiment.Latest.Lap;
            }
            if (frame.IsReplay || frame.InPit || !frame.OnTrack || !frame.IsValidLap)
                brakeResultAt = double.NaN;
        }

        bool DisplayBrakePractice(TelemetryFrame frame)
        {
            string pending = BrakePracticePendingText;
            string heading, observed, next, principle, progress;
            lock (engineLock)
            {
                if (!brakePracticeRunning && string.IsNullOrEmpty(pending)) return false;
                export.Enabled = haveFrames || brakeExperiment.Count > 0;
                if (!brakePracticeRunning)
                {
                    heading = "몬자 첫 시케인 · 내 기준 랩 준비";
                    observed = pending;
                    next = "LMU에서 포르쉐 992 GT3 R로 출발선을 지난 뒤 한 바퀴를 완주하세요.";
                    if (SelectedBrakeCorner != null)
                        next = "‘브레이크 해제 실험’ 탭에서 표시된 팀 차량이 992인지 확인한 뒤 시작하세요.";
                    else if (frame != null && (!BrakePracticeCourse.IsMonzaTrack(frame.Track, frame.TrackLength) ||
                        BrakePracticeCourse.IsKnownOtherCar(frame.Car)))
                        observed = "현재 " + frame.Track + " / " + frame.Car + " · 몬자 WEC의 포르쉐 992 주행을 기다립니다.";
                    principle = "첫 유효 랩에서 첫 제동과 두 번째 방향 전환을 찾아 측정 지점을 정합니다.";
                    progress = "";
                }
                else
                {
                    var corner = activeBrakeCorner;
                    bool result = frame != null && frame.Lap == brakeResultLap &&
                        frame.Time >= brakeResultAt && frame.Time - brakeResultAt <= 12 &&
                        frame.Brake < .12 && Math.Abs(frame.Steering) < .25;
                    heading = corner.Name + (result ? " · 탈출 결과" : " · 브레이크 해제");
                    observed = result ? brakeExperiment.ResultText :
                        frame == null ? "LMU 주행 데이터 대기" :
                        brakeMismatchMessage.Length > 0 ? brakeMismatchMessage : brakeExperiment.Status;
                    next = result ? brakeExperiment.ComparisonNote :
                        "진입 속도와 제동 시작점을 맞추고 첫 제동을 푸는 속도만 바꿔 보세요.";
                    principle = result ? "자세한 수치와 직전 시도는 ‘브레이크 해제 실험’ 탭에서 확인하세요." :
                        "페달이 60%에서 10%로 내려오는 시간과 고정된 탈출 지점의 속도를 잽니다.";
                    progress = string.Format("기록 {0}회   ·   구간 {1:N0}–{2:N0} m", brakeExperiment.Count, corner.StartM, corner.EndM);
                }
            }
            stage.Text = "브레이크 해제 실험";
            feedback.Text = heading;
            if (detail.Text != observed) detail.Text = observed;
            action.Text = next;
            overlay.UpdateExercise(frame, "한 코너 반복 연습", heading, observed, next, principle, progress);
            return true;
        }
    }
}
