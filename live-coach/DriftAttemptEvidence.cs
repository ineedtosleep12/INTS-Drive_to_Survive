using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntsLiveCoach
{
    internal enum DriftOutcome { GripTurn, Straightened, ContinuedRotation, MissedTransfer }

    // Short histories belong to a single unbroken stream. Angles are magnitudes:
    // the AC adapter does not verify which front-wheel direction is countersteer.
    internal sealed class DriftAttemptEvidence
    {
        struct Sample
        {
            internal double Time, Angle, Yaw, Throttle, Steer, Forward;
        }
        readonly List<Sample> samples = new List<Sample>();
        const double TriggerAngle = 12;
        const string Limits = "차체 속도·회전과 운전자 입력의 순서를 봅니다. 타이어 접지와 운전 의도는 직접 측정하지 않습니다.";

        internal void Reset() { samples.Clear(); }
        internal void Observe(TelemetryFrame f, double angle)
        {
            // Keep a bounded 20–40 Hz history even if the caller polls faster.
            if (samples.Count > 0 && f.Time - samples[samples.Count-1].Time < .025) return;
            samples.RemoveAll(s => s.Time < f.Time - 3.2);
            samples.Add(new Sample {Time=f.Time, Angle=angle, Yaw=f.YawRate, Throttle=f.Throttle,
                Steer=Math.Abs(f.Steering), Forward=f.LocalVelocityForward});
        }
        static string N(double value) { return value.ToString("0.0", CultureInfo.InvariantCulture); }
        static string P(double value) { return Math.Round(100*value).ToString(CultureInfo.InvariantCulture)+"%"; }
        static bool Unit(double value) { return value>=0 && value<=1; }
        static string Span(string channel, double a, double b) { return channel+" "+P(a)+" → "+P(b); }

        internal bool IsGripTurn
        {
            get
            {
                if (samples.Count==0) return false;
                var end=samples[samples.Count-1];
                int sign=Math.Sign(end.Yaw);
                for (int i=samples.Count-1; i>=0; i--)
                {
                    var s=samples[i];
                    if (s.Forward<=2 || s.Angle>=9 || Math.Abs(s.Yaw)<.18 || Math.Sign(s.Yaw)!=sign) break;
                    if (end.Time-s.Time>=.7) return true;
                }
                return false;
            }
        }

        internal CoachPrincipleView Describe(DriftOutcome outcome, double slideStart, double lastSlide, int yawSign)
        {
            if (samples.Count==0) return null;
            var end=samples[samples.Count-1];
            if (outcome==DriftOutcome.GripTurn) return GripTurn(end);
            int first=-1;
            for (int i=samples.Count-1; i>=0; i--)
            {
                var s=samples[i];
                if (s.Time>lastSlide+.001) continue;
                if (s.Time<Math.Max(slideStart,lastSlide-.9) || s.Angle<9 ||
                    s.Forward<=2 || Math.Sign(s.Yaw)!=yawSign || Math.Abs(s.Yaw)<.18) break;
                first=i;
            }
            if (first<0) return null;
            var start=samples[first];
            if (outcome==DriftOutcome.MissedTransfer)
            {
                // Look for real opposite yaw, not a steering sign. A brief reversal
                // can end before a linked slide forms. A long straight may be intentional.
                double opposite=0, began=double.NaN;
                for (int i=first; i<samples.Count; i++)
                {
                    var s=samples[i];
                    if (s.Time<=lastSlide || s.Angle<TriggerAngle || Math.Abs(s.Yaw)<.18 ||
                        Math.Sign(s.Yaw)!=-yawSign || s.Forward<=2) { began=double.NaN; continue; }
                    if (double.IsNaN(began)) began=s.Time;
                    opposite=Math.Max(opposite,s.Time-began);
                }
                if (opposite>=.05)
                    return Finding("반대 회전이 짧게 끝났어요",
                        "첫 방향이 끝난 뒤 반대 방향 옆미끄러짐이 약 "+N(opposite)+"초 이어졌습니다.",
                        "새 방향의 회전이 자리 잡기 전에 차가 펴지면 두 굽이의 연결이 끊길 수 있어요.",
                        "두 굽이를 이을 때는 첫 원의 끝에서 기존 카운터를 풀기 시작하는 시점만 조금 앞당겨 보세요.");
                var result=Finding("다음 방향으로 연결이 끊겼어요",
                    "첫 방향 뒤 "+N(end.Time-lastSlide)+"초 동안 이어지는 반대 옆미끄러짐이 관측되지 않았습니다.",
                    "차체가 새 방향으로 회전하고 옆으로도 움직여야 다음 미끄러짐이 이어져요.",
                    "두 굽이를 이으려던 구간이라면 첫 굽이 출구에서 카운터를 풀기 시작할 표지 하나를 정하세요.");
                var flat=FindFlat(first,lastSlide);
                if (flat>=0) AddControls(result,first,flat,false);
                return result;
            }
            bool spin=outcome==DriftOutcome.ContinuedRotation;
            int target=spin?samples.Count-1:FindFlat(first,lastSlide);
            if (target<0) return null;
            end=samples[target];
            var finding=spin?
                Finding("전진이 줄고 차가 계속 돌아요",
                    "직전 차체각 "+N(start.Angle)+"° → "+N(end.Angle)+"° · 앞쪽 속도 "+N(start.Forward*3.6)+" → "+N(end.Forward*3.6)+" km/h",
                    "차가 계속 도는 동안 진행 방향은 그대로 남아 차체 앞쪽으로 나아가는 속도가 줄 수 있어요.",
                    "같은 원에서 각도가 커지기 시작할 때 가속을 더 얹는 동작을 한 번 늦춰 보세요."):
                Finding("차가 진행 방향으로 펴졌어요",
                    "직전 차체각 "+N(start.Angle)+"° → "+N(end.Angle)+"°",
                    "차체와 이동 방향이 가까워지면 옆미끄러짐이 줄어요.",
                    "같은 진입에서 차가 펴지기 시작할 때 기존 카운터를 풀기 시작하는 시점만 바꿔 보세요.");
            AddControls(finding,first,target,spin);
            return finding;
        }

        int FindFlat(int first, double lastSlide)
        {
            return samples.FindIndex(first,s => s.Time>=lastSlide && s.Angle<=6 && s.Forward>2);
        }

        CoachPrincipleView GripTurn(Sample end)
        {
            if (!IsGripTurn) return null;
            int first=samples.FindIndex(s=>s.Time>=end.Time-.7);
            var start=samples[first]; double peak=0;
            for (int i=first; i<samples.Count; i++) peak=Math.Max(peak,samples[i].Angle);
            string observed="같은 방향 선회 약 "+N(end.Time-start.Time)+"초 · 차체각 최대 "+N(peak)+"°";
            if (Unit(start.Throttle) && Unit(end.Throttle)) observed+=" · "+Span("가속",start.Throttle,end.Throttle);
            return Finding("선회 중 · 차체각이 작아요", observed,
                "차가 도는 것과 뒤가 바깥으로 흐르는 것은 다른 움직임이에요. 지금은 차체와 진행 방향이 가깝습니다.",
                Unit(end.Throttle) && end.Throttle<.75 ?
                "원돌이를 시도한다면 같은 원과 기어에서 가속을 한 번만 조금 보태고 차체각 변화를 보세요.":
                "원돌이를 시도한다면 같은 속도에서 원의 반지름을 일정하게 유지하는 것부터 반복하세요.");
        }

        void AddControls(CoachPrincipleView finding, int first, int last, bool spin)
        {
            var start=samples[first]; var end=samples[last];
            if (Unit(start.Steer) && Unit(end.Steer) && Math.Abs(end.Steer-start.Steer)>=.15)
                finding.Observation+=" · "+Span("조향 크기",start.Steer,end.Steer);
            // Diagnose a pedal lead only when it persisted before an angle change.
            // Simultaneous samples or missing intermediate channels cannot establish order.
            if (!Unit(start.Throttle) || !Unit(end.Throttle)) return;
            string pedal=" · "+Span("가속",start.Throttle,end.Throttle);
            double pedalSince=double.NaN;
            double sign=spin?1:-1;
            for (int i=first+1; i<=last; i++)
            {
                var s=samples[i];
                if (!Unit(s.Throttle)) break;
                if ((s.Angle-start.Angle)*sign>=6)
                {
                    if (!double.IsNaN(pedalSince) && s.Time-pedalSince>=.10 &&
                        (s.Throttle-start.Throttle)*sign>=.18)
                    {
                        pedal=(spin?" · 각도가 6° 이상 커지기 전 ":" · 각도가 6° 이상 줄기 전 ")+Span("가속",start.Throttle,s.Throttle);
                        finding.PossibleMechanism=spin?
                            "후륜구동차는 가속을 늘리면 뒷바퀴가 선회에 쓸 접지가 줄어 회전이 더 커질 수 있어요.":
                            "후륜구동차는 가속을 덜면 뒷바퀴가 옆으로 버티는 힘을 되찾아 차가 펴질 수 있어요.";
                        finding.NextDrill=spin?
                            "같은 진입에서 가속을 올리는 속도만 늦춰 차체각이 계속 커지는지 보세요.":
                            "같은 원과 진입 속도에서 가속을 덜어내는 속도만 늦춰 보세요.";
                    }
                    break;
                }
                if ((s.Throttle-start.Throttle)*sign<.18) pedalSince=double.NaN;
                else if (double.IsNaN(pedalSince)) pedalSince=s.Time;
            }
            finding.Observation+=pedal;
        }
        static CoachPrincipleView Finding(string title,string observed,string why,string next)
        {
            return new CoachPrincipleView {Title=title,Observation=observed,PossibleMechanism=why,
                NextDrill=next,Limits=Limits,HasObservation=true};
        }
        internal static string Review(CoachPrincipleView finding)
        {
            return finding==null?"":finding.Title+"\n"+finding.Observation+"\n"+finding.PossibleMechanism+"\n다음 시도 · "+finding.NextDrill;
        }
    }
}
