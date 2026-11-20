using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    public sealed class CoachView
    {
        public string Status = "주행 데이터 대기";
        public string Headline = "완전한 유효 랩 1회를 먼저 기록하세요.";
        public string Detail = "내 이전 유효 랩과 같은 코스 위치에서 비교합니다.";
        public string ResultSummary = "";
        public string NextAction = "첫 바퀴는 평소처럼 달리세요. 코너를 빠져나온 뒤 짧게 알려드립니다.";
        public TelemetryFrame ReferenceAtPosition;
        public TelemetryFrame[] RecentTrace = new TelemetryFrame[0];
        public double ReferenceBrakeDistance = double.NaN;
        public double ReferenceDistanceUncertaintyM = double.NaN;
        public int CompletedLaps, ReferenceLap = -1;
        public bool HasReference, NewFeedback;
        public string FeedbackId = "";
    }

    public sealed class CornerFeedback
    {
        public string Context;
        public int Lap, ReferenceLap;
        public double StartDistance, EndDistance;
        public double BrakeStartDeltaM, BrakeReleaseDeltaM, ExitSpeedDeltaKph;
        public double SegmentTimeDeltaSeconds, CoastTimeDeltaSeconds, FuelDeltaLitres;
        public double DistanceUncertaintyM;
        public double SegmentTimeUncertaintySeconds;
        public string Headline, Detail, NextAction, ResultSummary;
    }

    // This engine compares observations. It does not infer tyre forces, a fastest
    // line, or a causal handling diagnosis from pedals and steering alone.
    public sealed class CoachEngine
    {
        const int MaxFrames = 24000;
        const double MaxGap = 1.0;
        readonly List<TelemetryFrame> current = new List<TelemetryFrame>();
        readonly List<TelemetryFrame> recent = new List<TelemetryFrame>();
        readonly List<Zone> zones = new List<Zone>();
        readonly List<Pending> pending = new List<Pending>();
        readonly HashSet<Zone> matchedReferenceZones = new HashSet<Zone>();
        readonly List<CornerFeedback> feedbackHistory = new List<CornerFeedback>();
        TelemetryFrame last, brakeCandidate;
        LapRecord previousValid, lapReference;
        Zone activeZone;
        string identity = "", status = "주행 데이터 대기";
        bool capturedStart, currentValid;
        int completedLaps;
        double lowBrakeAt = double.NaN, clearAt = double.NaN;
        double feedbackUntil = -1;
        CornerFeedback feedback;
        bool isNewFeedback;

        sealed class LapRecord
        {
            public int Number;
            public string Identity;
            public TelemetryFrame[] Frames;
            public Zone[] Zones;
        }
        sealed class Zone
        {
            public double StartS, ReleaseS = double.NaN, EndS, MinS, MinSpeed;
            public double StartTime, PeakBrake;
            public double StartUncertainty, ReleaseUncertainty, EndUncertainty;
        }
        sealed class Pending
        {
            public Zone Current, Reference;
            public double End;
        }

        static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
        static string Signed(double v, string format) { return Finite(v) ? (v > 0 ? "+" : "") + v.ToString(format, CultureInfo.InvariantCulture) : "미측정"; }
        static double Blend(double a, double b, double t) { return Finite(a) && Finite(b) ? a + (b - a) * t : double.NaN; }
        static object Number(double value) { return Finite(value) ? (object)value : null; }
        static double PositionUncertainty(TelemetryFrame f)
        {
            double age=Finite(f.DistanceAgeSeconds)?Clamp(f.DistanceAgeSeconds,0,.5):.5;
            // Engineering estimate, not a guaranteed error bound: allow one
            // 20 Hz input interval and up to ~6 g acceleration during score lag.
            return 3+f.SpeedKph/3.6*(age+.05)+30*age*age;
        }
        static string ApproxMetres(double value)
        { return Finite(value)?Signed(Math.Round(value/10)*10,"F0"):"미측정"; }
        static TelemetryFrame Copy(TelemetryFrame f)
        {
            return new TelemetryFrame { Time=f.Time, LapDistance=f.LapDistance, TrackLength=f.TrackLength,
                SpeedKph=f.SpeedKph, Throttle=f.Throttle, Brake=f.Brake, Clutch=f.Clutch, Steering=f.Steering,
                FuelLitres=f.FuelLitres, DistanceAgeSeconds=f.DistanceAgeSeconds, X=f.X, Y=f.Y, Z=f.Z, YawRate=f.YawRate, LateralG=f.LateralG,
                Handbrake=f.Handbrake, LongitudinalG=f.LongitudinalG, LocalVelocityRight=f.LocalVelocityRight, LocalVelocityForward=f.LocalVelocityForward,
                LocalVelocitySideAbs=f.LocalVelocitySideAbs, TimeIsCaptureClock=f.TimeIsCaptureClock,
                Lap=f.Lap, Gear=f.Gear, Track=f.Track, Car=f.Car, GameVersion=f.GameVersion,
                InPit=f.InPit, IsReplay=f.IsReplay, IsValidLap=f.IsValidLap, ValidLapKnown=f.ValidLapKnown,
                OnTrack=f.OnTrack, CaptureUtc=f.CaptureUtc };
        }
        static string Key(TelemetryFrame f)
        {
            return (f.Track ?? "") + "|" + (f.Car ?? "") + "|" + (f.GameVersion ?? "") + "|" + f.TrackLength.ToString("F1", CultureInfo.InvariantCulture);
        }
        static bool Usable(TelemetryFrame f)
        {
            return f != null && !f.TimeIsCaptureClock && Finite(f.Time) && f.Time >= 0 && f.Lap >= 0 &&
                Finite(f.TrackLength) && f.TrackLength >= 100 && f.TrackLength <= 100000 &&
                Finite(f.LapDistance) && f.LapDistance >= -1 && f.LapDistance <= f.TrackLength + 1 &&
                Finite(f.SpeedKph) && f.SpeedKph >= 0 && f.SpeedKph <= 550 &&
                Finite(f.Brake) && f.Brake >= 0 && f.Brake <= 1 &&
                Finite(f.Throttle) && f.Throttle >= 0 && f.Throttle <= 1 &&
                Finite(f.Steering) && Math.Abs(f.Steering) <= 1.01 &&
                (!Finite(f.DistanceAgeSeconds) || (f.DistanceAgeSeconds>=0 && f.DistanceAgeSeconds<=.5)) &&
                !string.IsNullOrWhiteSpace(f.Track) && !string.IsNullOrWhiteSpace(f.Car);
        }
        void ClearTracking(bool clearReference)
        {
            current.Clear(); recent.Clear(); zones.Clear(); pending.Clear(); matchedReferenceZones.Clear();
            activeZone=null; brakeCandidate=null; lowBrakeAt=clearAt=double.NaN;
            capturedStart=false; currentValid=false; last=null; lapReference=null;
            feedback=null; feedbackUntil=-1; isNewFeedback=false;
            if(clearReference) { previousValid=null; completedLaps=0; identity=""; }
        }
        public void Reset()
        {
            ClearTracking(true); feedbackHistory.Clear(); status="기록을 새로 시작합니다. 완전한 유효 랩을 달리세요.";
        }
        public CoachView Disconnect(string message)
        {
            ClearTracking(true); status=string.IsNullOrWhiteSpace(message)?"게임 연결 대기":message;
            return View(null);
        }

        public CoachView Update(TelemetryFrame incoming)
        {
            isNewFeedback=false;
            if(!Usable(incoming)) return Disconnect("필수 텔레메트리 부족 · 비교를 중단했습니다.");
            TelemetryFrame f=Copy(incoming); f.LapDistance=Clamp(f.LapDistance,0,f.TrackLength);
            if(f.IsReplay || f.InPit || !f.OnTrack)
            {
                ClearTracking(true);
                status=f.IsReplay?"리플레이 · 주행 기록 제외":f.InPit?"피트 · 출차 후 새 유효 랩을 기록합니다.":"주행 세션 대기";
                return View(f);
            }
            string key=Key(f);
            if(identity != key) { ClearTracking(true); identity=key; }
            if(last != null && f.Time < last.Time - .01) { ClearTracking(true); identity=key; }
            if(last != null && f.Time <= last.Time + .000001) return View(f);
            if(last != null && f.Time-last.Time > MaxGap)
            {
                // A recording gap may hide a pit visit or track-limit invalidation.
                ClearTracking(true); identity=key;
                status="데이터가 끊겨 현재 랩을 제외했습니다. 새 유효 랩을 기다립니다.";
            }
            if(last == null)
            {
                current.Add(f); last=f; currentValid=f.ValidLapKnown && f.IsValidLap;
                recent.Add(f); status="진입 중인 랩은 제외 · 다음 출발선부터 기록합니다.";
                return View(f);
            }
            bool boundary=f.Lap==last.Lap+1 && last.LapDistance>f.TrackLength*.85 && f.LapDistance<f.TrackLength*.15;
            double travelled=boundary?f.TrackLength-last.LapDistance+f.LapDistance:f.LapDistance-last.LapDistance;
            // Scoring distance can update more slowly than pedals. Allow coarse
            // updates, but never stitch a teleport into a complete reference lap.
            double plausible=Math.Max(80,Math.Max(f.SpeedKph,last.SpeedKph)/3.6*(f.Time-last.Time)*3+25);
            if(travelled>plausible)return Disconnect("위치 데이터가 불연속입니다. 새 유효 랩부터 다시 기록합니다.");
            if(f.Lap != last.Lap)
            {
                if(boundary)
                {
                    double gap=(f.TrackLength-last.LapDistance)+f.LapDistance;
                    double fraction=gap>0?(f.TrackLength-last.LapDistance)/gap:1;
                    TelemetryFrame end=Interpolate(last,f,Clamp(fraction,0,1));
                    end.Lap=last.Lap; end.LapDistance=f.TrackLength;
                    end.IsValidLap=last.IsValidLap; end.ValidLapKnown=last.ValidLapKnown;
                    Append(end);
                    if(capturedStart && currentValid && f.ValidLapKnown && current.Count>20 && current[0].LapDistance==0)
                    {
                        previousValid=new LapRecord { Number=last.Lap, Identity=identity,
                            Frames=current.Select(Copy).ToArray(), Zones=zones.ToArray() };
                        completedLaps++;
                    }
                    current.Clear(); zones.Clear(); pending.Clear(); matchedReferenceZones.Clear(); activeZone=null; brakeCandidate=null;
                    lowBrakeAt=clearAt=double.NaN; capturedStart=true;
                    currentValid=f.ValidLapKnown && f.IsValidLap;
                    lapReference=previousValid;
                    TelemetryFrame start=Copy(end); start.Lap=f.Lap; start.LapDistance=0;
                    start.IsValidLap=f.IsValidLap;start.ValidLapKnown=f.ValidLapKnown;
                    current.Add(start);
                }
                else
                {
                    ClearTracking(true); identity=key;
                    currentValid=false; current.Add(f); last=f;
                    status="랩 이동을 감지해 기록을 다시 시작합니다.";
                    return View(f);
                }
            }
            else if(f.LapDistance<last.LapDistance-2)
            {
                currentValid=false; activeZone=null; pending.Clear(); brakeCandidate=null;feedback=null;feedbackUntil=-1;
            }
            if(!f.ValidLapKnown || !f.IsValidLap) { currentValid=false; pending.Clear(); feedback=null;feedbackUntil=-1; }
            if(current.Count>=MaxFrames || (current.Count>0 && f.Time-current[0].Time>1200))
            {
                currentValid=false; capturedStart=false; current.Clear(); zones.Clear(); pending.Clear(); activeZone=null;
            }
            Append(f); recent.Add(f); while(recent.Count>0 && recent[0].Time<f.Time-10)recent.RemoveAt(0);
            if(currentValid && capturedStart) { DetectZone(f); EvaluatePending(f); }
            else { activeZone=null; brakeCandidate=null; }
            last=f;
            status=!f.ValidLapKnown?"유효 랩 신호 미제공 · 기준 랩으로 채택하지 않습니다.":!currentValid?"현재 랩 제외 · 다음 유효 랩을 기다립니다.":!capturedStart?"다음 출발선부터 온전한 랩을 기록합니다.":lapReference==null?"첫 유효 랩 기록 중 · 평소 페이스로 달리세요.":"실시간 비교 · 직전 유효 랩 "+lapReference.Number;
            return View(f);
        }
        void Append(TelemetryFrame f)
        {
            if(current.Count>0)
            {
                if(f.Time<=current[current.Count-1].Time+.000001)return;
                if(f.LapDistance<current[current.Count-1].LapDistance)
                {
                    f=Copy(f); f.LapDistance=current[current.Count-1].LapDistance;
                }
            }
            current.Add(f);
        }
        void DetectZone(TelemetryFrame f)
        {
            if(activeZone==null)
            {
                if(f.Brake>=.15 && f.SpeedKph>=35)
                {
                    if(brakeCandidate==null)brakeCandidate=f;
                    if(f.Time-brakeCandidate.Time>=.1)
                    {
                        activeZone=new Zone { StartS=brakeCandidate.LapDistance, StartTime=brakeCandidate.Time,
                            MinS=f.LapDistance, MinSpeed=f.SpeedKph, PeakBrake=f.Brake,StartUncertainty=PositionUncertainty(brakeCandidate) };
                        lowBrakeAt=clearAt=double.NaN;
                    }
                }
                else brakeCandidate=null;
                return;
            }
            Zone z=activeZone; z.PeakBrake=Math.Max(z.PeakBrake,f.Brake);
            if(f.SpeedKph<z.MinSpeed) { z.MinSpeed=f.SpeedKph; z.MinS=f.LapDistance; }
            if(f.Brake<=.08)
            {
                if(!Finite(lowBrakeAt))lowBrakeAt=f.Time;
                if(f.Time-lowBrakeAt>=.15 && !Finite(z.ReleaseS))
                {
                    TelemetryFrame release=current.Find(p=>p.Time>=lowBrakeAt); z.ReleaseS=release==null?f.LapDistance:release.LapDistance;
                    z.ReleaseUncertainty=PositionUncertainty(release??f);
                }
            }
            else { lowBrakeAt=double.NaN; z.ReleaseS=double.NaN; }
            bool clear=f.Brake<.08 && f.Throttle>.55 && Math.Abs(f.Steering)<.3 && f.SpeedKph>=z.MinSpeed+8;
            if(clear) { if(!Finite(clearAt))clearAt=f.Time; } else clearAt=double.NaN;
            bool finished=Finite(clearAt) && f.Time-clearAt>=.6 && f.Time-z.StartTime>=1.5 && f.LapDistance-z.StartS>=30;
            if(finished)
            {
                z.EndS=f.LapDistance;z.EndUncertainty=PositionUncertainty(f);
                if(z.PeakBrake>=.3 && Finite(z.ReleaseS))
                {
                    zones.Add(z);
                    if(lapReference!=null)
                    {
                        Zone[] matches=lapReference.Zones.Where(p=>!matchedReferenceZones.Contains(p) && Math.Abs(p.MinS-z.MinS)<=100 && Math.Abs(p.StartS-z.StartS)<=100 && Math.Min(p.EndS,z.EndS)>Math.Max(p.StartS,z.StartS)).OrderBy(p=>Math.Abs(p.MinS-z.MinS)).ToArray();
                        bool ambiguous=matches.Length>1 && Math.Abs(matches[1].MinS-z.MinS)-Math.Abs(matches[0].MinS-z.MinS)<20;
                        if(matches.Length>0 && !ambiguous)
                        {
                            Zone match=matches[0];matchedReferenceZones.Add(match);
                            pending.Add(new Pending { Current=z, Reference=match, End=Math.Max(z.EndS,match.EndS) });
                        }
                    }
                }
                activeZone=null; brakeCandidate=null; lowBrakeAt=clearAt=double.NaN;
            }
            else if(f.Time-z.StartTime>30 || f.LapDistance-z.StartS>700)
            { activeZone=null; brakeCandidate=null; lowBrakeAt=clearAt=double.NaN; }
        }
        void EvaluatePending(TelemetryFrame f)
        {
            for(int i=pending.Count-1;i>=0;i--)
            {
                Pending p=pending[i]; if(f.LapDistance<p.End)continue;
                pending.RemoveAt(i);
                double start=Math.Min(p.Current.StartS,p.Reference.StartS),end=p.End;
                TelemetryFrame a=At(current,start),b=At(current,end),ra=At(lapReference.Frames,start),rb=At(lapReference.Frames,end);
                if(a==null || b==null || ra==null || rb==null)continue;
                double fuel=Finite(a.FuelLitres)&&Finite(ra.FuelLitres)?a.FuelLitres-ra.FuelLitres:double.NaN;
                double brakeUncertainty=p.Current.StartUncertainty+p.Reference.StartUncertainty+5;
                double releaseUncertainty=p.Current.ReleaseUncertainty+p.Reference.ReleaseUncertainty+5;
                double distanceUncertainty=Math.Max(brakeUncertainty,Math.Max(releaseUncertainty,p.Current.EndUncertainty+p.Reference.EndUncertainty+5));
                CornerFeedback result=new CornerFeedback { Context=identity,Lap=f.Lap,ReferenceLap=lapReference.Number,StartDistance=start,EndDistance=end,
                    BrakeStartDeltaM=p.Current.StartS-p.Reference.StartS,BrakeReleaseDeltaM=p.Current.ReleaseS-p.Reference.ReleaseS,
                    ExitSpeedDeltaKph=b.SpeedKph-rb.SpeedKph,SegmentTimeDeltaSeconds=(b.Time-a.Time)-(rb.Time-ra.Time),
                    CoastTimeDeltaSeconds=Coasting(current,start,end)-Coasting(lapReference.Frames,start,end),FuelDeltaLitres=fuel,DistanceUncertaintyM=distanceUncertainty,
                    SegmentTimeUncertaintySeconds=2*distanceUncertainty/Math.Max(10,Math.Min(Math.Min(a.SpeedKph,b.SpeedKph),Math.Min(ra.SpeedKph,rb.SpeedKph))/3.6) };
                bool brakeDifferent=Math.Abs(result.BrakeStartDeltaM)>brakeUncertainty;
                bool releaseDifferent=Math.Abs(result.BrakeReleaseDeltaM)>releaseUncertainty;
                string brakeChange=!brakeDifferent?"제동 위치 차이는 거리 추정 오차 범위 안입니다.":"제동을 약 "+Math.Abs(Math.Round(result.BrakeStartDeltaM/10)*10).ToString("F0",CultureInfo.InvariantCulture)+" m "+(result.BrakeStartDeltaM>0?"늦게":"일찍")+" 시작했습니다.";
                result.Headline="코너 약 "+(Math.Round(p.Current.MinS/10)*10).ToString("F0",CultureInfo.InvariantCulture)+" m · "+brakeChange;
                string uncertaintyText=(Math.Ceiling(distanceUncertainty/5)*5).ToString("F0",CultureInfo.InvariantCulture);
                result.Detail="직전 유효 랩 "+result.ReferenceLap+" 대비: 해제 위치 약 "+ApproxMetres(result.BrakeReleaseDeltaM)+" m · 같은 탈출 표시 지점 속도 이전 약 "+(Math.Round(rb.SpeedKph/5)*5).ToString("F0",CultureInfo.InvariantCulture)+" / 이번 약 "+(Math.Round(b.SpeedKph/5)*5).ToString("F0",CultureInfo.InvariantCulture)+" km/h · 거리 오차 추정 약 ±"+uncertaintyText+" m";
                result.ResultSummary=(brakeDifferent?"제동 시작 약 "+ApproxMetres(result.BrakeStartDeltaM)+" m (+는 늦음)":"제동 위치 차이: 추정 오차 범위 안")+"\n해제 약 "+ApproxMetres(result.BrakeReleaseDeltaM)+" m · 거리 오차 추정 ±"+uncertaintyText+" m";
                if(!Finite(fuel) || Math.Abs(fuel)>10)
                    result.Detail+=" · 연료 조건 차이/미확인: 시간 평가는 생략";
                else if(distanceUncertainty>15)result.Detail+=" · 거리 정밀도 부족: 시간 평가는 생략";
                else if(Math.Abs(result.SegmentTimeDeltaSeconds)<=result.SegmentTimeUncertaintySeconds)result.Detail+=" · 구간 시간차: 표본 오차 추정 범위 안";
                else result.Detail+=" · 같은 구간 시간차 약 "+Signed(result.SegmentTimeDeltaSeconds,"F1")+" s";
                if(brakeDifferent)
                    result.NextAction="다음 랩은 제동 시작 기준물을 고정하고, 해제 동작만 바꿔 비교하세요.";
                else if(releaseDifferent)
                    result.NextAction="다음 랩은 제동 시작을 유지하고, 브레이크 해제 위치만 이전 랩과 비교하세요.";
                else if(result.CoastTimeDeltaSeconds>.4 && distanceUncertainty<=15)
                    result.NextAction="가속·제동이 모두 없는 시간이 늘었습니다. 그 구간의 진입 속도를 먼저 확인하세요.";
                else result.NextAction="같은 입력을 한 번 더 재현해 보세요. 연료·타이어·트래픽 영향도 함께 확인하세요.";
                feedback=result; feedbackUntil=f.Time+8; isNewFeedback=true;
                feedbackHistory.Add(result); if(feedbackHistory.Count>50)feedbackHistory.RemoveAt(0);
            }
        }
        static double Coasting(IList<TelemetryFrame> frames,double from,double to)
        {
            TelemetryFrame a=At(frames,from),b=At(frames,to); if(a==null||b==null)return double.NaN;
            List<TelemetryFrame> points=new List<TelemetryFrame>(); points.Add(a);
            points.AddRange(frames.Where(p=>p.LapDistance>from && p.LapDistance<to)); points.Add(b);
            double sum=0;
            for(int i=1;i<points.Count;i++)
                if(points[i-1].Brake<.05 && points[i-1].Throttle<.05)sum+=points[i].Time-points[i-1].Time;
            return sum;
        }
        static TelemetryFrame Interpolate(TelemetryFrame a,TelemetryFrame b,double t)
        {
            TelemetryFrame f=Copy(t<.5?a:b);f.Time=Blend(a.Time,b.Time,t);f.LapDistance=Blend(a.LapDistance,b.LapDistance,t);
            f.SpeedKph=Blend(a.SpeedKph,b.SpeedKph,t);f.Brake=Blend(a.Brake,b.Brake,t);f.Throttle=Blend(a.Throttle,b.Throttle,t);
            f.Steering=Blend(a.Steering,b.Steering,t);f.Clutch=Blend(a.Clutch,b.Clutch,t);f.FuelLitres=Blend(a.FuelLitres,b.FuelLitres,t);
            f.DistanceAgeSeconds=Blend(a.DistanceAgeSeconds,b.DistanceAgeSeconds,t);
            f.X=Blend(a.X,b.X,t);f.Y=Blend(a.Y,b.Y,t);f.Z=Blend(a.Z,b.Z,t);f.YawRate=Blend(a.YawRate,b.YawRate,t);f.LateralG=Blend(a.LateralG,b.LateralG,t);
            return f;
        }
        static TelemetryFrame At(IList<TelemetryFrame> frames,double s)
        {
            if(frames==null || frames.Count==0 || s<frames[0].LapDistance || s>frames[frames.Count-1].LapDistance)return null;
            int lo=0,hi=frames.Count-1;
            while(lo<hi) { int mid=(lo+hi)/2;if(frames[mid].LapDistance<s)lo=mid+1;else hi=mid; }
            if(lo==0 || frames[lo].LapDistance==s)return Copy(frames[lo]);
            TelemetryFrame a=frames[lo-1],b=frames[lo];
            return Interpolate(a,b,(s-a.LapDistance)/Math.Max(.000001,b.LapDistance-a.LapDistance));
        }
        CoachView View(TelemetryFrame frame)
        {
            CoachView view=new CoachView { Status=status,CompletedLaps=completedLaps,HasReference=lapReference!=null,
                ReferenceLap=lapReference==null?-1:lapReference.Number, RecentTrace=recent.Select(Copy).ToArray() };
            if(frame!=null && lapReference!=null)
            {
                view.ReferenceAtPosition=At(lapReference.Frames,frame.LapDistance);
                Zone next=lapReference.Zones.FirstOrDefault(z=>z.StartS>frame.LapDistance+3);
                if(next!=null && next.StartS-frame.LapDistance<=500)view.ReferenceBrakeDistance=next.StartS-frame.LapDistance;
                if(next!=null)view.ReferenceDistanceUncertaintyM=PositionUncertainty(frame)+next.StartUncertainty+5;
                view.Headline="직전 유효 랩 "+lapReference.Number+"와 같은 위치의 입력을 비교합니다.";
                view.Detail="기준 랩은 이번 랩이 끝날 때까지 고정됩니다. 실제 최적 입력을 뜻하지 않습니다.";
                view.NextAction="이번 랩은 입력 하나만 바꾸세요. 코너 탈출 뒤 결과를 알려드립니다.";
            }
            if(frame!=null && feedback!=null && frame.Time<=feedbackUntil)
            {
                view.Headline=feedback.Headline;view.Detail=feedback.Detail;view.NextAction=feedback.NextAction;
                view.ResultSummary=feedback.ResultSummary;
                view.NewFeedback=isNewFeedback;view.FeedbackId=feedback.Lap+":"+feedback.StartDistance.ToString("F1",CultureInfo.InvariantCulture);
            }
            return view;
        }
        static object FrameData(TelemetryFrame f)
        {
            return new { time=Number(f.Time),lap=f.Lap,distanceM=Number(f.LapDistance),speedKph=Number(f.SpeedKph),
                brake=Number(f.Brake),throttle=Number(f.Throttle),steering=Number(f.Steering),clutch=Number(f.Clutch),
                fuelLitres=Number(f.FuelLitres),distanceAgeSeconds=Number(f.DistanceAgeSeconds),gear=f.Gear,validityKnown=f.ValidLapKnown,validLap=f.IsValidLap };
        }
        public string ExportJson()
        {
            JavaScriptSerializer serializer=new JavaScriptSerializer { MaxJsonLength=32*1024*1024 };
            object data=new { schemaVersion=1,source="selected game's telemetry adapter",exportedAtUtc=DateTime.UtcNow.ToString("o"),
                comparison="previous complete valid own lap; observations, not a fastest line or causal diagnosis",
                context=identity,status=status,completedLaps=completedLaps,
                currentLapComplete=false,currentLapEligible=capturedStart&&currentValid,currentFrames=current.Select(FrameData).ToArray(),
                referenceLap=lapReference==null?(int?)null:lapReference.Number,
                referenceFrames=lapReference==null?new object[0]:lapReference.Frames.Select(FrameData).ToArray(),
                feedback=feedbackHistory.Select(x=>new {context=x.Context,lap=x.Lap,referenceLap=x.ReferenceLap,startM=Number(x.StartDistance),endM=Number(x.EndDistance),
                    brakeStartDeltaM=Number(x.BrakeStartDeltaM),brakeReleaseDeltaM=Number(x.BrakeReleaseDeltaM),exitSpeedDeltaKph=Number(x.ExitSpeedDeltaKph),
                    segmentTimeDeltaSeconds=Number(x.SegmentTimeDeltaSeconds),segmentTimeUncertaintySeconds=Number(x.SegmentTimeUncertaintySeconds),coastTimeDeltaSeconds=Number(x.CoastTimeDeltaSeconds),fuelDeltaLitres=Number(x.FuelDeltaLitres),distanceUncertaintyM=Number(x.DistanceUncertaintyM),
                    headline=x.Headline,detail=x.Detail,nextAction=x.NextAction,resultSummary=x.ResultSummary}).ToArray() };
            return serializer.Serialize(data);
        }
    }
}
