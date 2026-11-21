using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal sealed partial class CoachWindow
    {
        readonly LmuCoursePractice courseRun = new LmuCoursePractice();
        readonly ComboBox circuitChoice = new ComboBox(), circuitFiles = new ComboBox();
        readonly Button previousCircuit = new Button(), nextCircuit = new Button(), filePractice = new Button(), saveBaseline = new Button();
        readonly Label circuitTitle = new Label(), circuitSummary = new Label(), courseRunStatus = new Label(), circuitLibrary = new Label();
        readonly RichTextBox circuitDrills = new RichTextBox();
        readonly LinkLabel circuitVideo = new LinkLabel();
        bool settingCourseBaseline;

        LmuCircuitCourse SelectedCircuit { get { return circuitChoice.SelectedItem as LmuCircuitCourse; } }

        TabPage BuildCircuitPage()
        {
            var page = new TabPage("LMU 서킷 훈련") { BackColor = Palette.Background, AutoScroll = true };
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(23, 16, 23, 10) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            page.Controls.Add(grid);
            var nav = new FlowLayoutPanel { Height = 46, Dock = DockStyle.Fill, WrapContents = false };
            circuitChoice.DropDownStyle = ComboBoxStyle.DropDownList; circuitChoice.Width = 285;
            circuitChoice.AccessibleName = "연습할 LMU 서킷";
            foreach (var course in LmuCircuitGuide.Courses) circuitChoice.Items.Add(course);
            circuitChoice.Items.Add(LmuCircuitGuide.LiveLayout);
            nav.Controls.Add(circuitChoice);
            CourseButton(nav, previousCircuit, "이전 서킷", 100, delegate { circuitChoice.SelectedIndex--; });
            CourseButton(nav, nextCircuit, "다음 서킷", 100, delegate { circuitChoice.SelectedIndex++; });
            CourseButton(nav, new Button(), "게임에서 고른 레이아웃", 205, delegate { circuitChoice.SelectedItem = LmuCircuitGuide.LiveLayout; });
            AddCourseRow(grid, nav, 50);
            circuitTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold); circuitTitle.ForeColor = Palette.Lime;
            AddCourseRow(grid, circuitTitle, 46);
            AddCourseRow(grid, circuitSummary, 63);
            circuitDrills.BackColor = Palette.Panel; circuitDrills.ForeColor = Palette.Text;
            circuitDrills.ReadOnly = true; circuitDrills.BorderStyle = BorderStyle.None; circuitDrills.DetectUrls = false;
            circuitDrills.Font = new Font(Font.FontFamily, 11); circuitDrills.AccessibleName = "코너별 연습 과제";
            AddCourseRow(grid, circuitDrills, 188);
            var links = new FlowLayoutPanel { WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
            var official = new LinkLabel { Text = "공식 코스 안내", AutoSize = true, LinkColor = Palette.Cyan, Margin = new Padding(0, 0, 18, 0) };
            official.LinkClicked += delegate { OpenCircuitUrl(SelectedCircuit.OfficialUrl); };
            circuitVideo.Text = "포르쉐 992 온보드 영상"; circuitVideo.AutoSize = true; circuitVideo.LinkColor = Palette.Cyan;
            circuitVideo.LinkClicked += delegate { OpenCircuitUrl(SelectedCircuit.VideoUrl); };
            links.Controls.Add(official); links.Controls.Add(circuitVideo); AddCourseRow(grid, links, 46);
            var personalRow = new FlowLayoutPanel { WrapContents = false };
            CourseButton(personalRow, new Button(), "내 첫 랩으로 연습 시작", 202, StartCircuitPractice);
            CourseButton(personalRow, saveBaseline, "고정한 내 기준 랩 저장", 201, SaveCircuitBaseline);
            AddCourseRow(grid, personalRow, 45);
            courseRunStatus.ForeColor = Palette.Muted; AddCourseRow(grid, courseRunStatus, 58);
            circuitLibrary.ForeColor = Palette.Cyan; AddCourseRow(grid, circuitLibrary, 33);
            var fileRow = new FlowLayoutPanel { WrapContents = false };
            circuitFiles.DropDownStyle = ComboBoxStyle.DropDownList; circuitFiles.Width = 475;
            circuitFiles.DropDownWidth = 780; circuitFiles.DisplayMember = "CourseLabel"; circuitFiles.AccessibleName = "이 서킷의 실제 기준 파일";
            fileRow.Controls.Add(circuitFiles);
            CourseButton(fileRow, filePractice, "이 기록으로 비교 열기", 181, OpenCircuitFile);
            AddCourseRow(grid, fileRow, 48);
            circuitChoice.SelectedIndexChanged += delegate { RefreshCircuitPage(); };
            page.Enter += delegate { RefreshCircuitPage(); };
            circuitChoice.SelectedIndex = 0;
            return page;
        }
        static void AddCourseRow(TableLayoutPanel grid, Control control, int height)
        {
            int row = grid.RowStyles.Count; grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            control.Dock = DockStyle.Fill; grid.Controls.Add(control,0,row);
        }
        static void CourseButton(Control host, Button button, string title, int width, Action click)
        {
            StyleButton(button,title,width); button.Click += delegate { click(); }; host.Controls.Add(button);
        }
        void OpenCircuitUrl(string url)
        {
            Uri link;
            if (!Uri.TryCreate(url,UriKind.Absolute,out link) || link.Scheme != "https") return;
            try { Process.Start(link.AbsoluteUri); }
            catch (System.ComponentModel.Win32Exception) { MessageBox.Show(this,"브라우저로 열 주소입니다.\n" + url); }
        }
        void RefreshCircuitPage()
        {
            var course = SelectedCircuit;
            if (course == null) return;
            int index = circuitChoice.SelectedIndex;
            previousCircuit.Enabled = index > 0; nextCircuit.Enabled = index < LmuCircuitGuide.Courses.Count - 1;
            circuitTitle.Text = course.IsLiveLayout ? course.Name :
                (index+1) + " / " + LmuCircuitGuide.Courses.Count + "   " + course.Name + " · " + course.Layout;
            circuitSummary.Text = course.Summary;
            if (!course.IsLiveLayout)
                circuitSummary.Text += "\n게임 레이아웃   " + string.Join("  ·  ", course.AvailableLayouts) +
                    (course.AvailableLayouts.Count>1 ? "   |   다른 구성은 ‘게임에서 고른 레이아웃’으로 시작하세요." : "");
            var drills = new StringBuilder();
            for (int i=0; i<course.Drills.Count; i++) drills.AppendLine((i+1) + ". " + course.Drills[i]).AppendLine();
            circuitDrills.Text = drills.ToString().TrimEnd();
            circuitVideo.Visible = !string.IsNullOrEmpty(course.VideoUrl);
            circuitFiles.Items.Clear(); circuitFiles.Items.AddRange(referencePanel.EntriesFor(course));
            bool available = circuitFiles.Items.Count > 0;
            if (available) circuitFiles.SelectedIndex = 0;
            circuitLibrary.Text = available ? "같은 서킷의 실제 기록 · 파일에 표시된 차량과 레이아웃을 LMU에서 선택하세요."
                : "이 서킷의 기준 파일이 없습니다. 내 랩으로 시작하거나 ‘핫랩과 비교’에서 파일을 여세요.";
            if(course.IsLiveLayout) circuitLibrary.Text = "저장한 같은 차량·레이아웃의 랩은 ‘핫랩과 비교 → 기준 주행 열기’로 다시 사용할 수 있습니다.";
            circuitFiles.Visible = filePractice.Visible = available;
            RefreshCourseStatus();
        }
        void RefreshCourseStatus()
        {
            bool here = SelectedCircuit != null && SelectedCircuit == courseRun.Course;
            saveBaseline.Enabled = here && courseRun.Baseline != null;
            if (!here && SelectedCircuit!=null && SelectedCircuit.IsLiveLayout) courseRunStatus.Text = "LMU에서 연습할 레이아웃과 차량을 선택한 뒤 ‘내 첫 랩으로 연습 시작’을 누르세요.";
            else if (!here) courseRunStatus.Text = "LMU에서 위 레이아웃과 차량을 고르세요. 첫 유효 랩을 기준으로 고정하면 다음 랩부터 코너별로 비교합니다.";
            else if (courseRun.Baseline == null && courseRun.Course.IsLiveLayout && !string.IsNullOrEmpty(courseRun.LiveTrack))
                courseRunStatus.Text = "주행 조건 고정 · " + courseRun.LiveTrack + " / " + courseRun.LiveCar + " · " + courseRun.LiveLengthMeters.ToString("N0") + " m\n출발선부터 한 랩을 유효하게 완주하세요.";
            else if (courseRun.Baseline == null) courseRunStatus.Text = "연습 중 · 출발선을 지난 뒤 첫 유효 랩을 끝까지 달리세요.";
            else courseRunStatus.Text = "내 기준 랩 고정 · " + courseRun.Baseline.Car + " / " + courseRun.Baseline.Track +
                "\n같은 조건에서 추가로 완주한 유효 랩 " + courseRun.ComparisonLaps + "회 · ‘실전 코치 → 완주 랩 코너 복기’에서 다음 과제를 확인하세요.";
        }
        void SetCourseReference(ReferenceLap lap)
        {
            settingCourseBaseline = true;
            try { referencePanel.SetCourseBaseline(lap); }
            finally { settingCourseBaseline = false; }
        }
        void ClearCoursePractice()
        {
            if (courseRun.Course == null) return;
            courseRun.Begin(null); SetCourseReference(null); RefreshCourseStatus();
        }
        void StartCircuitPractice()
        {
            var course = SelectedCircuit;
            if (course == null) return;
            game.SelectedIndex = 0; discipline.SelectedIndex = 0;
            courseRun.Begin(course);
            lock (engineLock)
            {
                engine.Reset(); principles.Reset(); haveFrames=false; lastCapturedLapCount=0; generation++;
            }
            lastPrinciple=null; feedbackUntil=DateTime.MinValue; export.Enabled=false;
            SetCourseReference(null); RefreshCourseStatus();
            tabs.SelectedIndex = 0;
            if (!overlay.Visible) overlay.Show();
            show.Text = "오버레이 숨기기"; DisplayCourseWait(null);
        }
        void OpenCircuitFile()
        {
            var entry = circuitFiles.SelectedItem as ReferenceLibraryEntry;
            if (entry == null) return;
            game.SelectedIndex = 0; discipline.SelectedIndex = 0;
            if (!referencePanel.TrySelectReference(entry.Path)) return;
            referencePanel.EnableLiveReference();
            tabs.SelectedIndex = 1;
            if (!overlay.Visible) overlay.Show();
            show.Text = "오버레이 숨기기";
        }
        void SaveCircuitBaseline()
        {
            var lap = courseRun.Baseline;
            if (lap == null) return;
            using (var dialog = new SaveFileDialog { Filter = "INTS 완주 랩 (*.json)|*.json", FileName = "LMU-" + courseRun.Course.Id + "-own.json" })
                if (dialog.ShowDialog(this)==DialogResult.OK)
                    try { File.WriteAllText(dialog.FileName,lap.ToJson(),new UTF8Encoding(false)); }
                    catch (Exception ex) { MessageBox.Show(this,ex.Message,"기준 랩 저장 실패"); }
        }
        void ReceiveCompletedLap(ReferenceLap lap)
        {
            if (courseRun.Course == null) { referencePanel.SetOwnLap(lap); return; }
            bool first = courseRun.Baseline == null;
            if (!courseRun.Record(lap)) return;
            if (first) SetCourseReference(lap);
            else referencePanel.SetOwnLap(lap);
            RefreshCourseStatus();
        }
        bool DisplayCourseWait(TelemetryFrame frame)
        {
            if (courseRun.Course == null) return false;
            var course = courseRun.Course;
            if (course.IsLiveLayout && frame != null && string.IsNullOrEmpty(courseRun.LiveTrack) && courseRun.TryBindLiveLayout(frame))
                RefreshCourseStatus();
            bool matches = frame != null && courseRun.Accepts(frame.Track,frame.TrackLength,frame.Car,frame.GameVersion);
            if (matches && courseRun.Baseline != null) return false;
            stage.Text = course + " · 내 랩 연습";
            feedback.Text = frame == null ? "LMU 주행 데이터 대기" : !matches ? "선택한 연습과 주행 조건을 확인하세요." :
                frame.InPit || frame.IsReplay || !frame.OnTrack || !frame.IsValidLap ? "다음 유효 랩을 기다립니다." : "내 기준 랩을 기록하고 있습니다.";
            string target = courseRun.Baseline == null ? course.ToString() : courseRun.Baseline.Track + " / " + courseRun.Baseline.Car;
            if(course.IsLiveLayout && !string.IsNullOrEmpty(courseRun.LiveTrack))
                target = courseRun.LiveTrack + " / " + courseRun.LiveCar + " · " + courseRun.LiveLengthMeters.ToString("N0") + " m";
            detail.Text = matches ? "출발선부터 결승선까지 유효하게 완주하면 이 랩의 제동과 주행선을 고정합니다."
                : "연습 대상   " + target + (frame == null ? "" : "\n현재 주행   " + frame.Track + " / " + frame.Car);
            action.Text = matches ? "이번 랩은 평소 페이스로 달리세요." : "LMU 연습 세션에서 같은 차량과 레이아웃을 선택하세요.";
            if(course.IsLiveLayout && matches) action.Text = target + " · 이번 랩은 평소 페이스로 달리세요.";
            overlay.UpdateData(frame,null,feedback.Text,detail.Text,action.Text);
            return true;
        }
    }
}
