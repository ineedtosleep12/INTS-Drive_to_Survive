using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal sealed partial class CoachWindow
    {
        private sealed class Exercise
        {
            public readonly string LessonId, Name, FirstAction;
            public Exercise(string id, string name, string firstAction)
            {
                LessonId = id; Name = name; FirstAction = firstAction;
            }
            public override string ToString() { return Name; }
        }

        private static readonly Exercise DriftRun = new Exercise("drift-chain", "연속 드리프트 · 코너 연결",
            "두 코너를 골라 첫 코너에서 만든 미끄러짐을 다음 코너까지 이어 보세요. 끊겨도 그대로 다음 코너에서 다시 시작하세요.");
        private static readonly Exercise StageRun = new Exercise("rally-stage", "스테이지 전체 주행",
            "출발 전에 오버레이를 켜고 평소처럼 달리세요. 달리는 동안 구간별 조작과 다음에 해볼 한 가지를 보여줍니다.");
        private static readonly Exercise TougeRun = new Exercise("drift-touge", "드리프트 · 토우게 구간 반복",
            "출발과 도착 표지, 달릴 방향을 정하세요. 출발 표지에서 기록을 시작하고 도착 표지에서 마치세요.");
        private static readonly Exercise[] DriftExercises = {
            DriftRun,
            new Exercise("drift-donut", "원돌이 시작", "콘 하나를 정하고 한쪽으로 원을 그리세요. 원이 일정해지면 가속을 조금씩 보태 보세요."),
            new Exercise("drift-balance", "한 방향 유지", "같은 기어와 같은 원에서 가속을 조절하면서 옆미끄러짐이 이어지는 시간을 보세요."),
            new Exercise("drift-transition", "8자 방향 전환", "빈 평지에서 원 두 개를 정하세요. 첫 원에서 조향을 풀고 반대 원으로 넘어가 보세요."),
            TougeRun
        };
        private static readonly Exercise RallyExercise = new Exercise("rally-load", "기본 코너 · 감속부터 탈출까지",
            "출구가 보이는 느린 코너 하나를 고르세요. 직선에서 속도를 줄인 뒤 핸들을 돌리며 브레이크를 부드럽게 풀어 보세요.");
        private readonly Label exerciseLabel = new Label();
        private readonly ComboBox exerciseSelector = new ComboBox();
        private readonly Button wrcSetup = new Button();
        private readonly Button endStage = new Button();
        private readonly Button beginRun = new Button();
        private readonly TextBox stageRoute = new TextBox(), stageCar = new TextBox();
        private readonly Label routeLabel = new Label(), carLabel = new Label(), runKeyLabel = new Label();
        private readonly RowStyle runRow = new RowStyle(SizeType.Absolute, 0);
        private readonly TougeRunCoach tougeCoach = new TougeRunCoach();
        private readonly TabPage timelinePage = new TabPage("이번 주행 기록");
        private readonly RichTextBox timeline = new RichTextBox();
        private int eventRevision, displayedRevision = -1;
        private DateTime nextTimelineRefresh;
        private readonly FullStageCoach stageCoach = new FullStageCoach();
        private readonly ContinuousDriftCoach chainCoach = new ContinuousDriftCoach();
        private readonly DriftPracticeCoach driftCoach = new DriftPracticeCoach();
        private readonly RallySkillCoach rallyCoach = new RallySkillCoach();
        private sealed class ExerciseEvent
        {
            public string game, track, car, lesson, clock, progress, phase, observed, next, principle;
            public double? time;
        }
        private readonly Queue<ExerciseEvent> exerciseEvents = new Queue<ExerciseEvent>();
        private TelemetryFrame lastExerciseFrame;
        private Exercise activeExercise;
        private bool choosingExercises;

        private bool IsRun { get { return activeExercise == DriftRun || activeExercise == StageRun || activeExercise == TougeRun; } }

        private void PrepareTimeline()
        {
            timeline.ReadOnly = true; timeline.Dock = DockStyle.Fill;
            timeline.BackColor = Palette.Background; timeline.ForeColor = Palette.Text;
            timeline.Font = new Font("Malgun Gothic", 11); timeline.BorderStyle = BorderStyle.None;
            timelinePage.Padding = new Padding(18); timelinePage.BackColor = Palette.Background;
            timelinePage.Controls.Add(timeline);
            tabs.SelectedIndexChanged += delegate { displayedRevision = -1; ShowTimeline(); };
        }

        private void ShowTimeline()
        {
            if (tabs.SelectedTab != timelinePage) return;
            ExerciseEvent[] events;
            string review;
            lock (engineLock)
            {
                if (displayedRevision == eventRevision && DateTime.UtcNow < nextTimelineRefresh) return;
                nextTimelineRefresh = DateTime.UtcNow.AddSeconds(2);
                events = activeExercise == DriftRun || activeExercise == RallyExercise ? exerciseEvents.ToArray() : new ExerciseEvent[0];
                displayedRevision = eventRevision;
                review = activeExercise == TougeRun ? tougeCoach.ReviewText() :
                    activeExercise == DriftRun ? chainCoach.ReviewText() :
                    activeExercise == StageRun ? stageCoach.ReviewText() :
                    selectedGame == "AC" ? driftCoach.ReviewText() : "";
            }
            var lines = new StringBuilder();
            if (!String.IsNullOrEmpty(review)) { lines.AppendLine(review); lines.AppendLine(); }
            // Newest observations first so that the last corner is easy to find at the finish.
            for (int i = events.Length - 1; i >= 0; --i)
            {
                var e = events[i];
                if (activeExercise == null || e.lesson != activeExercise.LessonId) continue;
                lines.AppendLine(e.phase + (String.IsNullOrEmpty(e.progress) ? "" : "   ·   " + e.progress));
                lines.AppendLine(e.observed);
                if (!String.IsNullOrEmpty(e.principle)) lines.AppendLine(e.principle);
                lines.AppendLine(e.next);
                lines.AppendLine();
            }
            string updated = lines.Length > 0 ? lines.ToString() : "주행하면 관측한 코너와 드리프트 연결 기록을 여기서 다시 볼 수 있습니다.";
            if (timeline.Text != updated) timeline.Text = updated;
        }

        private void AddExerciseSelector(FlowLayoutPanel bar)
        {
            exerciseLabel.Text = "과제"; exerciseLabel.AutoSize = true;
            exerciseLabel.Margin = new Padding(16, 5, 6, 0);
            exerciseSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            exerciseSelector.Width = 240;
            exerciseSelector.AccessibleName = "실시간 코칭 과제";
            bar.Controls.Add(exerciseLabel); bar.Controls.Add(exerciseSelector);
            exerciseLabel.Visible = exerciseSelector.Visible = false;
            exerciseSelector.SelectedIndexChanged += delegate { if (!choosingExercises) SelectExercise(); };
        }

        private void AddWrcSetupButton(FlowLayoutPanel bar)
        {
            StyleButton(wrcSetup, "EA WRC 데이터 전송 켜기", 200);
            wrcSetup.Visible = false;
            wrcSetup.Click += delegate {
                string message;
                bool success = WrcPracticeSetup.TryEnable(out message);
                MessageBox.Show(this, message, "EA WRC 데이터 전송", MessageBoxButtons.OK,
                    success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };
            bar.Controls.Add(wrcSetup);
        }

        private Control BuildRunControls()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            routeLabel.Text = "스테이지·방향"; carLabel.Text = "차량";
            foreach (var label in new[] { routeLabel, carLabel, runKeyLabel })
            { label.AutoSize = true; label.Margin = new Padding(3, 7, 3, 0); }
            stageRoute.Width = 143; stageCar.Width = 113;
            stageRoute.MaxLength = stageCar.MaxLength = 80;
            stageRoute.AccessibleName = "반복할 스테이지와 방향"; stageCar.AccessibleName = "반복할 차량";
            StyleButton(beginRun, "이번 시도 시작", 117);
            StyleButton(endStage, "이번 스테이지 종료", 142);
            bar.Controls.AddRange(new Control[] {routeLabel, stageRoute, carLabel, stageCar, beginRun, endStage, runKeyLabel});
            beginRun.Click += delegate { BeginRunObservation(); }; endStage.Click += delegate { EndStageObservation(); };
            stageRoute.TextChanged += delegate { UpdateRunControls(); }; stageCar.TextChanged += delegate { UpdateRunControls(); };
            return bar;
        }

        private void UpdateRunControls()
        {
            bool rally = activeExercise == StageRun, touge = activeExercise == TougeRun;
            float height = rally || touge ? 44 : 0;
            if (runRow.Height != height) runRow.Height = height;
            runKeyLabel.Visible = beginRun.Visible = endStage.Visible = rally || touge;
            runKeyLabel.Text = runHotkey ? "Ctrl+Alt+R" : "";
            runKeyLabel.AccessibleName = "시도 시작 또는 종료 단축키";
            routeLabel.Visible = stageRoute.Visible = carLabel.Visible = stageCar.Visible = rally;
            lock (engineLock)
            {
                bool namedRun = rally && stageCoach.RepeatAttemptOpen;
                bool pending = rally && stageCoach.RepeatWaitingForStart;
                endStage.Text = touge ? "구간 종료" : pending ? "시작 대기 취소" : "이번 스테이지 종료";
                stageRoute.ReadOnly = stageCar.ReadOnly = namedRun;
                beginRun.Enabled = touge ? !tougeCoach.IsRecording : rally && !namedRun &&
                    !String.IsNullOrWhiteSpace(stageRoute.Text) && !String.IsNullOrWhiteSpace(stageCar.Text);
                endStage.Enabled = touge ? tougeCoach.CanFinishManually : rally && (pending || stageCoach.CanFinishManually);
            }
        }

        private void BeginRunObservation()
        {
            lock (engineLock)
            {
                if (activeExercise == StageRun)
                {
                    if (stageCoach.RepeatAttemptOpen || !stageCoach.ArmRepeat(stageRoute.Text, stageCar.Text)) return;
                }
                else if (activeExercise == TougeRun && !tougeCoach.IsRecording) tougeCoach.Start();
                else return;
                generation++; eventRevision++;
            }
            if (!overlay.Visible) overlay.Show();
            show.Text = "오버레이 숨기기"; ShowExerciseStart(); ShowTimeline();
        }

        private void ToggleRunFromGame()
        {
            if (activeExercise != StageRun && activeExercise != TougeRun) return;
            bool started;
            lock (engineLock) started = activeExercise == TougeRun ? tougeCoach.IsRecording : stageCoach.RepeatAttemptOpen;
            if (started) EndStageObservation();
            else if (beginRun.Enabled) BeginRunObservation();
            else if (endStage.Enabled) EndStageObservation();
        }

        private void RefreshExercises()
        {
            choosingExercises = true;
            try
            {
                exerciseSelector.Items.Clear();
                if (selectedGame == "AC" && selectedDiscipline == DrivingDiscipline.Drift)
                    exerciseSelector.Items.AddRange(DriftExercises);
                if ((selectedGame == "EA WRC" || selectedGame == "AC Rally") && selectedDiscipline == DrivingDiscipline.Rally)
                {
                    exerciseSelector.Items.Add(StageRun);
                    exerciseSelector.Items.Add(RallyExercise);
                }
                bool available = exerciseSelector.Items.Count > 0;
                if (available) { exerciseSelector.Items.Add("자유 주행 · 원리 코칭"); exerciseSelector.SelectedIndex = 0; }
                exerciseLabel.Visible = exerciseSelector.Visible = available;
                wrcSetup.Visible = selectedGame == "EA WRC";
            }
            finally { choosingExercises = false; }
            lock (engineLock) { exerciseEvents.Clear(); eventRevision++; }
            SelectExercise();
        }

        private void ResetExerciseObservation()
        {
            driftCoach.Reset();
            rallyCoach.Reset();
            stageCoach.Reset();
            tougeCoach.Reset();
            chainCoach.Reset();
            lastExerciseFrame = null;
        }

        private void InterruptExerciseObservation()
        {
            InterruptBrakePracticeObservation();
            driftCoach.Interrupt(); rallyCoach.Reset();
            if (activeExercise == StageRun)
            {
                var prior = stageCoach.LastReport;
                stageCoach.Interrupt();
                var report = stageCoach.LastReport;
                if (report != null && (prior == null || prior.EventId != report.EventId))
                    SaveExerciseEvent(lastExerciseFrame, report);
            }
            else if (activeExercise == TougeRun)
            {
                var prior = tougeCoach.LastReport;
                tougeCoach.Interrupt();
                var report = tougeCoach.LastReport;
                if (report != null && (prior == null || prior.EventId != report.EventId))
                    SaveExerciseEvent(lastExerciseFrame, report);
            }
            else if (activeExercise == DriftRun)
            {
                bool wasActive = chainCoach.Snapshot().Current != null;
                chainCoach.Update(null);
                if (wasActive) SaveExerciseEvent(lastExerciseFrame, chainCoach.LastReport);
            }
        }

        private CoachPrincipleView RetainedReport()
        {
            if (activeExercise == TougeRun) return tougeCoach.LastReport;
            if (activeExercise == DriftRun) return chainCoach.LastReport;
            if (activeExercise != null && selectedGame == "AC") return driftCoach.LastReport;
            return activeExercise == StageRun ? stageCoach.LastReport : null;
        }

        private void EndStageObservation()
        {
            CoachPrincipleView recap = null;
            lock (engineLock)
            {
                if (activeExercise == StageRun && stageCoach.CancelPendingRepeat()) eventRevision++;
                else if (activeExercise == TougeRun && tougeCoach.CanFinishManually) recap = tougeCoach.FinishManually();
                else if (activeExercise == StageRun && stageCoach.CanFinishManually) recap = stageCoach.FinishManually();
                else return;
                if (recap != null) SaveExerciseEvent(lastExerciseFrame, recap);
                generation++;
            }
            if (recap != null) ApplyExercise(null, recap);
            else { ShowExerciseStart(); ShowTimeline(); }
        }

        private void SelectExercise()
        {
            lock (engineLock)
            {
                activeExercise = exerciseSelector.SelectedItem as Exercise;
                ResetExerciseObservation(); generation++;
            }
            lastPrinciple = null; feedbackUntil = DateTime.MinValue;
            lastSpokenId = ""; lastSpokenUtc = DateTime.MinValue; speech.SpeakAsyncCancelAll();
            ConfigureRunHotkey();
            UpdateRunControls();
            if (activeExercise != null && !tabs.TabPages.Contains(timelinePage)) tabs.TabPages.Add(timelinePage);
            if (activeExercise == null && tabs.TabPages.Contains(timelinePage)) tabs.TabPages.Remove(timelinePage);
            displayedRevision = -1;
            if (activeExercise == null) { ClearLiveOverlay(); return; }
            string guideLesson = activeExercise == DriftRun || activeExercise == TougeRun ? "drift-transition" : activeExercise == StageRun ? "rally-load" : activeExercise.LessonId;
            foreach (PrincipleLesson lesson in lessons.Items)
                if (lesson.Id == guideLesson) { lessons.SelectedItem = lesson; break; }
            ShowExerciseStart();
        }

        private void ShowExerciseStart()
        {
            UpdateRunControls();
            string venue = activeExercise == TougeRun ? "AC · Drift Playground 2021 또는 Trento-Bondone에서 같은 차로 출발·도착 표지를 정하세요."
                : activeExercise == DriftRun ? "AC · Drift Playground 2021에서 굽이 두 개를 고르거나 Skidpad 0.5에서 8자를 그려 보세요."
                : selectedGame == "AC" ? "AC · Skidpad 0.5 또는 Drift Playground 2021의 빈 공간 / AE86 Drift나 사용하는 후륜 모드 차량"
                : selectedGame == "EA WRC" ? "EA WRC · 에스토니아 Vissi에서 연습할 수 있습니다."
                : "AC Rally · 웨일스 Hafren South에서 연습할 수 있습니다.";
            stage.Text = "선택한 과제 · " + activeExercise.Name;
            feedback.Text = activeExercise.Name;
            action.Text = activeExercise.FirstAction;
            lock (engineLock)
            {
                if (activeExercise == StageRun) action.Text = stageCoach.RepeatAttemptOpen ?
                    stageRoute.Text + " / " + stageCar.Text + " · 반복 기록 준비 완료. 출발하세요." :
                    "두 시도를 비교하려면 스테이지·방향과 차량을 입력하고 출발 전에 ‘이번 시도 시작’을 누르세요.";
                if (activeExercise == TougeRun && tougeCoach.IsRecording) action.Text = "기록 준비 완료. 출발 표지에서 달리기 시작하세요.";
            }
            detail.Text = venue + "\n차량에 탑승한 뒤 오버레이를 확인하세요.";
            overlay.UpdateExercise(null, activeExercise.Name, "주행 데이터 대기", venue, action.Text, "");
        }

        // Called under engineLock. Keep only changes of phase or completed turns for export.
        private CoachPrincipleView ObserveExercise(TelemetryFrame frame)
        {
            if (activeExercise == null) return null;
            lastExerciseFrame = frame;
            CoachPrincipleView cue;
            if (activeExercise == StageRun) cue = stageCoach.Update(frame);
            else if (activeExercise == TougeRun) cue = tougeCoach.Update(frame);
            else if (activeExercise == DriftRun) cue = chainCoach.Update(frame);
            else if (activeExercise == RallyExercise) cue = rallyCoach.Update(frame);
            else cue = driftCoach.Update(frame, activeExercise.LessonId);
            if (cue.NewFeedback) SaveExerciseEvent(frame, cue);
            return cue;
        }

        private void SaveExerciseEvent(TelemetryFrame frame, CoachPrincipleView cue)
        {
            exerciseEvents.Enqueue(new ExerciseEvent { game=selectedGame, track=frame == null ? "" : frame.Track,
                car=frame == null ? "" : frame.Car, lesson=activeExercise.LessonId,
                time=frame == null ? (double?)null : frame.Time, clock=frame == null ? "user" : frame.TimeIsCaptureClock ? "capture" : "simulation",
                progress=IsRun ? cue.Status : "", phase=cue.Title, observed=cue.Observation, next=cue.NextDrill, principle=cue.PossibleMechanism });
            if (exerciseEvents.Count > 500) exerciseEvents.Dequeue();
            eventRevision++;
        }

        private string ExportExercises()
        {
            return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(new {schemaVersion=3, events=exerciseEvents.ToArray(),
                stage=activeExercise == StageRun ? stageCoach.Snapshot() : null,
                touge=activeExercise == TougeRun ? tougeCoach.Snapshot() : null,
                drift=activeExercise == DriftRun ? chainCoach.Snapshot() : null});
        }

        private void ApplyExercise(TelemetryFrame frame, CoachPrincipleView cue)
        {
            export.Enabled = haveFrames;
            stage.Text = IsRun ? activeExercise.Name + "   ·   " + cue.Status : "선택한 과제 · " + activeExercise.Name;
            feedback.Text = cue.Title;
            string notes = string.Join("\n", new[] {cue.Observation, cue.PossibleMechanism, cue.Limits});
            if (detail.Text != notes) detail.Text = notes;
            action.Text = cue.NextDrill;
            overlay.UpdateExercise(frame, activeExercise.Name, cue.Title, cue.Observation, cue.NextDrill, cue.PossibleMechanism, IsRun ? cue.Status : null);
            UpdateRunControls();
            ShowTimeline();
            if (frame == null || !cue.HasObservation || frame.InPit || frame.IsReplay || !frame.OnTrack)
            {
                speech.SpeakAsyncCancelAll();
                return;
            }
            if (voice.Checked && cue.NewFeedback && cue.EventId != lastSpokenId &&
                (DateTime.UtcNow - lastSpokenUtc).TotalSeconds >= 6)
            {
                lastSpokenId = cue.EventId; lastSpokenUtc = DateTime.UtcNow;
                speech.SpeakAsyncCancelAll(); speech.SpeakAsync(cue.NextDrill);
            }
        }
    }
}
