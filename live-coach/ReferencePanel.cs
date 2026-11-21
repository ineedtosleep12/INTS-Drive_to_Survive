using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal sealed class ReferencePanel : UserControl
    {
        ReferenceLap reference, own;
        ComparisonResult comparison;
        string confirmedIdentity="";
        string confirmedSourceCar="",confirmedSourceTrack="",confirmedSourceLayout="",liveGame="",liveCar="",liveTrack="",referencePath="",libraryNotice="";
        bool changingReference,changingLibrary;
        int libraryErrors;
        readonly Label summary = new Label(), selection = new Label(), liveContext = new Label();
        readonly RichTextBox evidence = new RichTextBox();
        readonly CheckBox versions = new CheckBox(), zoom = new CheckBox(), alignment = new CheckBox(), useLive = new CheckBox();
        readonly ComboBox library = new ComboBox();
        readonly string libraryDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"references");
        readonly TrackBar cursor = new TrackBar();
        readonly ReferencePlot plot = new ReferencePlot();
        readonly DataGridView sections = Grid(), brakes = Grid();
        readonly Button save = new Button(), useOwn = new Button(), alignReplay = new Button();
        public event Action<ReferenceLiveSettings> LiveSettingsChanged;
        public ReferencePanel()
        {
            Dock=DockStyle.Fill; BackColor=Palette.Background; ForeColor=Palette.Text; Padding=new Padding(12);
            var rows=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8};
            foreach(float h in new float[]{38,76,90,58,38})rows.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            rows.RowStyles.Add(new RowStyle(SizeType.Percent,100)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); rows.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            Controls.Add(rows);
            var libraryRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};libraryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));libraryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));
            library.Dock=DockStyle.Fill;library.DropDownStyle=ComboBoxStyle.DropDownList;library.DropDownWidth=900;library.AccessibleName="기준 주행 라이브러리";libraryRow.Controls.Add(library,0,0);
            var refresh=new Button {Text="목록 새로고침",Dock=DockStyle.Fill};Style(refresh);refresh.Click+=delegate {ReloadLibrary(false);};libraryRow.Controls.Add(refresh,1,0);rows.Controls.Add(libraryRow,0,0);
            library.SelectedIndexChanged+=delegate {if(changingLibrary)return;var entry=library.SelectedItem as ReferenceLibraryEntry;if(entry!=null)TrySelectReference(entry.Path);};
            var commands=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true};
            AddButton(commands,"기준 주행 열기",delegate {LoadFile(false);});
            AddButton(commands,"내 기록 열기",delegate {LoadFile(true);});
            save.Text="내 완주 랩 저장"; Style(save); save.Enabled=false; save.Click+=delegate {SaveOwn();};commands.Controls.Add(save);
            useOwn.Text="내 완주 랩을 기준으로";Style(useOwn);useOwn.Enabled=false;useOwn.Click+=delegate {UseOwnReference();};commands.Controls.Add(useOwn);
            alignReplay.Text="내 랩으로 주행선 정렬";Style(alignReplay);alignReplay.Enabled=false;alignReplay.Click+=delegate {AlignReplayCoordinates();};commands.Controls.Add(alignReplay);
            AddButton(commands,"기준 주행 출처",delegate {if(reference!=null)OpenUrl(reference.SourceUrl);});
            AddButton(commands,"제작자 원본 자료",delegate {OpenDownload();});
            AddButton(commands,"차량·코스 이름 연결",delegate {MatchLabels();});
            rows.Controls.Add(commands,0,1);
            summary.Dock=DockStyle.Fill;summary.ForeColor=Palette.Text;rows.Controls.Add(summary,0,2);
            var options=new FlowLayoutPanel {Dock=DockStyle.Fill};
            useLive.Text="기준 주행으로 코너 뒤 코칭";useLive.AutoSize=true;useLive.Checked=true;useLive.CheckedChanged+=delegate {SettingsChanged();};options.Controls.Add(useLive);
            versions.Text="다른 버전도 탐색 비교";versions.AutoSize=true;versions.CheckedChanged+=delegate {if(!changingReference){Rebuild();SettingsChanged();}};options.Controls.Add(versions);
            zoom.Text="선택 지점 ±200 m 확대";zoom.AutoSize=true;zoom.CheckedChanged+=delegate {RefreshCursor();};options.Controls.Add(zoom);rows.Controls.Add(options,0,3);
            alignment.Text="MoTeC ↔ 실시간 거리·횡위치 탐색 정렬 (실제 게임 데이터 대조 전)";alignment.AutoSize=true;alignment.CheckedChanged+=delegate {if(!changingReference){Rebuild();SettingsChanged();}};options.Controls.Add(alignment);
            liveContext.Dock=DockStyle.Fill;liveContext.ForeColor=Palette.Cyan;rows.Controls.Add(liveContext,0,4);UpdateLiveContextLabel();
            var tabs=new TabControl {Dock=DockStyle.Fill};rows.Controls.Add(tabs,0,5);
            var traces=new TabPage("입력 · 시간 · 주행선") {BackColor=Palette.Background};plot.Dock=DockStyle.Fill;traces.Controls.Add(plot);tabs.TabPages.Add(traces);
            var losses=new TabPage("어디서 잃었나") {BackColor=Palette.Background};sections.Dock=DockStyle.Fill;losses.Controls.Add(sections);tabs.TabPages.Add(losses);
            var braking=new TabPage("제동 시작 · 해제") {BackColor=Palette.Background};brakes.Dock=DockStyle.Fill;braking.Controls.Add(brakes);tabs.TabPages.Add(braking);
            var sources=new TabPage("출처 · 조건 · 읽는 법") {BackColor=Palette.Background};evidence.Dock=DockStyle.Fill;evidence.ReadOnly=true;evidence.BackColor=Palette.Background;evidence.ForeColor=Palette.Text;evidence.BorderStyle=BorderStyle.None;evidence.DetectUrls=false;sources.Controls.Add(evidence);tabs.TabPages.Add(sources);
            cursor.Dock=DockStyle.Fill;cursor.Minimum=0;cursor.Maximum=10000;cursor.TickStyle=TickStyle.None;cursor.ValueChanged+=delegate {RefreshCursor();};rows.Controls.Add(cursor,0,6);
            selection.Dock=DockStyle.Fill;selection.ForeColor=Palette.Cyan;rows.Controls.Add(selection,0,7);
            sections.Columns.Add("range","구간 · 출발선부터");sections.Columns.Add("loss","이 구간의 시간 차이");sections.Columns.Add("speed","구간 끝 속도 차이");sections.Columns.Add("line","평균 주행선 간격");
            brakes.Columns.Add("zone","기준 시작 위치");brakes.Columns.Add("start","내 제동 시작");brakes.Columns.Add("release","내 브레이크 해제");brakes.Columns.Add("error","위치 차이 해석");
            sections.CellClick+=delegate(object s,DataGridViewCellEventArgs e) {if(e.RowIndex>=0&&sections.Rows[e.RowIndex].Tag is double)Seek((double)sections.Rows[e.RowIndex].Tag);};
            brakes.CellClick+=delegate(object s,DataGridViewCellEventArgs e) {if(e.RowIndex>=0&&brakes.Rows[e.RowIndex].Tag is double)Seek((double)brakes.Rows[e.RowIndex].Tag);};
            plot.SeekRequested+=Seek;
            ReloadLibrary(true);
        }
        static DataGridView Grid()
        {
            return new DataGridView {ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Palette.Background,BorderStyle=BorderStyle.None,
                DefaultCellStyle=new DataGridViewCellStyle {BackColor=Palette.Panel,ForeColor=Palette.Text,SelectionBackColor=Color.FromArgb(52,76,84),SelectionForeColor=Palette.Text,WrapMode=DataGridViewTriState.True},AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells};
        }
        static void Style(Button b) {b.AutoSize=true;b.Height=32;b.FlatStyle=FlatStyle.Flat;b.BackColor=Palette.Panel;b.ForeColor=Palette.Text;b.Margin=new Padding(0,0,7,5);}
        static void AddButton(Control host,string title,Action action) {var b=new Button {Text=title};Style(b);b.Click+=delegate {action();};host.Controls.Add(b);}
        static void OpenUrl(string url) {Uri uri;if(Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https")try {System.Diagnostics.Process.Start(uri.AbsoluteUri);}catch(Exception){} }
        static ReferenceLap Read(string path) {return ReferenceLibrary.Read(path);}
        void OpenDownload()
        {
            if(reference==null)return;string url;
            if(reference.Conditions.TryGetValue("sourceDownloadUrl",out url)){OpenUrl(url);return;}
            if(reference.SourceUrl=="https://www.youtube.com/watch?v=jtPPwxAlu0c")OpenUrl("https://drive.google.com/drive/folders/1GACcrJyLckzcjllVK45ROfGzPZMp3Sak");
            else OpenUrl(reference.SourceUrl);
        }
        void ReloadLibrary(bool initial)
        {
            var scan=ReferenceLibrary.Scan(libraryDirectory);libraryErrors=scan.Errors.Length;libraryNotice="라이브러리 "+scan.Entries.Length+"개 · 다른 코스의 실제 JSON을 references 폴더에 추가하거나 파일 열기로 선택하세요.";
            if(scan.Errors.Length>0)libraryNotice+="\n읽지 못한 파일: "+string.Join("\n",scan.Errors);
            changingLibrary=true;
            try
            {
                library.Items.Clear();library.Items.AddRange(scan.Entries);
                int index=-1;
                for(int i=0;i<library.Items.Count;i++)if(string.Equals(((ReferenceLibraryEntry)library.Items[i]).Path,referencePath,StringComparison.OrdinalIgnoreCase))index=i;
                if(index<0&&reference!=null&&!string.IsNullOrEmpty(referencePath)){library.Items.Add(new ReferenceLibraryEntry(referencePath,reference));index=library.Items.Count-1;}
                if(initial&&index<0)
                {
                    for(int i=0;i<library.Items.Count;i++)if(Path.GetFileName(((ReferenceLibraryEntry)library.Items[i]).Path)=="lmu-porsche992-monza-psracing.json")index=i;
                    // The public edition includes other car classes. Leave default coaching
                    // on the driver's own laps until they select a community recording.
                    for(int i=0;index<0&&i<library.Items.Count;i++)
                        if(!((ReferenceLibraryEntry)library.Items[i]).IsCommunityRecording)index=i;
                }
                library.SelectedIndex=index;
            }
            finally {changingLibrary=false;}
            if(initial&&library.SelectedItem is ReferenceLibraryEntry)TrySelectReference(((ReferenceLibraryEntry)library.SelectedItem).Path);
            else Rebuild();
        }
        public ReferenceLibraryEntry[] EntriesFor(LmuCircuitCourse course)
        {
            return library.Items.OfType<ReferenceLibraryEntry>().Where(entry =>
                string.Equals(entry.Game,"LMU",StringComparison.OrdinalIgnoreCase) && course.MatchesTrackFamily(entry.Track))
                .OrderByDescending(entry=>entry.Car=="Porsche 911 GT3 R (992)").ToArray();
        }
        public bool TrySelectReference(string path)
        {
            ReferenceLap loaded;
            try
            {
                loaded=Read(path);
            }
            catch(Exception ex)
            {
                MessageBox.Show(this,"기준 주행을 읽지 못했습니다. 기존 기준은 유지합니다.\n"+ex.Message,"INTS");
                ReloadLibrary(false);return false;
            }
            changingReference=true;
            try {reference=loaded;referencePath=Path.GetFullPath(path);confirmedIdentity="";confirmedSourceCar=confirmedSourceTrack=confirmedSourceLayout="";versions.Checked=false;alignment.Checked=false;}
            finally {changingReference=false;}
            ReloadLibrary(false);UpdateLiveContextLabel();SettingsChanged();return true;
        }
        public void EnableLiveReference() { useLive.Checked=true; }
        public void SetCourseBaseline(ReferenceLap captured)
        {
            changingReference=true;changingLibrary=true;
            try
            {
                reference=captured;own=null;referencePath="";confirmedIdentity="";
                confirmedSourceCar=confirmedSourceTrack=confirmedSourceLayout="";
                versions.Checked=false;alignment.Checked=false;useLive.Checked=captured!=null;
                library.SelectedIndex=-1;
            }
            finally {changingLibrary=false;changingReference=false;}
            Rebuild();SettingsChanged();
        }
        void LoadFile(bool mine)
        {
            using(var d=new OpenFileDialog {Filter="INTS 기준/완주 랩 (*.json)|*.json",Title=mine?"내 완주 랩 열기":"실제 기준 주행 열기"})
                if(d.ShowDialog(this)==DialogResult.OK)try {if(mine){own=Read(d.FileName);Rebuild();}else TrySelectReference(d.FileName);}catch(Exception ex){MessageBox.Show(this,"주행 파일을 읽지 못했습니다.\n"+ex.Message,"INTS");}
        }
        void SaveOwn()
        {
            if(own==null)return;
            using(var d=new SaveFileDialog {Filter="INTS 완주 랩 (*.json)|*.json",FileName="INTS-own-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"})
                if(d.ShowDialog(this)==DialogResult.OK)try {File.WriteAllText(d.FileName,own.ToJson(),new UTF8Encoding(false));}catch(Exception ex){MessageBox.Show(this,ex.Message,"INTS 저장 실패");}
        }
        void UseOwnReference()
        {
            if(own==null||!own.Complete||own.Validity!="valid"||ReferenceLibrary.ViewOnly(own))return;
            // Freeze this lap. Later capture events update own, never this reference.
            var captured=ReferenceLap.LoadJson(own.ToJson());
            captured.Car=SourceLabel(captured,"sourceCarLabel",captured.Car);
            captured.Track=SourceLabel(captured,"sourceTrackLabel",captured.Track);
            if(captured.Conditions.ContainsKey("sourceIdentityMapping"))captured.Layout="unknown";
            own=ReferenceLap.LoadJson(captured.ToJson());
            changingReference=true;
            try {reference=captured;referencePath="";confirmedIdentity="";confirmedSourceCar=confirmedSourceTrack=confirmedSourceLayout="";versions.Checked=false;alignment.Checked=false;useLive.Checked=true;}
            finally {changingReference=false;}
            ReloadLibrary(false);UpdateLiveContextLabel();SettingsChanged();
        }
        void AlignReplayCoordinates()
        {
            ReferenceLap mapped;string explanation;
            if(!AccReplayAlignment.TryAlign(reference,own,versions.Checked,alignment.Checked,out mapped,out explanation))
            {MessageBox.Show(this,explanation,"주행선 좌표 대응 확인");return;}
            reference=mapped;Rebuild();SettingsChanged();
            summary.Text+="\n"+explanation;
        }
        string IdentityKey(ReferenceLap lap) {return reference==null||lap==null?"":reference.Game+"|"+reference.Car+"|"+reference.Track+"|"+reference.Layout+"|"+lap.Game+"|"+lap.Car+"|"+lap.Track+"|"+lap.Layout;}
        public void SetOwnLap(ReferenceLap lap)
        {
            if(lap!=null&&reference!=null&&((!string.IsNullOrEmpty(confirmedIdentity)&&confirmedIdentity==IdentityKey(lap))||
                (confirmedSourceCar.Length>0&&lap.Game==reference.Game&&lap.Car==confirmedSourceCar&&lap.Track==confirmedSourceTrack)))
            {lap.Car=reference.Car;lap.Track=reference.Track;lap.Layout=reference.Layout;lap.Conditions["sourceIdentityMapping"]="Driver confirmed same model/layout labels in this session.";}
            own=lap;Rebuild();
        }
        public void SetCaptureError(string error) {summary.Text="최근 완주 랩은 비교에 추가하지 못했습니다.\n"+error+"\n기존에 불러온 기록은 유지합니다. 다음 유효 랩에서 다시 확인합니다.";}
        public ReferenceLiveSettings GetLiveSettings()
        {return new ReferenceLiveSettings(reference,useLive.Checked&&!ReferenceLibrary.ViewOnly(reference),versions.Checked,alignment.Checked,confirmedSourceCar,confirmedSourceTrack,confirmedSourceLayout);}
        void SettingsChanged()
        {if(changingReference)return;UpdateLiveContextLabel();var handler=LiveSettingsChanged;if(handler!=null)handler(GetLiveSettings());}
        public void SetLiveContext(string game,TelemetryFrame frame)
        {
            string newGame=frame==null?"":game??"",newCar=frame==null?"":frame.Car??"",newTrack=frame==null?"":frame.Track??"";
            if(newGame==liveGame&&newCar==liveCar&&newTrack==liveTrack)return;
            liveGame=newGame;liveCar=newCar;liveTrack=newTrack;UpdateLiveContextLabel();
        }
        void UpdateLiveContextLabel()
        {
            if(liveGame.Length==0)liveContext.Text="실시간 차량 연결 대기 · 기준 코칭은 코너 전체를 관측한 뒤 탈출 시 짧게 표시합니다.";
            else liveContext.Text="실시간: "+liveGame+" · "+liveCar+" · "+liveTrack+"\n이름이 다르면 ‘차량·코스 이름 연결’로 같은 모델과 레이아웃인지 확인하세요.";
            useLive.Enabled=reference!=null&&!ReferenceLibrary.ViewOnly(reference);
            if(ReferenceLibrary.ViewOnly(reference))liveContext.Text="입력 열람 전용: 코스 위치 채널이 없어 거리 비교·핫랩 오버레이는 비활성입니다. 기본 게임 코칭은 유지됩니다.";
        }
        static string SourceLabel(ReferenceLap lap,string key,string fallback)
        {string value;return lap.Conditions.TryGetValue(key,out value)&&!string.IsNullOrWhiteSpace(value)?value:fallback;}
        // This only reconciles labels after the driver checks the actual model/layout.
        // Coordinate, distance, version and length checks remain mandatory.
        void MatchLabels()
        {
            if(reference==null||(own==null&&liveGame.Length==0)){MessageBox.Show(this,"기준 주행을 연 뒤 게임 차량에 탑승하거나 내 완주 랩을 불러오세요. 완주 전에도 실시간 차량 이름을 연결할 수 있습니다.");return;}
            bool active=liveGame.Length>0;
            string sourceGame=active?liveGame:own.Game,sourceCar=active?liveCar:SourceLabel(own,"sourceCarLabel",own.Car),sourceTrack=active?liveTrack:SourceLabel(own,"sourceTrackLabel",own.Track),sourceLayout=active?"unknown":own.Layout;
            if(string.IsNullOrWhiteSpace(sourceCar)||string.IsNullOrWhiteSpace(sourceTrack)){MessageBox.Show(this,"게임의 차량·코스 식별자가 아직 없습니다.");return;}
            if(!string.Equals(reference.Game,sourceGame,StringComparison.OrdinalIgnoreCase)){MessageBox.Show(this,"다른 게임의 기록은 이름을 연결할 수 없습니다.");return;}
            if(MessageBox.Show(this,"게임의 차량/리버리 이름이 제작자 기록과 다를 때만 사용하세요.\n\n기준: "+reference.Car+" / "+reference.Track+" / "+reference.Layout+"\n"+(active?"실시간 차량: ":"내 기록: ")+sourceCar+" / "+sourceTrack+" / "+sourceLayout+"\n\n실제로 같은 모델·세대·트랙 레이아웃인지 확인했나요? 다른 차량이나 코스를 연결하면 비교가 의미 없어집니다. 거리·버전 검사는 계속 적용됩니다.","동일 차량·코스 확인",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            confirmedSourceCar=sourceCar;confirmedSourceTrack=sourceTrack;confirmedSourceLayout=sourceLayout;
            if(own!=null&&string.Equals(own.Game,sourceGame,StringComparison.OrdinalIgnoreCase)&&SourceLabel(own,"sourceCarLabel",own.Car)==sourceCar&&SourceLabel(own,"sourceTrackLabel",own.Track)==sourceTrack)
            {
                if(!own.Conditions.ContainsKey("sourceCarLabel"))own.Conditions["sourceCarLabel"]=own.Car;
                if(!own.Conditions.ContainsKey("sourceTrackLabel"))own.Conditions["sourceTrackLabel"]=own.Track;
                confirmedIdentity=IdentityKey(own);own.Car=reference.Car;own.Track=reference.Track;own.Layout=reference.Layout;
                own.Conditions["sourceIdentityMapping"]="Driver confirmed model and layout labels; original labels preserved.";
            }
            Rebuild();SettingsChanged();
        }
        static string N(double n,string unit) {return ReferenceLap.Finite(n)?n.ToString("0.0",CultureInfo.InvariantCulture)+unit:"미측정";}
        static string Signed(double n,string unit) {return ReferenceLap.Finite(n)?n.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture)+unit:"미측정";}
        static string LapTime(ReferenceLap lap)
        {double t=lap.Samples.Last().Time-lap.Samples[0].Time;string recorded;double scored;
            if(lap.Conditions.TryGetValue("recordedLapTimeSeconds",out recorded)&&double.TryParse(recorded,NumberStyles.Float,CultureInfo.InvariantCulture,out scored)&&scored>0)
                return ((int)scored/60)+":"+(scored%60).ToString("00.000",CultureInfo.InvariantCulture)+(lap.Complete?"":" · 경계 표본 제외");
            if(!lap.HasTime)return "표본 시각 미제공";
            return (lap.Complete?"완주 ":"부분 ")+((int)t/60)+":"+(t%60).ToString("00.000",CultureInfo.InvariantCulture);}
        void Rebuild()
        {
            comparison=ReferenceComparison.Compare(reference,own,versions.Checked,alignment.Checked);save.Enabled=own!=null;useOwn.Enabled=own!=null&&own.Complete&&own.Validity=="valid"&&!ReferenceLibrary.ViewOnly(own);sections.Rows.Clear();brakes.Rows.Clear();
            alignReplay.Enabled=AccReplayAlignment.CanTry(reference,own);
            if(reference!=null&&reference.DistanceBasis==ReferenceLap.LmuAnchoredDistanceBasis)alignment.Text="기록 거리 탐색 정렬 (게임 계측 지점 사이를 속도로 보간)";
            else alignment.Text=reference!=null&&reference.DistanceBasis==AccReplayAlignment.SourceDistanceBasis?"ACCReplay ↔ 실시간 코스 거리 탐색 정렬 (대응 미검증)":"MoTeC ↔ 실시간 거리·횡위치 탐색 정렬 (실제 게임 데이터 대조 전)";
            string title=reference==null?"기준 주행을 열어 주세요.":"기준: "+reference.Game+" · "+reference.Car+" · "+reference.Track+" · "+LapTime(reference)+" ("+reference.Author+")";
            if(libraryErrors>0)title+=" · 라이브러리 읽기 문제 "+libraryErrors+"건 (출처 탭 확인)";
            summary.Text=title+"\n"+(own==null?"내 기록: 출발선을 통과한 뒤 완전한 유효 랩을 마치면 자동 비교합니다.":"내 기록: "+own.Car+" · "+own.Track+" · "+LapTime(own))+"\n"+(comparison.Comparable?(comparison.HasTimeComparison?"구간별 +시간 = 내 손실 · −시간 = 내 이득. 랩 차이 "+Signed(comparison.LapDeltaSeconds," s"):"위치·입력 비교 · 기준 표본 시각이 없어 초 단위 손실은 계산하지 않습니다.")+(alignment.Checked?" · 탐색 정렬 / 검증 전":""):comparison.Reason);
            if(ReferenceLibrary.ViewOnly(reference))summary.Text=title+"\n실제 속도·브레이크·가속 기록을 볼 수 있습니다. 코스 위치와 주행선 채널은 없습니다.\n가로축은 속도 적분 거리이며, 같은 코스 지점 비교나 실시간 기준 코칭에는 사용하지 않습니다.";
            foreach(var s in comparison.Sections){int row=sections.Rows.Add(s.Name,comparison.HasTimeComparison?Signed(s.TimeLossSeconds," s"):"기준 시각 미제공",Signed(s.ExitSpeedDeltaKph," km/h"),comparison.HasLineComparison?N(s.MeanLineOffsetM," m"):"좌표 비교 불가");sections.Rows[row].Tag=s.StartDistanceM;}
            foreach(var b in comparison.BrakeZones){int row=brakes.Rows.Add(N(b.ReferenceStartM," m"),BrakeText(b.StartDeltaM,b.StartDifferenceResolved),BrakeText(b.ReleaseDeltaM,b.ReleaseDifferenceResolved),"추정 불확실성 약 "+N(b.EstimatedUncertaintyM," m"));brakes.Rows[row].Tag=b.ReferenceStartM;}
            var text=new StringBuilder();
            if(reference!=null&&!reference.HasTime)text.AppendLine("위치 기준 기록\n원본에는 표본별 시각이 없습니다. 랩타임 표기는 제작자 기록의 전체 시간일 뿐, 각 코너의 시간 손실을 계산하는 데 쓰지 않습니다. 거리별 속도·페달·조향 각도와 실제 주행선만 보존했습니다. 조향°는 기준 각도만 표시하며 정규화된 내 입력과 직접 겹치지 않습니다.\nACCReplay의 지도 좌표와 게임 좌표의 관계는 문서로 검증되지 않았습니다. 실제 ACC 유효 완주 랩을 기록하고 ‘내 랩으로 주행선 정렬’을 눌러 전 코스의 축·척도 대응이 확인된 경우에만 두 선을 비교합니다. 임의 이동·회전·크기 맞춤은 하지 않습니다. 이 검사는 원리 설명을 위한 기하학적 대응이며 정렬 정확도의 보증이 아닙니다.\n");
            text.AppendLine("기준 라이브러리\n"+libraryNotice+"\n");
            if(ReferenceLibrary.ViewOnly(reference))text.AppendLine("이 파일은 입력 열람 전용입니다. 가로축은 실제 속도 표본을 적분한 이동거리이며 코스 위치가 아닙니다. 조향° 그래프는 원본 각도이며 정규화 입력과 비교하지 않습니다. 원본 시계열은 recordings의 companion 파일에도 보존합니다. 거리 델타·제동 위치·라인 비교·핫랩 오버레이를 활성화하지 않습니다.\n");
            text.AppendLine("실시간 코너 뒤 코칭\n체크박스를 켜면 선택한 파일이 오버레이 기준으로 사용됩니다. 완주할 필요 없이 차량 이름을 연결할 수 있습니다. 이미 진입한 코너의 중간부터 연결되었다면 그 코너는 건너뛰고 완전히 관측한 다음 구간부터 비교합니다.\n다른 서킷도 실제 기준 JSON을 추가하면 같은 방식으로 사용합니다. 목록에 없는 코스의 핫랩이나 라인을 자동 생성하지 않습니다. 파일을 바꾸면 이름 연결과 탐색 정렬 선택을 다시 확인합니다.\n");
            text.AppendLine("읽는 법\n청록 = 선택한 기준 랩 · 노랑 = 비교할 내 기록. 그래프를 클릭하거나 아래 슬라이더를 움직이세요.\n구간 표를 누르면 해당 위치로 이동합니다. ±200 m 확대로 제동 해제와 가속 시점을 확인하세요.\n+시간 차이는 기준보다 더 오래 걸렸다는 뜻입니다. 손실 표는 중복 없는 100 m 구간의 차이를 순서대로 보여 줍니다. 코너 이름 자동 판정이 아닙니다.\n제동 시작은 15% 이상 0.1초, 해제는 8% 이하 0.15초 기준입니다. 작은 위치 차이는 기록 해상도 안으로 표시합니다.\n주행선은 같은 좌표계의 실측 위치만 겹칩니다. 기준선도 세계 최속의 정답이라는 뜻은 아닙니다.\n차이만으로 늦은 제동이 원인이라고 단정하지 마세요. 진입 속도 → 해제 → 최저 속도 → 탈출 가속을 함께 보세요.\n");
            AppendEvidence(text,"기준 주행",reference);AppendEvidence(text,"내 주행",own);
            text.AppendLine("비교 조건/제한");foreach(string warning in comparison.Warnings)text.AppendLine("• "+warning);
            if(!comparison.Comparable)text.AppendLine(comparison.Reason);
            evidence.Text=text.ToString();plot.Reference=reference;plot.Own=own;plot.Result=comparison;RefreshCursor();UpdateLiveContextLabel();
        }
        static string BrakeText(double delta,bool resolved) {return resolved?N(Math.Abs(delta)," m ")+(delta>0?"더 뒤에서":"더 앞에서"):"기록 해상도 안 ("+Signed(delta," m")+")";}
        static void AppendEvidence(StringBuilder text,string heading,ReferenceLap lap)
        {
            text.AppendLine(heading);if(lap==null){text.AppendLine("불러온 기록 없음\n");return;}
            text.AppendLine(lap.Game+" / "+lap.Car+" / "+lap.Track+" / "+lap.Layout);
            text.AppendLine("기록: "+LapTime(lap)+" · 유효성: "+lap.Validity+" · 게임 버전: "+lap.GameVersion);
            text.AppendLine("횡위치 기준: "+lap.LateralPositionBasis+" (코스의 근사 중심 경로 기준, 실제 도로 중심선 아님)");
            text.AppendLine("제작자: "+lap.Author+" · 날짜: "+lap.RecordedAt+"\n출처: "+lap.SourceUrl+"\n이용 조건: "+lap.Licence+"\n거리: "+lap.DistanceBasis+"\n좌표: "+lap.CoordinateFrame);
            foreach(var item in lap.Conditions)text.AppendLine(item.Key+": "+item.Value);
            text.AppendLine(lap.Provenance+"\n");
        }
        void Seek(double metres)
        {var lap=reference??own;if(lap==null)return;double max=lap.Samples.Last().DistanceM;cursor.Value=(int)Math.Max(0,Math.Min(10000,metres/max*10000));RefreshCursor();}
        void RefreshCursor()
        {
            var lap=reference??own;if(lap==null){selection.Text="기준 파일을 열면 게임 없이도 입력과 출처를 살펴볼 수 있습니다.";plot.Invalidate();return;}
            double at=lap.Samples.Last().DistanceM*cursor.Value/10000.0;plot.CursorM=at;plot.Zoom=zoom.Checked;
            var r=reference==null?null:ReferenceComparison.SampleAtDistance(reference,at);var o=comparison.Comparable?ReferenceComparison.SampleAtDistance(own,at):null;
            selection.Text=(ReferenceLibrary.ViewOnly(lap)?"속도 적분 이동거리 ":"출발선부터 ")+N(at," m")+" · 기준 "+(r==null?"기록 공백":N(r.SpeedKph," km/h")+" / 제동 "+N(r.Brake*100,"%"))+"\n내 기록 "+(o==null?"비교 대기":N(o.SpeedKph," km/h")+" / 제동 "+N(o.Brake*100,"%"))+(comparison.HasLateralComparison&&r!=null&&o!=null?" · 횡위치 차이 "+Signed(o.PathLateralM-r.PathLateralM," m"):" · 해제와 탈출 가속 함께 확인");
            plot.Invalidate();
        }
    }

    internal sealed class ReferencePlot : Control
    {
        public ReferenceLap Reference,Own;
        public ComparisonResult Result;
        public double CursorM;
        public bool Zoom;
        public event Action<double> SeekRequested;
        double from,to;
        RectangleF chart;
        public ReferencePlot() {DoubleBuffered=true;BackColor=Palette.Background;ResizeRedraw=true;}
        protected override void OnMouseDown(MouseEventArgs e)
        {base.OnMouseDown(e);if(chart.Contains(e.Location)&&SeekRequested!=null)SeekRequested(from+(e.X-chart.Left)/chart.Width*(to-from));}
        static bool Ok(double n) {return ReferenceLap.Finite(n);}
        void TextAt(Graphics g,string text,RectangleF area,Color color)
        {TextRenderer.DrawText(g,text,Font,Rectangle.Round(area),color,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Reference==null){TextAt(g,"실제 기준 주행 JSON을 먼저 열어 주세요.\n원본 .ld는 동봉 변환 도구로 가져올 수 있습니다.",new RectangleF(20,30,Width-40,90),Palette.Muted);return;}
            bool compare=Result!=null&&Result.Comparable;double length=Reference.Samples.Last().DistanceM;
            from=Zoom?Math.Max(0,Math.Min(length-400,CursorM-200)):0;to=Zoom?Math.Min(length,from+400):length;
            float mapWidth=Width>=650?220:0;float left=48,right=Width-mapWidth-12;float top=30,h=Math.Max(20,(Height-58)/5f);
            chart=new RectangleF(left,top,right-left,h*5);
            TextAt(g,ReferenceLibrary.ViewOnly(Reference)?"청록: 실제 입력 기록 · 가로축: 속도 적분 거리 (코스 위치 아님)":"청록: 기준     노랑: 내 기록     클릭: 위치 선택",new RectangleF(8,4,Width-10,25),Palette.Muted);
            double speedMax=Math.Max(100,Math.Ceiling(Reference.Samples.Concat(compare?Own.Samples:new ReferenceSample[0]).Where(s=>Ok(s.SpeedKph)).Select(s=>s.SpeedKph).DefaultIfEmpty(300).Max()/20)*20);
            DrawTrace(g,new RectangleF(left,top,right-left,h-7),"km/h",0,speedMax,s=>s.SpeedKph,compare);
            DrawTrace(g,new RectangleF(left,top+h,right-left,h-7),"제동",0,1,s=>s.Brake,compare);
            DrawTrace(g,new RectangleF(left,top+h*2,right-left,h-7),"가속",0,1,s=>s.Throttle,compare);
            bool rawAngle=Reference.Samples.Any(s=>Ok(s.SteeringDegrees))&&!Reference.Samples.Any(s=>Ok(s.Steering));
            if(rawAngle){double angleMax=Math.Max(10,Math.Ceiling(Reference.Samples.Where(s=>Ok(s.SteeringDegrees)).Max(s=>Math.Abs(s.SteeringDegrees))/10)*10);DrawTrace(g,new RectangleF(left,top+h*3,right-left,h-7),"조향°",-angleMax,angleMax,s=>s.SteeringDegrees,false);}
            else DrawTrace(g,new RectangleF(left,top+h*3,right-left,h-7),"조향",-1,1,s=>s.Steering,compare);
            var delta=new RectangleF(left,top+h*4,right-left,h-7);Axes(g,delta,Reference.HasTime?"Δ s":"시각");
            if(compare&&Result.HasTimeComparison)
            {
                double max=Math.Max(1,Result.Points.Where(p=>p.DistanceM>=from&&p.DistanceM<=to).Select(p=>Math.Abs(p.DeltaSeconds)).DefaultIfEmpty(1).Max());
                DrawPoints(g,delta,Result.Points.Select(p=>new PointF((float)p.DistanceM,(float)p.DeltaSeconds)).ToArray(),-max,max,Palette.Lime,12);
                TextAt(g,"+"+max.ToString("0.0"),new RectangleF(0,delta.Top,47,18),Palette.Muted);TextAt(g,"−"+max.ToString("0.0"),new RectangleF(0,delta.Bottom-18,47,18),Palette.Muted);
            }
            else TextAt(g,!Reference.HasTime?"기준 표본 시각 미제공 · 초 단위 손실 계산 안 함":"내 완주 랩이 들어오면 시간 차이를 표시합니다.",delta,Palette.Muted);
            using(var pen=new Pen(Color.FromArgb(130,Palette.Text),1)){float x=left+(float)((CursorM-from)/(to-from))*(right-left);if(x>=left&&x<=right)g.DrawLine(pen,x,top,x,top+h*5-7);}
            TextAt(g,from.ToString("0")+" m",new RectangleF(left,top+h*5,right-left,20),Palette.Muted);TextAt(g,to.ToString("0")+" m",new RectangleF(right-70,top+h*5,75,20),Palette.Muted);
            if(mapWidth>0)DrawMap(g,new RectangleF(right+16,35,mapWidth-24,Height-75),compare);
        }
        void Axes(Graphics g,RectangleF box,string label)
        {using(var p=new Pen(Palette.Panel,1)){g.DrawRectangle(p,box.X,box.Y,box.Width,box.Height);g.DrawLine(p,box.Left,box.Top+box.Height/2,box.Right,box.Top+box.Height/2);}TextAt(g,label,new RectangleF(0,box.Top,47,20),Palette.Muted);}
        void DrawTrace(Graphics g,RectangleF box,string name,double min,double max,Func<ReferenceSample,double> value,bool compare)
        {
            Axes(g,box,name);
            DrawSamples(g,box,Reference,min,max,value,Palette.Cyan);
            if(compare)DrawSamples(g,box,Own,min,max,value,Palette.Lime);
        }
        void DrawSamples(Graphics g,RectangleF box,ReferenceLap lap,double min,double max,Func<ReferenceSample,double> value,Color color)
        {
            var state=g.Save();g.SetClip(box);
            PointF? previous=null;ReferenceSample last=null;
            using(var p=new Pen(color,1.7f))foreach(var s in lap.Samples)
            {
                double v=value(s);if(s.DistanceM<from||s.DistanceM>to||!Ok(v)){previous=null;last=s;continue;}
                var at=new PointF(box.Left+(float)((s.DistanceM-from)/(to-from))*box.Width,box.Bottom-(float)((v-min)/(max-min))*box.Height);
                if(previous.HasValue&&last!=null&&(!lap.HasTime||s.Time-last.Time<=1)&&s.DistanceM-last.DistanceM<=100)g.DrawLine(p,previous.Value,at);
                previous=at;last=s;
            }
            g.Restore(state);
        }
        void DrawPoints(Graphics g,RectangleF box,PointF[] points,double min,double max,Color color,double maxGap)
        {
            PointF? last=null;using(var p=new Pen(color,1.7f))foreach(var s in points){if(s.X<from||s.X>to||!Ok(s.Y)){last=null;continue;}
                if(last.HasValue&&s.X-last.Value.X<=maxGap){var a=last.Value;g.DrawLine(p,box.Left+(float)((a.X-from)/(to-from))*box.Width,box.Bottom-(float)((a.Y-min)/(max-min))*box.Height,box.Left+(float)((s.X-from)/(to-from))*box.Width,box.Bottom-(float)((s.Y-min)/(max-min))*box.Height);}last=s;}
        }
        void DrawMap(Graphics g,RectangleF box,bool compare)
        {
            TextAt(g,"기록된 주행선",new RectangleF(box.X,box.Y,box.Width,25),Palette.Text);box.Y+=30;box.Height-=30;
            var samples=Reference.Samples.Where(s=>Ok(s.X)&&Ok(s.Z)&&(!Zoom||(s.DistanceM>=from&&s.DistanceM<=to))).ToArray();
            if(samples.Length<2||Reference.CoordinateFrame=="unknown") {DrawLateral(g,box,compare);return;}
            var my=compare&&Result.HasLineComparison?Own.Samples.Where(s=>Ok(s.X)&&Ok(s.Z)&&(!Zoom||(s.DistanceM>=from&&s.DistanceM<=to))).ToArray():new ReferenceSample[0];
            var bounds=samples.Concat(my).ToArray();double minX=bounds.Min(s=>s.X),maxX=bounds.Max(s=>s.X),minZ=bounds.Min(s=>s.Z),maxZ=bounds.Max(s=>s.Z);
            double scale=Math.Min((box.Width-10)/Math.Max(1,maxX-minX),(box.Height-45)/Math.Max(1,maxZ-minZ));
            Func<ReferenceSample,PointF> project=s=>new PointF(box.Left+5+(float)((s.X-minX)*scale),box.Bottom-40-(float)((s.Z-minZ)*scale));
            MapLine(g,samples,project,Palette.Cyan,Reference.HasTime);MapLine(g,my,project,Palette.Lime,Own==null||Own.HasTime);
            var r=ReferenceComparison.SampleAtDistance(Reference,CursorM);if(r!=null&&Ok(r.X)){var p=project(r);using(var b=new SolidBrush(Palette.Cyan))g.FillEllipse(b,p.X-4,p.Y-4,8,8);}
            var o=compare&&Result.HasLineComparison?ReferenceComparison.SampleAtDistance(Own,CursorM):null;if(o!=null&&Ok(o.X)){var p=project(o);using(var b=new SolidBrush(Palette.Lime))g.FillEllipse(b,p.X-4,p.Y-4,8,8);}
            TextAt(g,compare&&!Result.HasLineComparison?"내 좌표계 불일치 · 선 비교 보류":"같은 코스 위치에 점 표시\n선택 구간 확대 가능",new RectangleF(box.X,box.Bottom-35,box.Width,40),Palette.Muted);
        }
        static void MapLine(Graphics g,ReferenceSample[] samples,Func<ReferenceSample,PointF> project,Color color,bool hasTime)
        {using(var p=new Pen(color,1.5f))for(int i=1;i<samples.Length;i++)if((!hasTime||samples[i].Time-samples[i-1].Time<=1)&&samples[i].DistanceM-samples[i-1].DistanceM<=100)g.DrawLine(p,project(samples[i-1]),project(samples[i]));}
        void DrawLateral(Graphics g,RectangleF box,bool compare)
        {
            TextAt(g,"횡위치 (m) · 근사 중심 경로 기준",new RectangleF(box.X,box.Y,box.Width,42),Palette.Muted);
            var graph=new RectangleF(box.X+4,box.Y+46,box.Width-8,Math.Max(60,box.Height-165));
            var samples=Reference.Samples.Where(s=>Ok(s.PathLateralM)).ToArray();
            if(samples.Length==0){TextAt(g,"좌표 변환 미확인\n주행선 비교 불가",box,Palette.Muted);return;}
            double max=Math.Max(10,samples.Max(s=>Math.Abs(s.PathLateralM)));if(compare&&Result.HasLateralComparison)max=Math.Max(max,Own.Samples.Where(s=>Ok(s.PathLateralM)).Select(s=>Math.Abs(s.PathLateralM)).DefaultIfEmpty(0).Max());
            using(var p=new Pen(Palette.Panel))g.DrawRectangle(p,graph.X,graph.Y,graph.Width,graph.Height);
            DrawSamples(g,graph,Reference,-max,max,s=>s.PathLateralM,Palette.Cyan);
            if(compare&&Result.HasLateralComparison)DrawSamples(g,graph,Own,-max,max,s=>s.PathLateralM,Palette.Lime);
            TextAt(g,"+"+max.ToString("0")+" / −"+max.ToString("0")+" m",new RectangleF(graph.X,graph.Bottom+2,graph.Width,20),Palette.Muted);
            TextAt(g,"가로: 코스 거리\n세로: 기록된 횡위치\n\n지도나 좌우 정답선이 아닙니다. 월드 좌표 변환은 미확인입니다.",new RectangleF(box.X,graph.Bottom+26,box.Width,110),Palette.Muted);
        }
    }
}
