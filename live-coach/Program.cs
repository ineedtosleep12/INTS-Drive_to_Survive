using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CoachWindow());
        }
    }

    internal sealed partial class CoachWindow : Form
    {
        private ITelemetryReader reader = new LmuReader();
        private readonly CoachEngine engine = new CoachEngine();
        private readonly PrincipleCoach principles = new PrincipleCoach();
        private readonly LiveReferenceCoach referenceCoach = new LiveReferenceCoach();
        private readonly OverlayWindow overlay = new OverlayWindow();
        private readonly Label connection = new Label(), stage = new Label(), feedback = new Label(), action = new Label();
        private readonly RichTextBox detail = new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, BackColor = Palette.Background, ScrollBars = RichTextBoxScrollBars.Vertical };
        private readonly Label session = new Label();
        private readonly Label setup = new Label();
        private readonly ComboBox game = new ComboBox(), discipline = new ComboBox();
        private readonly TabControl tabs = new TabControl();
        private readonly TabPage brakePracticePage;
        private readonly ReferencePanel referencePanel = new ReferencePanel();
        private int lastCapturedLapCount;
        private bool referenceCoachingEnabled;
        private string referenceGame="";
        private string liveContextKey="";
        private readonly ListBox lessons = new ListBox();
        private readonly RichTextBox lessonText = new RichTextBox();
        private readonly LinkLabel lessonSource = new LinkLabel();
        private readonly string[] gameNames = { "LMU", "ACC", "AMS2", "AC", "EA WRC", "AC Rally" };
        private DrivingDiscipline selectedDiscipline = DrivingDiscipline.Race;
        private CoachPrincipleView lastPrinciple;
        private int generation;
        private bool haveFrames;
        private string selectedGame = "LMU";
        private readonly CheckBox voice = new CheckBox(), passThrough = new CheckBox();
        private readonly Button show = new Button(), export = new Button();
        private readonly Button reviewLastLap = new Button();
        private readonly SpeechSynthesizer speech = new SpeechSynthesizer();
        private readonly object engineLock = new object();
        private System.Threading.Timer poll;
        private DateTime lastFreshUtc = DateTime.MinValue, lastSpokenUtc = DateTime.MinValue, feedbackUntil = DateTime.MinValue;
        private double lastTime = double.NaN;
        private int polling;
        private bool disconnected = true, closing, hotkey;
        private string lastSpokenId = "";
        private const int HotkeyId = 0x494E;
        private const int RunHotkeyId = 0x494F;
        private bool runHotkey;

        public CoachWindow()
        {
            Text = "INTS Live Coach · 실전 주행 & 원리";
            ClientSize = new Size(1020, 900); MinimumSize = new Size(900, 760);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Palette.Background;
            ForeColor = Palette.Text; Font = new Font("Malgun Gothic", 10);
            var selectors = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(20, 10, 0, 0), BackColor = Palette.Panel };
            selectors.Controls.Add(new Label { Text = "게임", AutoSize = true, Margin = new Padding(0, 5, 8, 0) });
            game.DropDownStyle = ComboBoxStyle.DropDownList; game.Width = 160; game.Items.AddRange(gameNames); game.SelectedIndex = 0; selectors.Controls.Add(game);
            selectors.Controls.Add(new Label { Text = "연습", AutoSize = true, Margin = new Padding(20, 5, 8, 0) });
            discipline.DropDownStyle = ComboBoxStyle.DropDownList; discipline.Width = 190; discipline.Items.AddRange(new object[] { "레이싱 · 제동과 회전", "랠리 · 하중과 방향 전환", "드리프트 · 슬라이드 균형" }); discipline.SelectedIndex = 0; selectors.Controls.Add(discipline);
            AddExerciseSelector(selectors);
            tabs.Dock = DockStyle.Fill;
            var practice = new TabPage("실전 코치") { BackColor = Palette.Background, AutoScroll = true };
            var guide = new TabPage("원리 배우기 · 게임 없이 보기") { BackColor = Palette.Background, Padding = new Padding(16) };
            var compare = new TabPage("핫랩과 비교") { BackColor = Palette.Background };
            compare.Controls.Add(referencePanel);
            tabs.TabPages.Add(practice); tabs.TabPages.Add(compare); tabs.TabPages.Add(BuildCircuitPage()); tabs.TabPages.Add(guide);
            brakePracticePage = BuildBrakePracticePage(); tabs.TabPages.Add(brakePracticePage);
            Controls.Add(tabs); Controls.Add(selectors);
            PrepareTimeline();
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 650, Padding = new Padding(25, 16, 25, 0), ColumnCount = 1, RowCount = 11 };
            foreach (float height in new float[] { 43, 37, 82, 45, 54, 70, 95, 55, 46, 50 }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            grid.RowStyles.Insert(3, runRow);
            practice.Controls.Add(grid);
            var title = new Label { Text = "INTS'  LIVE COACH", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = Palette.Lime };
            grid.Controls.Add(title, 0, 0);
            connection.Text = "LMU 연결을 확인하는 중…"; connection.Dock = DockStyle.Fill; connection.ForeColor = Palette.Cyan; grid.Controls.Add(connection, 0, 1);
            setup.Dock = DockStyle.Fill; setup.ForeColor = Palette.Muted; grid.Controls.Add(setup, 0, 2); UpdateSetup();
            grid.Controls.Add(BuildRunControls(), 0, 3);
            stage.Dock = DockStyle.Fill; stage.Font = new Font(Font, FontStyle.Bold); stage.Text = "LMU 대기 → 기준 랩 기록 → 다음 랩 비교"; grid.Controls.Add(stage, 0, 4);
            session.Dock = DockStyle.Fill; session.ForeColor = Palette.Muted; session.Text = "실제 LMU 데이터가 들어오면 차량·트랙·랩이 표시됩니다."; grid.Controls.Add(session, 0, 5);
            feedback.Dock = DockStyle.Fill; feedback.Font = new Font("Malgun Gothic", 17, FontStyle.Bold); feedback.Text = "실제 주행에서 입력 하나를 바꿔 보세요."; grid.Controls.Add(feedback, 0, 6);
            detail.Dock = DockStyle.Fill; detail.ForeColor = Palette.Muted; detail.Text = "내 직전 유효 랩과 같은 코스 위치에서 비교합니다.\n내 이전 랩은 이상적인 주행이나 최속 기준이 아닙니다."; grid.Controls.Add(detail, 0, 7);
            action.Dock = DockStyle.Fill; action.ForeColor = Palette.Lime; grid.Controls.Add(action, 0, 8);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            StyleButton(show, "오버레이 보기", 155); show.Click += delegate { if (overlay.Visible) overlay.Hide(); else overlay.Show(); show.Text = overlay.Visible ? "오버레이 숨기기" : "오버레이 보기"; }; buttons.Controls.Add(show);
            StyleButton(export, "이번 주행 내보내기", 170); export.Enabled = false; export.Click += Export; buttons.Controls.Add(export);
            passThrough.Text = "마우스 통과"; passThrough.AutoSize = true; passThrough.Margin = new Padding(12, 8, 0, 0); passThrough.CheckedChanged += delegate { overlay.SetPassThrough(passThrough.Checked); }; buttons.Controls.Add(passThrough); grid.Controls.Add(buttons, 0, 9);
            AddWrcSetupButton(buttons);
            StyleButton(reviewLastLap, "완주 랩 코너 복기", 158);
            reviewLastLap.Click += delegate { tabs.SelectedTab=compare; referencePanel.ShowCornerReview(); };
            buttons.Controls.Add(reviewLastLap);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill };
            voice.Text = "짧은 한국어 음성 코칭"; voice.AutoSize = true; voice.Enabled = false;
            try { foreach (InstalledVoice v in speech.GetInstalledVoices()) if (v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "ko") { speech.SelectVoice(v.VoiceInfo.Name); voice.Enabled = true; break; } } catch { }
            if (!voice.Enabled) voice.Text = "한국어 음성 미설치 · 텍스트 코칭 사용";
            voice.CheckedChanged += delegate { if (!voice.Checked) speech.SpeakAsyncCancelAll(); }; footer.Controls.Add(voice);
            footer.Controls.Add(new Label { AutoSize = true, ForeColor = Palette.Muted, Text = "Ctrl+Alt+O 마우스 통과 전환 · 기록은 이 PC에 저장" }); grid.Controls.Add(footer, 0, 10);
            overlay.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing) { e.Cancel = true; overlay.Hide(); show.Text = "오버레이 보기"; } };
            lessons.Dock = DockStyle.Left; lessons.Width = 210; lessons.BackColor = Palette.Panel; lessons.ForeColor = Palette.Text; lessons.BorderStyle = BorderStyle.None; lessons.DisplayMember = "Title";
            lessonText.Dock = DockStyle.Fill; lessonText.ReadOnly = true; lessonText.BorderStyle = BorderStyle.None; lessonText.BackColor = Palette.Background; lessonText.ForeColor = Palette.Text; lessonText.Font = new Font("Malgun Gothic", 11); lessonText.DetectUrls = false;
            lessonSource.Dock = DockStyle.Bottom; lessonSource.Height = 48; lessonSource.LinkColor = Palette.Cyan;
            guide.Controls.Add(lessonText); guide.Controls.Add(lessons); guide.Controls.Add(lessonSource);
            lessons.SelectedIndexChanged += delegate { ShowLesson(); }; lessonSource.LinkClicked += delegate { var item = lessons.SelectedItem as PrincipleLesson; if (item != null && item.SourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) System.Diagnostics.Process.Start(item.SourceUrl); };
            game.SelectedIndexChanged += delegate { ChangeSource(); };
            discipline.SelectedIndexChanged += delegate { ClearBrakePracticeSource(); ClearCoursePractice(); lock (engineLock) { selectedDiscipline = (DrivingDiscipline)discipline.SelectedIndex; engine.Reset(); principles.Reset(); referenceCoach.Reset(); haveFrames = false; lastCapturedLapCount=0; generation++; lastPrinciple = null; } feedbackUntil = DateTime.MinValue; lastSpokenId="";lastSpokenUtc=DateTime.MinValue;speech.SpeakAsyncCancelAll(); ClearLiveOverlay(); LoadLessons(); RefreshExercises(); UpdateSetup(); };
            referencePanel.LiveSettingsChanged += ConfigureReference;
            ConfigureReference(referencePanel.GetLiveSettings());
            LoadLessons();
            Shown += delegate { hotkey = Native.RegisterHotKey(Handle, HotkeyId, 0x4003, (uint)Keys.O); if (!hotkey) passThrough.Text = "마우스 통과 (이 창에서 해제)"; ConfigureRunHotkey(); poll = new System.Threading.Timer(Poll, null, 0, 50); };
        }

        private void ChangeSource()
        {
            ClearBrakePracticeSource();
            ClearCoursePractice();
            stageRoute.Clear(); stageCar.Clear();
            lock (engineLock)
            {
                generation++; reader.Dispose();
                switch (game.SelectedIndex)
                {
                    case 1: reader = new AssettoReader(AssettoGame.Acc); break;
                    case 2: reader = new Ams2Reader(); break;
                    case 3: reader = new AssettoReader(AssettoGame.Ac); break;
                    case 4: reader = new WrcReader(); break;
                    case 5: reader = new AcrReader(); break;
                    default: reader = new LmuReader(); break;
                }
                selectedGame = gameNames[game.SelectedIndex]; lastTime = double.NaN; lastFreshUtc = DateTime.MinValue; disconnected = true; lastBrakeContext = null; latestBrakeCapture = null;
                engine.Reset(); principles.Reset(); referenceCoach.Reset(); haveFrames = false; lastCapturedLapCount=0; lastPrinciple = null;
            }
            export.Enabled = false; feedbackUntil = DateTime.MinValue; speech.SpeakAsyncCancelAll();
            lastSpokenId="";lastSpokenUtc=DateTime.MinValue;
            liveContextKey="";referencePanel.SetLiveContext(selectedGame,null);
            int preferred = selectedGame == "AC" ? 2 : selectedGame == "EA WRC" || selectedGame == "AC Rally" ? 1 : 0;
            if (discipline.SelectedIndex != preferred) discipline.SelectedIndex = preferred;
            else RefreshExercises();
            UpdateSetup(); Apply(null, null, null, "선택한 게임에서 주행을 시작하세요.");
        }
        private void UpdateSetup()
        {
            reviewLastLap.Visible = selectedDiscipline == DrivingDiscipline.Race;
            string instruction;
            switch (game.SelectedIndex)
            {
                case 1: instruction = "ACC: 차량에 탑승하면 연결 · 기존 Broadcasting 설정으로 코스 길이를 확인합니다.\n공식 코스 길이 수신 후 유효 완주 랩의 제동 위치·주행선을 비교할 수 있습니다."; break;
                case 2: instruction = "AMS2: Options → System → Shared Memory: Project CARS 2\n레이싱: 출발선을 통과한 뒤 완전한 유효 랩을 마치면 다음 랩과 비교합니다."; break;
                case 3: instruction = "Skidpad 0.5에서는 ‘원돌이 시작’, Drift Playground 2021에서는 ‘연속 드리프트’를 골라 보세요.\n후륜 모드 차량도 차체 회전·속도·페달이 수신되면 같은 과제를 연습할 수 있습니다."; break;
                case 4: instruction = "EA WRC를 처음 연결할 때는 게임을 종료하고 아래 ‘EA WRC 데이터 전송 켜기’를 누르세요.\n그다음 게임을 실행하고 스테이지에서 주행하세요."; break;
                case 5: instruction = "Assetto Corsa Rally: 다른 AC 계열 게임을 종료하고 스테이지에서 직접 운전\n랠리 입력·원리 코칭 시험 연결 · 공식 SDK/실제 주행 검증 전"; break;
                default: instruction = "LMU의 Gameplay → Enable Plugins를 켜고 게임을 재시작하세요.\n한 코너의 직전 시도와 비교하려면 ‘브레이크 해제 실험’, 한 바퀴를 복기하려면 ‘LMU 서킷 훈련’을 여세요."; break;
            }
            setup.Text = instruction + "\n오버레이는 테두리 없는 창 / 창 모드에서 사용하세요.";
            if(referenceCoachingEnabled&&!string.Equals(selectedGame,referenceGame,StringComparison.OrdinalIgnoreCase))setup.Text+="\n불러온 "+referenceGame+" 기준은 이 게임에 적용하지 않습니다. 기본 원리 코칭을 사용합니다.";
        }
        private void LoadLessons()
        {
            lessons.Items.Clear(); foreach (var item in PrincipleCoach.GetLessons(selectedDiscipline)) lessons.Items.Add(item);
            if (lessons.Items.Count > 0) lessons.SelectedIndex = 0;
        }
        private void ConfigureReference(ReferenceLiveSettings settings)
        {
            if(!settingCourseBaseline && courseRun.Course!=null){courseRun.Begin(null);RefreshCourseStatus();}
            var selectedReference=settings.Reference;
            lock(engineLock) {referenceCoach.Configure(settings);referenceCoachingEnabled=settings.Enabled&&selectedReference!=null;referenceGame=selectedReference==null?"":selectedReference.Game;generation++;}
            // A changed source or alignment must never leave an old corner cue on screen.
            speech.SpeakAsyncCancelAll();lastSpokenId="";lastSpokenUtc=DateTime.MinValue;ClearLiveOverlay();UpdateSetup();
        }
        private void ClearLiveOverlay()
        {overlay.UpdateData(null,null,"기준·주행 조건 확인 중","다음 주행 데이터에서 코칭을 다시 시작합니다.","");}
        private void ShowLesson()
        {
            var item = lessons.SelectedItem as PrincipleLesson; if (item == null) return;
            lessonText.Text = item.Title + "\n\n목표\n" + item.Goal + "\n\n원리\n" + item.Explanation + "\n\n게임에서 해 볼 연습\n" + item.Drill + "\n\n확인할 변화\n" + item.LookFor + "\n\n판단의 한계\n" + item.Limitations;
            lessonSource.Text = "참고: " + item.SourceTitle + " (브라우저로 열기)";
        }

        private static void StyleButton(Button button, string text, int width)
        {
            button.Text = text; button.Size = new Size(width, 34); button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Palette.Panel; button.ForeColor = Palette.Text; button.FlatAppearance.BorderColor = Color.FromArgb(65, 88, 100);
        }

        private void Poll(object unused)
        {
            if (closing || Interlocked.Exchange(ref polling, 1) != 0) return;
            try
            {
                lock (engineLock)
                {
                TelemetryFrame frame; string status;
                bool read = reader.TryRead(out frame, out status);
                CoachView view = null; CoachPrincipleView principleView = null;
                LiveReferenceView liveReferenceView=null;
                CoachPrincipleView exerciseView=null;
                ReferenceLap completedLap=null;
                string completedLapError="";
                bool invalidateBrakeSeed=false;
                bool fresh = read && frame != null && (!double.IsNaN(frame.Time)) && (frame.Time != lastTime);
                if (fresh)
                {
                    if (selectedGame=="LMU" && selectedDiscipline==DrivingDiscipline.Race) invalidateBrakeSeed=CheckBrakeCaptureContext(frame);
                    lastFreshUtc = DateTime.UtcNow; lastTime = frame.Time; disconnected = false;
                    bool canCompare = selectedDiscipline == DrivingDiscipline.Race && !frame.TimeIsCaptureClock && frame.ValidLapKnown && !double.IsNaN(frame.TrackLength) && !double.IsNaN(frame.LapDistance);
                    if (canCompare)
                    {
                        view = engine.Update(frame);
                        if(view.CompletedLaps>0 && view.CompletedLaps!=lastCapturedLapCount)
                        {
                            CapturedLap.TryCreate(selectedGame,engine.GetLatestCompleteLap(),out completedLap,out completedLapError);
                            if (selectedGame=="LMU") latestBrakeCapture=completedLap;
                        }
                        lastCapturedLapCount=view.CompletedLaps;
                    }
                    else
                    {
                        engine.Disconnect("이 모드에서는 유효한 거리 랩 비교를 수집하지 않습니다.");
                        view = new CoachView { Status = "실시간 입력 · 원리 코칭", Headline = "측정할 수 있는 입력과 반응을 관찰합니다.", Detail = selectedDiscipline == DrivingDiscipline.Race ? "이 연결은 거리·랩 유효성 정보가 부족해 이전 랩의 거리 비교를 제공하지 않습니다." : "원리 배우기의 과제 하나를 고르고 입력 하나씩 바꿔 보세요.", NextAction = "진단 근거가 부족하면 실패 원인을 단정하지 않습니다." };
                    }
                    principleView = principles.Update(frame, selectedDiscipline); haveFrames = true;
                    exerciseView = ObserveExercise(frame);
                    ObserveBrakePracticeFrame(frame);
                    if(UseExternalReference)liveReferenceView=referenceCoach.Update(selectedGame,frame);
                }
                else if (!reader.LastReadTransient || (DateTime.UtcNow - lastFreshUtc).TotalSeconds >= 1.5)
                {
                    invalidateBrakeSeed=!disconnected;
                    if (!disconnected) { view = engine.Disconnect("주행 데이터가 멈췄습니다. 연결되면 기준 랩부터 다시 확인합니다."); lastCapturedLapCount=0; principles.Disconnect("주행 데이터 대기"); referenceCoach.Reset(); InterruptExerciseObservation(); disconnected = true; }
                    frame = null;
                }
                else return; // A short writer lock/duplicate is not a disconnection.
                TelemetryFrame captured = frame; CoachView capturedView = view; CoachPrincipleView capturedPrinciple = principleView; string capturedStatus = status; int capturedGeneration = generation;
                if (!closing && IsHandleCreated) BeginInvoke((Action)delegate {
                    if(capturedGeneration!=generation)return;
                    if(invalidateBrakeSeed)InvalidateBrakePracticeSeed();
                    if(completedLap!=null)ReceiveCompletedLap(completedLap);
                    else if(completedLapError.Length>0)referencePanel.SetCaptureError(completedLapError);
                    if(capturedGeneration==generation)Apply(captured,capturedView,capturedPrinciple,capturedStatus,liveReferenceView,exerciseView);
                });
                }
            }
            catch (Exception ex)
            {
                if (!disconnected) { lock (engineLock) { engine.Disconnect("데이터 읽기 오류로 이번 랩 비교를 중단했습니다."); principles.Disconnect("데이터 읽기 오류"); referenceCoach.Reset(); InterruptExerciseObservation(); } disconnected = true; }
                int errorGeneration=generation;
                if (!closing && IsHandleCreated) try { BeginInvoke((Action)delegate { if(errorGeneration==generation){InvalidateBrakePracticeSeed();Apply(null, null, null, "연결을 확인하세요: " + ex.GetType().Name);} }); } catch (InvalidOperationException) { }
            }
            finally { Interlocked.Exchange(ref polling, 0); }
        }

        private void Apply(TelemetryFrame frame, CoachView view, CoachPrincipleView principleView, string status,LiveReferenceView liveReference=null, CoachPrincipleView exerciseView=null)
        {
            if (closing) return;
            bool connected = frame != null && !disconnected;
            string context=frame==null?"":selectedGame+"|"+frame.Car+"|"+frame.Track+"|"+frame.GameVersion;
            if(context!=liveContextKey){liveContextKey=context;lastSpokenId="";lastSpokenUtc=DateTime.MinValue;speech.SpeakAsyncCancelAll();referencePanel.SetLiveContext(selectedGame,frame);}
            connection.Text = connected ? "● " + selectedGame + " 데이터 수신 · 게임이 보고한 입력" : "○ " + (string.IsNullOrEmpty(status) ? "선택한 게임을 실행하고 차량에 탑승하세요." : status);
            connection.ForeColor = connected ? Palette.Lime : Palette.Muted;
            if (frame != null) session.Text = frame.Track + "  /  " + frame.Car + "\n" + (frame.Lap >= 0 ? "랩 " + frame.Lap + " · " : "") + Value(frame.SpeedKph, " km/h") + (frame.TimeIsCaptureClock ? " · 수집 시계 사용" : "");
            else session.Text = "실시간 데이터 없음 · 게임·플러그인·차량 탑승 상태를 확인하세요.";
            if (tabs.SelectedTab == brakePracticePage) RefreshBrakePractice();
            if (DisplayBrakePractice(connected ? frame : null)) return;
            if (!connected)
            {
                stage.Text = "주행 데이터 대기 · 코칭 일시 중단";
                session.Text = "실시간 데이터 없음 · 게임·플러그인·차량 탑승 상태를 확인하세요.";
                feedback.Text = "먼저 원리 하나와 연습 목표를 골라 보세요.";
                detail.Text = "원리 배우기는 게임 없이도 볼 수 있습니다.\n주행 데이터가 들어오면 관측 → 가능한 원리 → 다음 연습으로 연결합니다.";
                action.Text = "";
                feedbackUntil = DateTime.MinValue;
                lastPrinciple = null;
                if (DisplayCourseWait(null)) return;
                if (activeExercise != null)
                {
                    CoachPrincipleView report;
                    lock (engineLock) report = RetainedReport();
                    if (report == null) ShowExerciseStart();
                    else ApplyExercise(null, report);
                    return;
                }
                overlay.UpdateData(null, null, selectedGame + " 주행 데이터 대기", "원리 배우기 탭은 게임 없이도 볼 수 있습니다.", "게임·연결 설정·차량 탑승 상태를 확인하세요."); speech.SpeakAsyncCancelAll(); return;
            }
            if (DisplayCourseWait(frame)) return;
            if (exerciseView != null) { ApplyExercise(frame, exerciseView); return; }
            if(UseExternalReference && liveReference!=null)
            {
                ApplyReference(frame,liveReference);return;
            }
            if (view != null)
            {
                if (frame.InPit || frame.IsReplay || !frame.OnTrack || (selectedDiscipline == DrivingDiscipline.Race && frame.ValidLapKnown && !frame.IsValidLap)) { lastPrinciple = null; feedbackUntil = DateTime.MinValue; }
                stage.Text = view.Status; feedback.Text = view.Headline; string detailText = view.Detail; action.Text = view.NextAction;
                if (principleView != null && !string.IsNullOrEmpty(principleView.Limits)) detailText += "\n확인 범위: " + principleView.Limits;
                export.Enabled = haveFrames;
                if (principleView != null && principleView.NewFeedback) { lastPrinciple = principleView; feedbackUntil = DateTime.UtcNow.AddSeconds(10); }
                else if (view.NewFeedback && DateTime.UtcNow >= feedbackUntil) { lastPrinciple = null; feedbackUntil = DateTime.UtcNow.AddSeconds(8); }
                bool showFeedback = DateTime.UtcNow < feedbackUntil;
                string spokenId = view.FeedbackId, spoken = view.NextAction; bool newSpeech = view.NewFeedback;
                if (lastPrinciple != null)
                {
                    feedback.Text = "최근 관찰 · " + lastPrinciple.Title;
                    detailText = "관측: " + lastPrinciple.Observation + "\n가능한 원리: " + lastPrinciple.PossibleMechanism + "\n확인 범위: " + lastPrinciple.Limits;
                    action.Text = "다음 연습: " + lastPrinciple.NextDrill;
                }
                if (showFeedback && lastPrinciple != null)
                {
                    overlay.UpdateData(frame, view, lastPrinciple.Title, lastPrinciple.Observation, lastPrinciple.NextDrill);
                    spokenId = lastPrinciple.EventId; spoken = lastPrinciple.NextDrill; newSpeech = principleView != null && principleView.NewFeedback;
                }
                else overlay.UpdateData(frame, view, showFeedback ? view.Headline : view.Status, showFeedback ? view.ResultSummary : (view.HasReference ? "흰 표시: 내 직전 유효 랩의 페달 입력 · 거리 비교는 근사" : "원리 배우기에서 과제를 선택하세요. 판단 근거가 쌓이면 알려드립니다."), showFeedback ? view.NextAction : (principleView == null ? view.NextAction : principleView.Status));
                if (detail.Text != detailText) detail.Text = detailText;
                if (voice.Checked && newSpeech && spokenId != lastSpokenId && (DateTime.UtcNow - lastSpokenUtc).TotalSeconds > 15 && frame.Brake < .08 && Math.Abs(frame.Steering) < .15 && Math.Abs(frame.LateralG) < .35 && frame.SpeedKph > 30)
                { lastSpokenId = spokenId; lastSpokenUtc = DateTime.UtcNow; speech.SpeakAsyncCancelAll(); speech.SpeakAsync(spoken); }
                if (frame.Brake > .1 || Math.Abs(frame.Steering) > .2 || double.IsNaN(frame.LateralG) || Math.Abs(frame.LateralG) > .5 || frame.InPit || frame.IsReplay) speech.SpeakAsyncCancelAll();
            }
        }

        private void ApplyReference(TelemetryFrame frame,LiveReferenceView live)
        {
            export.Enabled=haveFrames;stage.Text=(courseRun.Course==null?"":courseRun.Course.Name+" · 고정한 내 기준 랩 · ")+live.Status;
            bool cue=live.Ready&&!string.IsNullOrEmpty(live.Heading);
            feedback.Text=cue?live.Heading:live.Ready?"코너 탈출 후 짧게 알려드립니다.":courseRun.Course==null?"기준 주행 코칭 대기":"내 기준 랩 코칭 대기";
            string observation=cue?live.Observation:live.Status;
            string principle=cue?live.Principle:"한 구간의 진입부터 탈출까지 관측한 뒤 비교합니다.";
            string next=cue?live.NextAction:live.Ready?"이번 랩은 평소처럼 달리세요.":"핫랩과 비교 탭에서 기준·이름 연결·탐색 정렬을 확인하세요.";
            detail.Text=observation+"\n원리: "+principle+(string.IsNullOrEmpty(live.Estimate)?"":"\n확인 범위: "+live.Estimate);
            action.Text=cue?"다음 랩: "+next:next;
            overlay.UpdateData(frame,null,feedback.Text,observation,next,live);
            bool settled=frame.Brake<.08&&Math.Abs(frame.Steering)<.15&&!double.IsNaN(frame.LateralG)&&Math.Abs(frame.LateralG)<.35&&frame.SpeedKph>30;
            // Active IDs survive skipped UI callbacks; speak each delivered corner at most once.
            string id="reference:"+live.FeedbackId;
            if(voice.Checked&&cue&&settled&&id!=lastSpokenId&&(DateTime.UtcNow-lastSpokenUtc).TotalSeconds>15)
            {lastSpokenId=id;lastSpokenUtc=DateTime.UtcNow;speech.SpeakAsyncCancelAll();speech.SpeakAsync(next);}
            if(!cue||!settled||frame.InPit||frame.IsReplay||!frame.OnTrack||!frame.IsValidLap)speech.SpeakAsyncCancelAll();
        }
        private bool UseExternalReference {get {return selectedDiscipline==DrivingDiscipline.Race&&referenceCoachingEnabled&&string.Equals(selectedGame,referenceGame,StringComparison.OrdinalIgnoreCase);}}

        internal static string Value(double number, string unit) { return double.IsNaN(number) || double.IsInfinity(number) ? "—" : Math.Round(number).ToString() + unit; }
        private void Export(object sender, EventArgs args)
        {
            using (var dialog = new SaveFileDialog { Filter = "주행 코칭 기록 (*.json)|*.json", FileName = "INTS-" + selectedGame + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" })
                if (dialog.ShowDialog(this) == DialogResult.OK) try { string json; lock (engineLock) json = "{\"game\":\"" + selectedGame + "\",\"lapComparison\":" + engine.ExportJson() + ",\"principleCoaching\":" + principles.ExportJson() + ",\"referenceCoaching\":"+referenceCoach.ExportJson()+",\"skillPractice\":"+ExportExercises()+",\"brakeReleasePractice\":"+brakeExperiment.ExportJson()+"}"; File.WriteAllText(dialog.FileName, json, new System.Text.UTF8Encoding(false)); } catch (Exception ex) { MessageBox.Show(this, "기록 저장 실패: " + ex.Message); }
        }
        private void ConfigureRunHotkey()
        {
            bool wanted = Visible && (activeExercise == StageRun || activeExercise == TougeRun);
            if (wanted && !runHotkey) runHotkey = Native.RegisterHotKey(Handle, RunHotkeyId, 0x4003, (uint)Keys.R);
            if (!wanted && runHotkey) { Native.UnregisterHotKey(Handle, RunHotkeyId); runHotkey = false; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) passThrough.Checked = !passThrough.Checked;
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == RunHotkeyId) ToggleRunFromGame();
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true; if (poll != null) poll.Dispose(); if (hotkey) Native.UnregisterHotKey(Handle, HotkeyId);
            if (runHotkey) Native.UnregisterHotKey(Handle, RunHotkeyId);
            // Do not dispose the map while a reader callback is copying it.
            for (int i = 0; i < 30 && Interlocked.CompareExchange(ref polling, 0, 0) != 0; i++) Thread.Sleep(10);
            if (Interlocked.CompareExchange(ref polling, 0, 0) == 0) reader.Dispose();
            speech.SpeakAsyncCancelAll(); speech.Dispose(); overlay.Dispose(); base.OnFormClosed(e);
        }
    }

    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(13, 22, 29), Panel = Color.FromArgb(23, 39, 48), Text = Color.FromArgb(229, 239, 242), Muted = Color.FromArgb(154, 177, 190), Lime = Color.FromArgb(183, 241, 112), Cyan = Color.FromArgb(110, 214, 235), Brake = Color.FromArgb(255, 143, 124);
    }

    internal sealed class OverlayWindow : Form
    {
        private TelemetryFrame frame; private CoachView view; private LiveReferenceView external;
        private string skillName, skillPrinciple, skillProgress;
        private string headline = "주행 데이터 대기", result = "", detail = "조작이 안정된 뒤 짧게 피드백합니다."; private bool passThrough;
        public OverlayWindow()
        {
            Text = "INTS Coaching Overlay";
            FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true;
            Size = new Size(500, 310); BackColor = Palette.Background; ForeColor = Palette.Text; Opacity = .94;
            StartPosition = FormStartPosition.Manual; Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 16, Screen.PrimaryScreen.WorkingArea.Top + 90); Font = new Font("Malgun Gothic", 9);
            MouseDown += delegate(object sender, MouseEventArgs e) { if (!passThrough && e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; } }
        public void SetPassThrough(bool enabled) { passThrough = enabled; if (IsHandleCreated) { long style = Native.GetExStyle(Handle); Native.SetExStyle(Handle, enabled ? style | 0x20L : style & ~0x20L); } Invalidate(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetPassThrough(passThrough); }
        public void UpdateData(TelemetryFrame current, CoachView coach, string heading, string observed, string explanation,LiveReferenceView live=null) { frame = current; view = coach;external=live; skillName=skillPrinciple=skillProgress=null; headline = heading ?? ""; result = observed ?? ""; detail = explanation ?? ""; Invalidate(); }
        public void UpdateExercise(TelemetryFrame current, string name, string heading, string observed, string next, string principle, string progress=null)
        {
            UpdateData(current, null, heading, observed, next);
            skillName = name;
            skillPrinciple = principle;
            skillProgress = progress;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Palette.Cyan, 2)) g.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
            Draw(g, "INTS  /  LIVE COACH", new Rectangle(14, 9, 245, 20), Palette.Cyan, 9, true);
            Draw(g, passThrough ? "마우스 통과 · Ctrl+Alt+O" : "드래그 이동 · Ctrl+Alt+O 잠금", new Rectangle(250, 9, 238, 20), Palette.Muted, 8, false);
            Draw(g, frame == null ? "— km/h" : CoachWindow.Value(frame.SpeedKph, " km/h") + "   " + (frame.Gear == 0 ? "N" : frame.Gear.ToString()) + "단", new Rectangle(14, 35, 190, 30), Palette.Text, 18, true);
            string reference = external!=null?(external.Ready?"선택한 기준 랩 · 근사 비교":"기준 랩 연결 대기"):view != null && view.HasReference ? "내 기준 랩 " + view.ReferenceLap : "입력 · 원리 코칭";
            Draw(g, skillName ?? reference, new Rectangle(210, 40, 277, 22), Palette.Muted, 10, false);
            double refBrake=external!=null?(external.Ready&&external.ReferenceAtPosition!=null?external.ReferenceAtPosition.Brake:double.NaN):view == null || view.ReferenceAtPosition == null ? double.NaN : view.ReferenceAtPosition.Brake;
            double refThrottle=external!=null?(external.Ready&&external.ReferenceAtPosition!=null?external.ReferenceAtPosition.Throttle:double.NaN):view == null || view.ReferenceAtPosition == null ? double.NaN : view.ReferenceAtPosition.Throttle;
            Bar(g, "제동", frame == null ? double.NaN : frame.Brake, refBrake, 14, Palette.Brake,external!=null?Palette.Cyan:Palette.Text);
            Bar(g, "가속", frame == null ? double.NaN : frame.Throttle, refThrottle, 178, Palette.Lime,external!=null?Palette.Cyan:Palette.Text);
            double steer = frame == null ? double.NaN : frame.Steering;
            Draw(g, "조향  " + (double.IsNaN(steer) ? "—" : (steer > 0 ? "+" : "") + Math.Round(steer * 100) + "%"), new Rectangle(344, 74, 145, 26), Palette.Cyan, 10, true);
            Draw(g, headline, new Rectangle(14, 117, 473, 34), Palette.Text, 12, true);
            if (skillName != null)
            {
                Draw(g, result, new Rectangle(14, 150, 473, 58), Palette.Muted, 9, false);
                Draw(g, detail, new Rectangle(14, 211, 473, 44), Palette.Lime, 10, true);
                if (!String.IsNullOrEmpty(skillProgress))
                {
                    Draw(g, skillPrinciple, new Rectangle(14, 253, 473, 32), Palette.Text, 9, false);
                    Draw(g, skillProgress, new Rectangle(14, 286, 473, 20), Palette.Cyan, 9, true);
                }
                else Draw(g, skillPrinciple, new Rectangle(14, 258, 473, 45), Palette.Text, 9, false);
                return;
            }
            Draw(g, result, new Rectangle(14, 153, 473, 39), Palette.Muted, 9, false);
            Draw(g, (external!=null&&!string.IsNullOrEmpty(external.Heading)?"다음 랩: ":"")+detail, new Rectangle(14, 196, 473, 40), Palette.Lime, 10, false);
            Draw(g, external!=null&&!string.IsNullOrEmpty(external.Principle)?"원리: "+external.Principle:"",new Rectangle(14,240,473,40),Palette.Text,9,false);
            Draw(g,external!=null?"청록 표시: 선택한 기준 랩 · 주행 조건은 비교 탭에서 확인하세요.":"흰 표시: 내 이전 유효 랩 · 다음 코너에서는 조작에 집중하세요.",new Rectangle(14,287,473,18),Palette.Muted,8,false);
        }
        private static void Draw(Graphics g, string text, Rectangle bounds, Color color, float size, bool bold) { using (var font = new Font("Malgun Gothic", size, bold ? FontStyle.Bold : FontStyle.Regular)) TextRenderer.DrawText(g, text, font, bounds, color, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); }
        private static void Bar(Graphics g, string label, double value, double reference, int x, Color color,Color markerColor)
        {
            Draw(g, label + " " + (double.IsNaN(value) ? "—" : Math.Round(value * 100) + "%"), new Rectangle(x, 73, 150, 21), color, 10, true);
            using (var brush = new SolidBrush(Palette.Panel)) g.FillRectangle(brush, x, 99, 142, 7);
            if (!double.IsNaN(value)) using (var brush = new SolidBrush(color)) g.FillRectangle(brush, x, 99, (float)(142 * Math.Max(0, Math.Min(1, value))), 7);
            if (!double.IsNaN(reference)) using (var pen = new Pen(markerColor, 2)) { float marker = (float)(x + 142 * Math.Max(0, Math.Min(1, reference))); g.DrawLine(pen, marker, 95, marker, 110); }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        internal static long GetExStyle(IntPtr h) { return IntPtr.Size == 8 ? GetWindowLongPtr(h, -20).ToInt64() : GetWindowLong(h, -20); }
        internal static void SetExStyle(IntPtr h, long style) { if (IntPtr.Size == 8) SetWindowLongPtr(h, -20, new IntPtr(style)); else SetWindowLong(h, -20, (int)style); }
    }
}
