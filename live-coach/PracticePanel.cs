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
        private static readonly Exercise[] DriftExercises = {
            DriftRun,
            new Exercise("drift-donut", "원돌이 시작", "콘 하나를 정하고 한쪽으로 원을 그리세요. 원이 일정해지면 가속을 조금씩 보태 보세요."),
            new Exercise("drift-balance", "한 방향 유지", "같은 기어와 같은 원에서 가속을 조절하면서 옆미끄러짐이 이어지는 시간을 보세요."),
            new Exercise("drift-transition", "8자 방향 전환", "빈 평지에서 원 두 개를 정하세요. 첫 원에서 조향을 풀고 반대 원으로 넘어가 보세요.")
        };
        private static readonly Exercise RallyExercise = new Exercise("rally-load", "기본 코너 · 감속부터 탈출까지",
            "출구가 보이는 느린 코너 하나를 고르세요. 직선에서 속도를 줄인 뒤 핸들을 돌리며 브레이크를 부드럽게 풀어 보세요.");
        private readonly Label exerciseLabel = new Label();
        private readonly ComboBox exerciseSelector = new ComboBox();
        private readonly Button wrcSetup = new Button();
        private readonly Button endStage = new Button();
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

        private bool IsRun { get { return activeExercise == DriftRun || activeExercise == StageRun; } }

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
                events = activeExercise == DriftRun ? exerciseEvents.ToArray() : new ExerciseEvent[0];
                displayedRevision = eventRevision;
                review = activeExercise == DriftRun ? chainCoach.ReviewText() : stageCoach.ReviewText();
            }
            var lines = new StringBuilder();
            if (!String.IsNullOrEmpty(review)) { lines.AppendLine(review); lines.AppendLine(); }
            // Newest observations first so that the last corner is easy to find at the finish.
            for (int i = events.Length - 1; i >= 0; --i)
            {
                var e = events[i];
                if (e.lesson != DriftRun.LessonId) continue;
                lines.AppendLine(e.phase + (String.IsNullOrEmpty(e.progress) ? "" : "   ·   " + e.progress));
                lines.AppendLine(e.observed);
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
            StyleButton(endStage, "이번 스테이지 종료", 155);
            endStage.Visible = endStage.Enabled = false;
            endStage.Click += delegate { EndStageObservation(); };
            bar.Controls.Add(endStage);
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
            chainCoach.Reset();
            lastExerciseFrame = null;
        }

        private void InterruptExerciseObservation()
        {
            driftCoach.Reset(); rallyCoach.Reset();
            if (activeExercise == StageRun)
            {
                var prior = stageCoach.LastReport;
                stageCoach.Interrupt();
                var report = stageCoach.LastReport;
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
            if (activeExercise == DriftRun) return chainCoach.LastReport;
            return activeExercise == StageRun ? stageCoach.LastReport : null;
        }

        private void EndStageObservation()
        {
            CoachPrincipleView recap;
            lock (engineLock)
            {
                if (activeExercise != StageRun || !stageCoach.CanFinishManually) return;
                recap = stageCoach.FinishManually();
                if (recap == null) return;
                SaveExerciseEvent(lastExerciseFrame, recap);
                generation++;
            }
            ApplyExercise(null, recap);
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
            endStage.Visible = activeExercise == StageRun;
            endStage.Enabled = false;
            if (IsRun && !tabs.TabPages.Contains(timelinePage)) tabs.TabPages.Add(timelinePage);
            if (!IsRun && tabs.TabPages.Contains(timelinePage)) tabs.TabPages.Remove(timelinePage);
            if (activeExercise == null) { ClearLiveOverlay(); return; }
            string guideLesson = activeExercise == DriftRun ? "drift-transition" : activeExercise == StageRun ? "rally-load" : activeExercise.LessonId;
            foreach (PrincipleLesson lesson in lessons.Items)
                if (lesson.Id == guideLesson) { lessons.SelectedItem = lesson; break; }
            ShowExerciseStart();
        }

        private void ShowExerciseStart()
        {
            string venue = activeExercise == DriftRun ? "AC · Drift Playground 2021 또는 원하는 토우게 / 후륜 드리프트 차량"
                : selectedGame == "AC" ? "AC · Drift 코스 / Toyota AE86 Drift"
                : selectedGame == "EA WRC" ? "EA WRC · 에스토니아 Vissi에서 연습할 수 있습니다."
                : "AC Rally · 웨일스 Hafren South에서 연습할 수 있습니다.";
            stage.Text = "선택한 과제 · " + activeExercise.Name;
            feedback.Text = activeExercise.Name;
            action.Text = activeExercise.FirstAction;
            detail.Text = venue + "\n차량에 탑승한 뒤 오버레이 보기를 누르세요.";
            overlay.UpdateExercise(null, activeExercise.Name, "주행 데이터 대기", venue, activeExercise.FirstAction, "");
        }

        // Called under engineLock. Keep only changes of phase or completed turns for export.
        private CoachPrincipleView ObserveExercise(TelemetryFrame frame)
        {
            if (activeExercise == null) return null;
            lastExerciseFrame = frame;
            CoachPrincipleView cue;
            if (activeExercise == StageRun) cue = stageCoach.Update(frame);
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
            return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(new {schemaVersion=2, events=exerciseEvents.ToArray(),
                stage=activeExercise == StageRun ? stageCoach.Snapshot() : null,
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
            lock (engineLock) endStage.Enabled = activeExercise == StageRun && stageCoach.CanFinishManually;
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
