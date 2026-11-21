using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace IntsLiveCoach
{
    internal sealed class CornerReviewPanel : UserControl
    {
        readonly ComboBox corners = new ComboBox();
        readonly RichTextBox report = new RichTextBox();
        readonly Button traces = new Button();
        LiveReferenceView[] reviews = new LiveReferenceView[0];
        public event Action<double> TraceRequested;

        public CornerReviewPanel()
        {
            Dock = DockStyle.Fill; BackColor = Palette.Background; Padding = new Padding(8);
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
            corners.DropDownStyle = ComboBoxStyle.DropDownList; corners.Dock = DockStyle.Fill;
            corners.AccessibleName = "복기할 감속·회전 구간";
            traces.Text = "이 위치의 입력·주행선"; traces.Dock = DockStyle.Fill;
            traces.FlatStyle = FlatStyle.Flat; traces.BackColor = Palette.Panel; traces.ForeColor = Palette.Cyan;
            report.ReadOnly = true; report.DetectUrls = false; report.Dock = DockStyle.Fill;
            report.BorderStyle = BorderStyle.None; report.BackColor = Palette.Background; report.ForeColor = Palette.Text;
            report.AccessibleName = "관측한 조작, 가능한 원리와 다음 랩 과제";
            header.Controls.Add(corners, 0, 0); header.Controls.Add(traces, 1, 0);
            Controls.Add(report); Controls.Add(header);
            corners.SelectedIndexChanged += delegate { ShowSelection(); };
            traces.Click += delegate {
                var cue = Selected;
                if (cue != null && TraceRequested != null)
                    TraceRequested(ReferenceLap.Finite(cue.LineDivergenceM) ? cue.LineDivergenceM : cue.StartDistanceM);
            };
        }

        LiveReferenceView Selected
        {
            get { return corners.SelectedIndex >= 0 && corners.SelectedIndex < reviews.Length ? reviews[corners.SelectedIndex] : null; }
        }
        static string Metres(double value) { return value.ToString("N0", CultureInfo.CurrentCulture) + " m"; }
        static string Title(LiveReferenceView cue)
        {
            bool timed = ReferenceLap.Finite(cue.TimeLossSeconds);
            string delta = timed && Math.Abs(cue.TimeLossSeconds) <= cue.TimeResolutionSeconds ? "시간 차이 측정 오차 안" :
                timed ? cue.TimeLossSeconds.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "초" : "위치·입력 비교";
            return cue.CornerName + "  ·  " + Metres(cue.StartDistanceM) + " – " + Metres(cue.EndDistanceM) +
                "  ·  " + delta;
        }
        public void SetLaps(ReferenceLap reference, ReferenceLap own, ComparisonResult comparison)
        {
            var reports = comparison.Comparable ? LiveReferenceCoach.ReviewLap(reference, own, comparison) : new LiveReferenceView[0];
            corners.BeginUpdate();
            try
            {
                corners.Items.Clear(); reviews = reports;
                foreach (var cue in reviews) corners.Items.Add(Title(cue));
                // A timed loss chooses where to look first. The explanation itself comes from measured inputs.
                int focus = 0;
                double largestLoss = 0;
                for (int i = 0; i < reviews.Length; i++)
                    if (reviews[i].TimeLossSeconds > Math.Max(largestLoss, reviews[i].TimeResolutionSeconds))
                    { largestLoss = reviews[i].TimeLossSeconds; focus = i; }
                if (reviews.Length > 0) corners.SelectedIndex = focus;
            }
            finally { corners.EndUpdate(); }
            corners.Enabled = traces.Enabled = reviews.Length > 0;
            if (reviews.Length > 0) return;
            report.Text = reference == null ? "LMU 서킷 훈련에서 ‘내 첫 랩으로 연습 시작’을 누르거나 이 화면에서 기준 주행을 여세요." :
                own == null ? "기준 랩을 준비했습니다. 같은 차량과 레이아웃에서 다음 유효 랩을 완주하거나 ‘내 기록 열기’로 비교할 랩을 선택하세요." :
                !comparison.Comparable ? comparison.Reason : "두 주행에서 처음부터 끝까지 관측한 감속·회전 구간을 찾지 못했습니다. ‘입력 · 시간 · 주행선’에서 기록이 겹치는 위치를 살펴보세요.";
        }
        void ShowSelection()
        {
            var cue = Selected;
            if (cue == null) return;
            var text = new StringBuilder();
            text.AppendLine(cue.Heading).AppendLine().AppendLine(cue.Observation);
            if (!String.IsNullOrEmpty(cue.LineObservation) && !cue.Observation.Contains(cue.LineObservation))
                text.AppendLine(cue.LineObservation);
            text.AppendLine().AppendLine("가능한 원리").AppendLine(cue.Principle);
            text.AppendLine().AppendLine("다음 랩에 바꿀 한 가지").AppendLine(cue.NextAction);
            if (!String.IsNullOrEmpty(cue.Estimate)) text.AppendLine().AppendLine(cue.Estimate);
            report.Text = text.ToString().TrimEnd(); report.SelectionStart = 0; report.ScrollToCaret();
        }
    }
}
