using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal sealed partial class CoachWindow
    {
        sealed class BrakeLapChoice
        {
            public ReferenceLap Lap;
            public BrakePracticeCorner[] Corners;
            public BrakePracticeCorner Monza;
            public bool FromFile;

            public string Track { get { return BrakePracticeCourse.NativeLabel(Lap,"sourceTrackLabel",Lap.Track); } }
            public string Car { get { return BrakePracticeCourse.NativeLabel(Lap,"sourceCarLabel",Lap.Car); } }
            public override string ToString() { return Track + " / " + Car; }
            public bool SameContext(ReferenceLap other)
            {
                return Track == BrakePracticeCourse.NativeLabel(other,"sourceTrackLabel",other.Track) &&
                    Car == BrakePracticeCourse.NativeLabel(other,"sourceCarLabel",other.Car) && Lap.GameVersion == other.GameVersion &&
                    Lap.TrackLengthM == other.TrackLengthM;
            }
        }

        const string MonzaBrakeChoice = "몬자 WEC · 포르쉐 911 GT3 R (992)";
        const string OtherBrakeChoice = "다른 코스 · 다음 LMU 완주 랩 받기";
        const int MaxBrakeLayouts = 8;
        readonly ComboBox brakeLayouts = new ComboBox(), brakeCorners = new ComboBox();
        readonly Button brakeStart = new Button(), brakeEnd = new Button(), brakeReset = new Button(), brakeOpen = new Button();
        readonly Label brakePlan = new Label(), brakeState = new Label(), brakeAdvice = new Label(), brakeMeasuredCourse = new Label();
        readonly Label[,] brakeValues = new Label[6,3];
        readonly RichTextBox brakeHistory = new RichTextBox();
        readonly BrakeCornerMap brakeMap = new BrakeCornerMap();
        BrakeLapChoice latestBrakeSource;
        BrakePracticeCorner lastArmedBrakeCorner;
        ReferenceLap lastArmedBrakeLap;
        BrakeReleasePass brakeDisplayedPass;
        bool brakePresetPending, changingBrakeChoice;
        bool brakeHasFreshLap, brakeTableRendered;
        string brakeFileNotice = "";

        bool IsMonzaBrakeChoice { get { return Equals(brakeLayouts.SelectedItem, MonzaBrakeChoice); } }
        BrakeLapChoice SelectedBrakeLap
        { get { return IsMonzaBrakeChoice ? latestBrakeSource : brakeLayouts.SelectedItem as BrakeLapChoice; } }
        BrakePracticeCorner SelectedBrakeCorner
        { get { return IsMonzaBrakeChoice ? (latestBrakeSource == null ? null : latestBrakeSource.Monza) : brakeCorners.SelectedItem as BrakePracticeCorner; } }

        string BrakePracticePendingText
        {
            get
            {
                if (!brakePresetPending) return "";
                if (SelectedBrakeCorner != null) return "앱에서 차량 확인 · " + SelectedBrakeCorner.Car;
                return "몬자 WEC에서 포르쉐 992로 아웃랩 후 한 랩을 유효하게 완주하세요.";
            }
        }

        TabPage BuildBrakePracticePage()
        {
            var page = new TabPage("브레이크 해제 실험") { BackColor=Palette.Background, AutoScroll=true };
            var grid = new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=1, Padding=new Padding(20,10,20,8) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            page.Controls.Add(grid);
            var intro = new Label { Text="진입 속도와 제동 표지를 맞추고, 브레이크를 놓는 속도만 바꿔 보세요.",
                Font=new Font(Font.FontFamily,12,FontStyle.Bold), ForeColor=Palette.Lime };
            AddCourseRow(grid,intro,35);
            var layouts = BrakeChoiceRow("연습 코스",brakeLayouts,395);
            CourseButton(layouts,brakeOpen,"저장한 내 랩 열기",158,OpenBrakePracticeLap);
            AddCourseRow(grid,layouts,41);
            var location = new TableLayoutPanel { ColumnCount=2, RowCount=1, Margin=Padding.Empty };
            location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,204));
            var choice = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, Margin=Padding.Empty };
            choice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            AddCourseRow(choice,BrakeChoiceRow("연습 구간",brakeCorners,385),38);
            brakePlan.ForeColor=Palette.Muted; AddCourseRow(choice,brakePlan,80);
            location.Controls.Add(choice,0,0); location.Controls.Add(brakeMap,1,0);
            AddCourseRow(grid,location,122);
            var commands = new FlowLayoutPanel { WrapContents=false };
            CourseButton(commands,brakeStart,"실험 시작",232,StartSelectedBrakePractice);
            CourseButton(commands,brakeEnd,"실험 종료",110,StopSelectedBrakePractice);
            CourseButton(commands,brakeReset,"결과 비우기",122,delegate {
                ResetBrakePractice(); lastArmedBrakeCorner=null; lastArmedBrakeLap=null;
                RefreshBrakePractice();
            });
            AddCourseRow(grid,commands,44);
            brakeState.ForeColor=Palette.Cyan; AddCourseRow(grid,brakeState,45);
            brakeMeasuredCourse.Font=new Font(Font,FontStyle.Bold);
            AddCourseRow(grid,brakeMeasuredCourse,26);
            AddCourseRow(grid,BuildBrakeTable(),196);
            brakeAdvice.ForeColor=Palette.Lime; AddCourseRow(grid,brakeAdvice,60);
            AddCourseRow(grid,new Label {Text="최근 통과 기록",Font=new Font(Font,FontStyle.Bold)},26);
            brakeHistory.ReadOnly=true; brakeHistory.DetectUrls=false; brakeHistory.BackColor=Palette.Panel;
            brakeHistory.ForeColor=Palette.Text; brakeHistory.BorderStyle=BorderStyle.None;
            brakeHistory.AccessibleName="최근 통과 기록"; AddCourseRow(grid,brakeHistory,105);
            brakeLayouts.Items.Add(MonzaBrakeChoice); brakeLayouts.Items.Add(OtherBrakeChoice);
            brakeLayouts.SelectedIndexChanged+=delegate {
                if (changingBrakeChoice) return;
                brakeFileNotice=""; RefreshBrakeChoices();
            };
            brakeCorners.SelectedIndexChanged+=delegate {if (!changingBrakeChoice) RefreshBrakePractice();};
            brakeLayouts.SelectedIndex=0; page.Enter+=delegate {RefreshBrakePractice();};
            return page;
        }

        FlowLayoutPanel BrakeChoiceRow(string caption,ComboBox combo,int width)
        {
            var row=new FlowLayoutPanel { WrapContents=false };
            row.Controls.Add(new Label { Text=caption,Width=80,Margin=new Padding(0,7,4,0) });
            combo.DropDownStyle=ComboBoxStyle.DropDownList; combo.Width=width;
            combo.DropDownWidth=Math.Max(550,width); combo.AccessibleName=caption;
            row.Controls.Add(combo); return row;
        }

        Control BuildBrakeTable()
        {
            var table = new TableLayoutPanel { ColumnCount=4, RowCount=7, BackColor=Palette.Panel };
            foreach(float width in new float[] {34,21,21,24}) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,width));
            string[] headers={"측정 항목","직전 통과","이번 통과","변화 (이번 − 직전)"};
            string[] names={"브레이크 해제 시간 (60 → 10%)","평균 해제 속도","탈출 지점 속도","구간 통과 시간","진입 지점 속도","제동 시작 (출발선 기준)"};
            for(int row=0;row<7;row++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent,100f/7));
                for(int col=0;col<4;col++)
                {
                    var value=new Label { Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleLeft,
                        Margin=new Padding(8,0,0,0),ForeColor=row==0 || col==3?Palette.Cyan:col==1?Palette.Muted:Palette.Text };
                    value.Text=row==0?headers[col]:col==0?names[row-1]:"—";
                    if(row>0 && col>0) brakeValues[row-1,col-1]=value;
                    table.Controls.Add(value,col,row);
                }
            }
            return table;
        }

        void RefreshBrakeChoices()
        {
            var source=SelectedBrakeLap;
            int previous=brakeCorners.SelectedIndex;
            changingBrakeChoice=true;
            try
            {
                brakeCorners.Items.Clear();
                if(IsMonzaBrakeChoice) brakeCorners.Items.Add("첫 시케인 · 레티필로 T1–T2");
                else if(source!=null) brakeCorners.Items.AddRange(source.Corners);
                if(brakeCorners.Items.Count>0)
                    brakeCorners.SelectedIndex=previous<0 || previous>=brakeCorners.Items.Count?0:previous;
            }
            finally { changingBrakeChoice=false; }
            RefreshBrakePractice();
        }

        void SetBrakePracticeLap(ReferenceLap lap)
        {
            RegisterBrakePracticeLap(lap,false);
        }

        void RegisterBrakePracticeLap(ReferenceLap lap,bool fromFile)
        {
            if (brakePracticePage==null || lap==null || lap.Game!="LMU") return;
            var corners=BrakePracticeCourse.FromNativeLap(lap);
            BrakePracticeCorner monza=BrakePracticeCourse.MonzaFirstChicane(lap);
            var next=new BrakeLapChoice {Lap=lap,Corners=corners,Monza=monza,FromFile=fromFile};
            latestBrakeSource=next;
            brakeHasFreshLap=!fromFile;
            bool running;
            lock(engineLock) running=brakePracticeRunning;
            int existing=-1;
            for(int i=2;i<brakeLayouts.Items.Count;i++)
                if(((BrakeLapChoice)brakeLayouts.Items[i]).SameContext(lap)) {existing=i;break;}
            bool chooseSource=fromFile && (!IsMonzaBrakeChoice || monza==null) ||
                Equals(brakeLayouts.SelectedItem,OtherBrakeChoice);
            bool replaceSelected=existing>=0 && brakeLayouts.SelectedIndex==existing;
            changingBrakeChoice=true;
            try
            {
                if(existing<0)
                {
                    if(brakeLayouts.Items.Count>=MaxBrakeLayouts+2)
                    {
                        // Keep the selected menu entry alive when a different course arrives.
                        int remove=brakeLayouts.SelectedIndex==2?3:2;
                        brakeLayouts.Items.RemoveAt(remove);
                    }
                    existing=brakeLayouts.Items.Add(next);
                }
                else brakeLayouts.Items[existing]=next;
                if(!running && (chooseSource || replaceSelected)) brakeLayouts.SelectedIndex=existing;
            }
            finally { changingBrakeChoice=false; }
            if(!running) RefreshBrakeChoices();
            if(brakePresetPending && monza!=null && BrakePracticeCourse.IsIdentifiedPorsche992(monza.Car))
                StartSelectedBrakePractice();
            else RefreshBrakePractice();
        }

        void OpenBrakePracticeLap()
        {
            using(var dialog=new OpenFileDialog {Filter="INTS 완주 랩 JSON (*.json)|*.json",Title="저장한 내 LMU 완주 랩 열기"})
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try
                {
                    var lap=ReferenceLibrary.Read(dialog.FileName);
                    if(lap.Game!="LMU" || !lap.Complete || lap.Validity!="valid" ||
                       lap.DistanceBasis!=ReferenceLap.LmuDistanceBasis || lap.CoordinateFrame!=ReferenceLap.LmuCoordinateFrame || !lap.HasTime)
                        brakeFileNotice="LMU에서 유효하게 완주해 저장한 코스 위치와 시각이 있는 내 랩 파일을 선택하세요.";
                    else
                    {
                        SelectBrakeRaceGame();
                        brakeFileNotice="";
                        RegisterBrakePracticeLap(lap,true);
                    }
                }
                catch(Exception ex)
                {
                    if(!(ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                    brakeFileNotice="완주 랩을 열 수 없습니다. " + ex.Message;
                }
            }
            RefreshBrakePractice();
        }

        void SelectBrakeRaceGame()
        {
            if (selectedGame!="LMU" || selectedDiscipline!=DrivingDiscipline.Race)
            {
                bool other=Equals(brakeLayouts.SelectedItem,OtherBrakeChoice);
                game.SelectedIndex=0; discipline.SelectedIndex=0;
                if(other) brakeLayouts.SelectedItem=OtherBrakeChoice;
            }
        }

        void StartSelectedBrakePractice()
        {
            SelectBrakeRaceGame();
            brakeFileNotice="";
            var source=SelectedBrakeLap;
            var corner=SelectedBrakeCorner;
            if (corner==null)
            {
                if(IsMonzaBrakeChoice) brakePresetPending=true;
                if(!overlay.Visible) overlay.Show();
                if(brakePresetPending) DisplayBrakePractice(null);
                show.Text="오버레이 숨기기"; RefreshBrakePractice();
                return;
            }
            brakePresetPending=false; lastArmedBrakeCorner=corner; lastArmedBrakeLap=source.Lap;
            BeginBrakePractice(corner,source.Lap,brakeHasFreshLap && !source.FromFile && ReferenceEquals(source,latestBrakeSource));
            RefreshBrakePractice();
        }

        void StopSelectedBrakePractice()
        {
            if(brakePresetPending) ClearLiveOverlay();
            brakePresetPending=false; EndBrakePractice(); RefreshBrakeChoices();
        }

        void ClearBrakePracticeSource()
        {
            brakePresetPending=false; latestBrakeSource=null; brakeHasFreshLap=false; brakeFileNotice="";
            if(brakePracticePage==null) return;
            bool running;
            lock(engineLock) running=brakePracticeRunning;
            if(running) EndBrakePractice();
            changingBrakeChoice=true;
            try
            {
                brakeLayouts.SelectedIndex=0;
                while(brakeLayouts.Items.Count>2) brakeLayouts.Items.RemoveAt(brakeLayouts.Items.Count-1);
            }
            finally { changingBrakeChoice=false; }
            RefreshBrakeChoices();
        }

        void InvalidateBrakePracticeSeed() { brakeHasFreshLap=false; }

        static string MeasuredBrakeValue(double? value,string unit,int decimals,bool signed=false)
        {
            if(!value.HasValue || !ReferenceLap.Finite(value.Value)) return "—";
            string format="F"+decimals;
            double rounded=Math.Round(value.Value,decimals,MidpointRounding.AwayFromZero);
            string prefix=signed && rounded>0?"+":"";
            return prefix+rounded.ToString(format,CultureInfo.InvariantCulture)+" "+unit;
        }
        void SetBrakeMetric(int row,double? previous,double? current,string unit,int decimals)
        {
            brakeValues[row,0].Text=MeasuredBrakeValue(previous,unit,decimals,false);
            brakeValues[row,1].Text=MeasuredBrakeValue(current,unit,decimals,false);
            brakeValues[row,2].Text=MeasuredBrakeValue(current-previous,unit,decimals,true);
        }
        void RefreshBrakePractice()
        {
            if(brakeValues[0,0]==null) return;
            bool running;
            BrakeReleasePass latest,previous;
            string status,note,mismatch;
            BrakeReleasePass[] recent=null;
            lock(engineLock)
            {
                running=brakePracticeRunning; latest=brakeExperiment.Latest; previous=brakeExperiment.Previous;
                status=brakeExperiment.Status; note=brakeExperiment.ComparisonNote; mismatch=brakeMismatchMessage;
                if(latest!=null && latest!=brakeDisplayedPass) recent=brakeExperiment.Recent;
            }
            var source=SelectedBrakeLap;
            var corner=running?lastArmedBrakeCorner:SelectedBrakeCorner;
            brakeMap.ShowCorner(running?lastArmedBrakeLap:source==null?null:source.Lap,corner);
            bool waiting=brakePresetPending || running;
            bool confirmation=!running && IsMonzaBrakeChoice && corner!=null && !BrakePracticeCourse.IsIdentifiedPorsche992(corner.Car);
            bool connectOther=Equals(brakeLayouts.SelectedItem,OtherBrakeChoice) &&
                (selectedGame!="LMU" || selectedDiscipline!=DrivingDiscipline.Race);
            brakeLayouts.Enabled=brakeOpen.Enabled=!waiting;
            brakeCorners.Enabled=!waiting && !IsMonzaBrakeChoice && brakeCorners.Items.Count>0;
            brakeStart.Enabled=!running && (corner!=null || connectOther || IsMonzaBrakeChoice && !brakePresetPending);
            brakeStart.Text=confirmation?"표시 차량이 992임을 확인하고 시작":connectOther?"LMU 연습 연결":"실험 시작";
            brakeEnd.Text=brakePresetPending?"준비 취소":"실험 종료"; brakeEnd.Enabled=waiting;
            brakeReset.Enabled=!waiting && latest!=null;
            string range=corner==null?"":" · 계측 거리 "+corner.StartM.ToString("N0")+"–"+corner.EndM.ToString("N0")+" m";
            if(corner!=null)
                brakePlan.Text=corner.Track+" / "+corner.Car+range+"\n"+
                    (confirmation?"게임이 팀·도색 이름을 보냈습니다. 이 차량이 포르쉐 911 GT3 R (992)인지 LMU에서 확인하세요.":
                    "같은 두 지점에서 시간을 재고, 끝 지점을 지날 때의 속도를 비교합니다.")+
                    (!running && source!=null && source.FromFile?"\n저장 파일로 구간을 정했습니다. 시작 후 첫 통과가 비교 기준이 됩니다.":"");
            else if(IsMonzaBrakeChoice)
                brakePlan.Text="LMU 몬자 WEC의 포르쉐 992로 아웃랩 뒤 유효 랩을 완주하세요. 그 랩에서 첫 시케인의 계측 지점을 잡습니다."+
                    (source==null?"":"\n최근 완주  "+source+" · 첫 시케인 조건 확인 대기");
            else
                brakePlan.Text=source==null?"LMU에서 원하는 서킷과 차량으로 아웃랩 뒤 유효 랩을 완주하거나, 저장한 내 랩을 여세요.":
                    source+" · 이 랩에서 구간 전체를 계측할 수 있는 제동 코너를 찾지 못했습니다.";
            string state=running?(String.IsNullOrEmpty(mismatch)?status:mismatch):
                brakePresetPending?BrakePracticePendingText:latest!=null?"실험 종료 · 다음 실험을 시작할 때까지 기록을 보관합니다.":
                "처음은 평소 페이스로 통과하세요. 같은 구간을 다시 달리면 직전 기록과 비교합니다.";
            brakeState.Text=String.IsNullOrEmpty(brakeFileNotice)?state:brakeFileNotice;
            brakeMeasuredCourse.Text=lastArmedBrakeCorner==null?"통과 결과":"통과 결과 · "+lastArmedBrakeCorner.Track+" / "+lastArmedBrakeCorner.Name;
            brakeAdvice.Text=latest!=null?note:"첫 통과는 평소처럼 달리세요. 다음 랩에는 같은 표지에서 제동하고 60%에서 10%까지 조금 더 천천히 풀어 보세요.";
            if(brakeTableRendered && latest==brakeDisplayedPass) return;
            SetBrakeMetric(0,previous==null?null:previous.ReleaseSeconds,latest==null?null:latest.ReleaseSeconds,"초",2);
            SetBrakeMetric(1,previous==null?null:previous.ReleaseRatePctPerSecond,latest==null?null:latest.ReleaseRatePctPerSecond,"%p/s",1);
            SetBrakeMetric(2,previous==null?(double?)null:previous.ExitSpeedKph,latest==null?(double?)null:latest.ExitSpeedKph,"km/h",1);
            SetBrakeMetric(3,previous==null?(double?)null:previous.SegmentSeconds,latest==null?(double?)null:latest.SegmentSeconds,"초",2);
            SetBrakeMetric(4,previous==null?(double?)null:previous.EntrySpeedKph,latest==null?(double?)null:latest.EntrySpeedKph,"km/h",1);
            SetBrakeMetric(5,previous==null?null:previous.BrakeStartM,latest==null?null:latest.BrakeStartM,"m",1);
            var history=new StringBuilder();
            if(recent!=null)
            {
                for(int i=recent.Length-1;i>=0;i--)
                {
                    var pass=recent[i];
                    history.AppendLine(pass.AttemptNumber+"회 · 랩 "+pass.Lap+
                        "     해제 "+MeasuredBrakeValue(pass.ReleaseSeconds,"초",2)+
                        "     탈출 "+MeasuredBrakeValue(pass.ExitSpeedKph,"km/h",1)+
                        "     구간 "+MeasuredBrakeValue(pass.SegmentSeconds,"초",2));
                }
            }
            brakeHistory.Text=history.Length==0?"통과 기록 대기":history.ToString().TrimEnd();
            brakeDisplayedPass=latest; brakeTableRendered=true;
        }
    }

    internal sealed class BrakeCornerMap : Control
    {
        ReferenceLap lap;
        BrakePracticeCorner corner;
        double minX,maxX,minZ,maxZ;
        bool hasShape;

        public BrakeCornerMap()
        {
            DoubleBuffered=true; ResizeRedraw=true; Dock=DockStyle.Fill;
            BackColor=Palette.Panel; AccessibleRole=AccessibleRole.Graphic;
            AccessibleName="전체 코스와 선택한 계측 구간";
        }
        public void ShowCorner(ReferenceLap next,BrakePracticeCorner selected)
        {
            if(next==lap && selected==corner) return;
            if(next!=lap)
            {
                lap=next; hasShape=false;
                if(lap!=null)
                {
                    var points=lap.Samples.Where(PositionKnown).ToArray();
                    if(points.Length>1)
                    {
                        minX=points.Min(p=>p.X); maxX=points.Max(p=>p.X);
                        minZ=points.Min(p=>p.Z); maxZ=points.Max(p=>p.Z);
                        hasShape=maxX>minX || maxZ>minZ;
                    }
                }
            }
            corner=selected; AccessibleDescription=corner==null?"구간 선택 대기":corner.ToString();
            Invalidate();
        }
        static bool PositionKnown(ReferenceSample p)
        { return p!=null && ReferenceLap.Finite(p.X) && ReferenceLap.Finite(p.Z); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle box=ClientRectangle;
            if(!hasShape || box.Width<40 || box.Height<48)
            {
                TextRenderer.DrawText(e.Graphics,"완주 후 코스 위치 표시",Font,box,Palette.Muted,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); return;
            }
            var map=Rectangle.Inflate(new Rectangle(0,0,box.Width,box.Height-22),-8,-6);
            double scale=Math.Min(map.Width/Math.Max(1,maxX-minX),map.Height/Math.Max(1,maxZ-minZ));
            Func<ReferenceSample,PointF> project=p=>new PointF(
                (float)(map.Left+(map.Width-(maxX-minX)*scale)/2+(p.X-minX)*scale),
                (float)(map.Top+(map.Height-(maxZ-minZ)*scale)/2+(maxZ-p.Z)*scale));
            var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var track=new Pen(Palette.Muted,1.6f))
            using(var segment=new Pen(Palette.Lime,3.5f))
            {
                ReferenceSample previous=null;
                foreach(var p in lap.Samples)
                {
                    if(previous!=null && PositionKnown(previous) && PositionKnown(p) &&
                       p.Time-previous.Time<=1 && p.DistanceM-previous.DistanceM<=100)
                    {
                        bool inRange=corner!=null && previous.DistanceM<corner.EndM && p.DistanceM>corner.StartM;
                        g.DrawLine(inRange?segment:track,project(previous),project(p));
                    }
                    previous=p;
                }
            }
            if(corner==null) return;
            var entry=ReferenceComparison.SampleAtDistance(lap,corner.StartM);
            var exit=ReferenceComparison.SampleAtDistance(lap,corner.EndM);
            if(!PositionKnown(entry) || !PositionKnown(exit)) return;
            var a=project(entry); var b=project(exit);
            using(var ring=new Pen(Palette.Cyan,2.4f)) g.DrawEllipse(ring,a.X-4.5f,a.Y-4.5f,9,9);
            using(var square=new SolidBrush(Palette.Lime)) g.FillRectangle(square,b.X-2.5f,b.Y-2.5f,5,5);
            var legend=new Rectangle(0,box.Bottom-23,box.Width/2,23);
            var align=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g,"○ 진입",Font,legend,Palette.Cyan,align);
            legend.X+=legend.Width;
            TextRenderer.DrawText(g,"■ 탈출",Font,legend,Palette.Lime,align);
        }
    }
}
