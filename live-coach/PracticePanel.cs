using System;
using System.Collections.Generic;
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

        private static readonly Exercise[] DriftExercises = {
            new Exercise("drift-donut", "원돌이 시작", "콘 하나를 정하고 한쪽으로 원을 그리세요. 원이 일정해지면 가속을 조금씩 보태 보세요."),
            new Exercise("drift-balance", "한 방향 유지", "같은 기어와 같은 원에서 가속을 조절하면서 옆미끄러짐이 이어지는 시간을 보세요."),
            new Exercise("drift-transition", "8자 방향 전환", "빈 평지에서 원 두 개를 정하세요. 첫 원에서 조향을 풀고 반대 원으로 넘어가 보세요.")
        };
        private static readonly Exercise RallyExercise = new Exercise("rally-load", "기본 코너 · 감속부터 탈출까지",
            "출구가 보이는 느린 코너 하나를 고르세요. 직선에서 속도를 줄인 뒤 핸들을 돌리며 브레이크를 부드럽게 풀어 보세요.");
        private readonly Label exerciseLabel = new Label();
        private readonly ComboBox exerciseSelector = new ComboBox();
        private readonly Button wrcSetup = new Button();
        private readonly DriftPracticeCoach driftCoach = new DriftPracticeCoach();
        private readonly RallySkillCoach rallyCoach = new RallySkillCoach();
        private readonly Queue<object> exerciseEvents = new Queue<object>();
        private Exercise activeExercise;
        private bool choosingExercises;

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

        private void RefreshExercises()
        {
            choosingExercises = true;
            try
            {
                exerciseSelector.Items.Clear();
                if (selectedGame == "AC" && selectedDiscipline == DrivingDiscipline.Drift)
                    exerciseSelector.Items.AddRange(DriftExercises);
                if ((selectedGame == "EA WRC" || selectedGame == "AC Rally") && selectedDiscipline == DrivingDiscipline.Rally)
                    exerciseSelector.Items.Add(RallyExercise);
                bool available = exerciseSelector.Items.Count > 0;
                if (available) { exerciseSelector.Items.Add("자유 주행 · 원리 코칭"); exerciseSelector.SelectedIndex = 0; }
                exerciseLabel.Visible = exerciseSelector.Visible = available;
                wrcSetup.Visible = selectedGame == "EA WRC";
            }
            finally { choosingExercises = false; }
            lock (engineLock) exerciseEvents.Clear();
            SelectExercise();
        }

        private void ResetExerciseObservation()
        {
            driftCoach.Reset();
            rallyCoach.Reset();
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
            if (activeExercise == null) { ClearLiveOverlay(); return; }
            foreach (PrincipleLesson lesson in lessons.Items)
                if (lesson.Id == activeExercise.LessonId) { lessons.SelectedItem = lesson; break; }
            ShowExerciseStart();
        }

        private void ShowExerciseStart()
        {
            string venue = selectedGame == "AC" ? "AC · Drift 코스 / Toyota AE86 Drift"
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
            CoachPrincipleView cue = activeExercise == RallyExercise ? rallyCoach.Update(frame)
                : driftCoach.Update(frame, activeExercise.LessonId);
            if (cue.NewFeedback)
            {
                exerciseEvents.Enqueue(new {game=selectedGame, track=frame.Track, car=frame.Car, lesson=cue.LessonId,
                    time=frame.Time, clock=frame.TimeIsCaptureClock ? "capture" : "simulation",
                    phase=cue.Title, observed=cue.Observation, next=cue.NextDrill, principle=cue.PossibleMechanism});
                if (exerciseEvents.Count > 100) exerciseEvents.Dequeue();
            }
            return cue;
        }

        private string ExportExercises()
        {
            return new JavaScriptSerializer().Serialize(new {schemaVersion=1, events=exerciseEvents.ToArray()});
        }

        private void ApplyExercise(TelemetryFrame frame, CoachPrincipleView cue)
        {
            export.Enabled = haveFrames;
            stage.Text = "선택한 과제 · " + activeExercise.Name;
            feedback.Text = cue.Title;
            string notes = string.Join("\n", new[] {cue.Observation, cue.PossibleMechanism, cue.Limits});
            if (detail.Text != notes) detail.Text = notes;
            action.Text = cue.NextDrill;
            overlay.UpdateExercise(frame, activeExercise.Name, cue.Title, cue.Observation, cue.NextDrill, cue.PossibleMechanism);
            if (!cue.HasObservation || frame.InPit || frame.IsReplay || !frame.OnTrack)
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
