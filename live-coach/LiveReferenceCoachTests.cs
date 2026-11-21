using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntsLiveCoach
{
    public static class LiveReferenceCoachTests
    {
        static int checks;
        static void Check(bool ok,string label) {checks++;if(!ok)throw new Exception("Live reference: "+label);}
        static ReferenceLap Source(string track="Synthetic Circuit A")
        {
            var samples=new List<ReferenceSample>();
            for(int i=0;i<=120;i++)
            {
                double d=i*10;bool first=d>=300&&d<500,second=d>=800&&d<1000;
                samples.Add(new ReferenceSample {Time=i*.2,DistanceM=d,SpeedKph=180,Brake=(d>=300&&d<400)||(d>=800&&d<900)?.7:0,Throttle=first||second?.3:1,Steering=first||second?.3:0});
            }
            return new ReferenceLap {Game="LMU",Car="Synthetic GT3",Track=track,Layout="GP",GameVersion="one",DistanceBasis=ReferenceLap.LmuDistanceBasis,TrackLengthM=1200,Complete=true,Validity="valid",Samples=samples.ToArray()};
        }
        static TelemetryFrame Frame(ReferenceSample s,int lap=1)
        {return new TelemetryFrame {Time=100+s.Time,DistanceAgeSeconds=.1,LapDistance=s.DistanceM,TrackLength=1200,Lap=lap,Car="Synthetic GT3",Track="Synthetic Circuit A",GameVersion="one",OnTrack=true,ValidLapKnown=true,IsValidLap=true,SpeedKph=s.SpeedKph,Brake=s.Brake,Throttle=s.Throttle,Steering=s.Steering,LateralG=0,X=s.X,Z=s.Z};}
        static LiveReferenceCoach Coach(ReferenceLap source=null,bool mapping=false,bool enabled=true,string aliasCar="",string aliasTrack="")
        {var c=new LiveReferenceCoach();c.Configure(new ReferenceLiveSettings(source??Source(),enabled,false,mapping,aliasCar,aliasTrack,"GP"));return c;}
        static List<LiveReferenceView> Drive(LiveReferenceCoach coach,ReferenceLap data,Action<TelemetryFrame> edit=null,double from=0,double to=1200,string game="LMU")
        {
            var output=new List<LiveReferenceView>();
            foreach(var sample in data.Samples.Where(s=>s.DistanceM>=from&&s.DistanceM<=to))
            {var f=Frame(sample);if(edit!=null)edit(f);var v=coach.Update(game,f);if(v.NewFeedback)output.Add(v);}
            return output;
        }
        static List<LiveReferenceView> DriveAccNative(LiveReferenceCoach coach,ReferenceLap data,bool advancing)
        {
            var bytes=AssettoReaderTests.Fixture(AssettoGame.Acc);var observation=new AssettoGraphicsObservation();var clock=new AssettoLapClock();var output=new List<LiveReferenceView>();
            for(int i=0;i<data.Samples.Length;i++)
            {
                var s=data.Samples[i];int packet=advancing?i+50:50,currentMs=1000+(int)Math.Round(s.Time*1000);
                Buffer.BlockCopy(BitConverter.GetBytes(packet),0,bytes[1],0,4);
                Buffer.BlockCopy(BitConverter.GetBytes(currentMs),0,bytes[1],140,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)(s.DistanceM/1200)),0,bytes[1],248,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)s.Throttle),0,bytes[0],4,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)s.Brake),0,bytes[0],8,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)s.Steering),0,bytes[0],24,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)s.SpeedKph),0,bytes[0],28,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)s.X),0,bytes[1],268,4);
                Buffer.BlockCopy(BitConverter.GetBytes((float)(s.Z+2)),0,bytes[1],276,4);
                TelemetryFrame f;string status;
                if(!AssettoReader.TryDecode(AssettoGame.Acc,bytes[0],bytes[1],bytes[2],1200,out f,out status))throw new Exception("ACC native coach fixture: "+status);
                observation.Observe(BitConverter.ToInt32(bytes[1],0),100+s.Time);observation.Apply(f,100+s.Time);
                clock.Apply(f,2,currentMs,90000,0,100+s.Time);
                if(!double.IsNaN(f.DistanceAgeSeconds))throw new Exception("ACC native source age was invented");
                var v=coach.Update("ACC",f);if(v.NewFeedback)output.Add(v);
            }
            return output;
        }
        public static int Run()
        {
            checks=0;var source=Source();var coach=Coach(source);
            Check(coach.PreparedZoneCount==2,"two sustained complexes detected without any circuit-name table");
            var events=Drive(coach,source);
            Check(events.Count==2,"one feedback after each observed complex");
            Check(events.All(e=>Math.Abs(e.TimeLossSeconds)<.000001),"identical trace cannot create artificial corner loss");
            Check(events.All(e=>e.NextAction.Contains("재현")),"small difference does not prescribe a driving change");
            Check(events.All(e=>e.Estimate.Contains("전반/중반/후반")),"phase thirds are not described as verified apexes");
            Check(coach.ExportJson().Contains("firstThirdLoss"),"delivered feedback has exportable evidence");
            Check(!Coach(source,enabled:false).Update("LMU",Frame(source.Samples[0])).Ready,"disabled source is not ready");
            coach=Coach(source);var wrong=Frame(source.Samples[0]);wrong.Car="Other car";
            Check(!coach.Update("LMU",wrong).Ready,"different car never activates reference pedals");
            wrong=Frame(source.Samples[0]);wrong.Track="Other track";Check(!Coach(source).Update("LMU",wrong).Ready,"wrong track held");
            wrong=Frame(source.Samples[0]);wrong.GameVersion="two";Check(!Coach(source).Update("LMU",wrong).Ready,"known physics version mismatch held");
            Check(!Coach(source).Update("ACC",Frame(source.Samples[0])).Ready,"wrong game held even with identical labels");
            coach=Coach(source,aliasCar:"Livery A",aliasTrack:"Native track");
            wrong=Frame(source.Samples[0]);wrong.Car="Livery A";wrong.Track="Native track";
            Check(coach.Update("LMU",wrong).Ready,"explicit exact identity tuple maps live livery and track");
            wrong.Track="Native track 2";Check(!coach.Update("LMU",wrong).Ready,"alias cannot apply after source-track change");
            var other=Source("Synthetic Circuit B");coach=Coach(other);
            events=Drive(coach,other,f=>f.Track="Synthetic Circuit B");Check(events.Count==2,"same mechanism works on an independent circuit identity");
            coach=Coach(source);events=Drive(coach,source,null,350);
            Check(events.Count==1&&events[0].CornerName.EndsWith("2"),"connecting halfway through a corner skips it but observes next full corner");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.IsValidLap=false;});
            Check(events.Count==0,"invalidated lap cannot regain coaching within same lap");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.InPit=true;});Check(events.Count==0,"pit invalidates pending and future cues in that lap");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.IsReplay=true;});Check(events.Count==0,"replay does not create driver results");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.Brake=double.NaN;});Check(events.Count==1,"missing input discards current corner and preserves later full observation");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance>=450)f.Time+=2;});Check(events.Count==1,"long gap cannot bridge a corner comparison");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.LapDistance=440;});Check(events.Count==2,"normal duplicate scoring distance is accepted");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance==450)f.LapDistance=420;});Check(events.Count==1,"backwards position drops the affected corner");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance>=530&&f.LapDistance<=750)f.Steering=.4;});Check(events.Count==1,"no cue during sustained busy steering; stale cue expires before next complex");
            coach=Coach(source);events=Drive(coach,source,f=>{if(f.LapDistance>=530&&f.LapDistance<=750)f.Steering=.4;f.LapDistance+=3;});
            Check(events.Count==1&&events[0].CornerName.EndsWith("2"),"dropping stale previous-corner tail preserves the same overshooting frame at next zone entry");
            coach=Coach(source);events=Drive(coach,source,f=>f.LateralG=double.NaN);Check(events.Count==2,"missing G channel still allows conservative stable-input text feedback");
            coach=Coach(source);events=Drive(coach,source,f=>f.DistanceAgeSeconds=.7);Check(events.Count==0,"stale scoring positions cannot coach");
            coach=Coach(source);events=Drive(coach,source,f=>f.TimeIsCaptureClock=true);Check(events.Count==0,"capture clock cannot become lap time loss");
            coach=Coach(source);events=Drive(coach,source,f=>{
                double added=Math.Max(0,Math.Min(1,(f.LapDistance-350)/150));f.Time+=added;
                if(f.LapDistance>=400&&f.LapDistance<480)f.Brake=.4;
                if(f.LapDistance>=450&&f.LapDistance<=550)f.SpeedKph=170;
            });
            Check(events.Count==2&&events[0].TimeLossSeconds>.9,"local delay is measured in its own complex");
            Check(Math.Abs(events[1].TimeLossSeconds)<.000001,"prior corner cumulative delay never becomes the next corner loss");
            Check(events[0].NextAction.Contains("해제만"),"late brake release plus lower exit speed suggests one controlled release experiment");
            Check(events[0].Principle.Contains("접지"),"feedback connects observation to a conditional tyre-load principle");
            coach=Coach(source);events=Drive(coach,source,f=>{
                f.Time+=Math.Max(0,Math.Min(1,(f.LapDistance-350)/150));
                if(f.LapDistance==480)f.Brake=.4;
                if(f.LapDistance>=450&&f.LapDistance<=550)f.SpeedKph=170;
            });
            Check(!events[0].NextAction.Contains("해제만"),"one isolated brake spike does not invent a sustained late-release prescription");
            coach=Coach(source);events=Drive(coach,source,f=>{
                f.Time+=Math.Max(0,Math.Min(1,(f.LapDistance-430)/100));
                // Extra delay after the fixed reference exit must not move its loss anchor.
                f.Time+=Math.Max(0,Math.Min(1,(f.LapDistance-560)/40));
                if(f.LapDistance>=500&&f.LapDistance<600){f.Steering=.3;f.Throttle=.3;}
            });
            Check(events.Count==2&&events[0].NextAction.Contains("조향을 점진적으로"),"delayed own unwind and throttle after reference exit can produce a gentle steering exercise");
            Check(Math.Abs(events[0].TimeLossSeconds-1)<.000001,"extra observation tail does not shift fixed course timing anchors");
            Check(events[0].Estimate.Contains("자체 최대 입력"),"steering comparison describes relative release timing, not angle or sign");
            coach=Coach(source);Drive(coach,source,null,0,620);coach.Reset();
            Check(string.IsNullOrEmpty(coach.Update("LMU",Frame(source.Samples[63])).Heading),"disconnect/reset clears previous cue while retaining reference");
            coach.Configure(new ReferenceLiveSettings(source,true,false,false));
            Check(!coach.ExportJson().Contains("감속·회전 구간 1"),"configuration change clears previous source history");
            // Independent source clone: edits after configuring never affect live matching.
            var mutable=Source();coach=Coach(mutable);mutable.Car="Changed after configuration";
            Check(coach.Update("LMU",Frame(source.Samples[0])).Ready,"reference settings are isolated from caller mutation");
            var motec=Source();motec.DistanceBasis=ReferenceLap.LmuMotecDistanceBasis;motec.Complete=false;motec.TrackLengthM=double.NaN;
            Check(!Coach(motec).Update("LMU",Frame(source.Samples[0])).Ready,"MoTeC/native mapping remains explicit");
            Check(Coach(motec,true).Update("LMU",Frame(source.Samples[0])).Ready,"extent-qualified explicit MoTeC mapping activates");
            var spike=Source();spike.Samples[65].Steering=.07;spike.Samples[65].Throttle=.8;
            Check(Coach(spike).PreparedZoneCount==2,"single lift/steer sample never creates a spurious corner");
            var longBend=Source();
            foreach(var s in longBend.Samples){s.Brake=s.DistanceM>=300&&s.DistanceM<400?.7:0;s.Throttle=s.DistanceM>=300&&s.DistanceM<500?.3:1;s.Steering=s.DistanceM>=300&&s.DistanceM<800?.1:0;}
            coach=Coach(longBend);events=Drive(coach,longBend);
            Check(coach.PreparedZoneCount==1&&events.Count==1,"low-steer long sweeper ends after relative unwind, not merely below a fixed .12 input");
            Check(events[0].EndDistanceM>=800,"full-throttle while most reference steering remains is not an exit anchor");
            var line=Source();line.Game="ACC";line.DistanceBasis=CapturedLap.AccDistanceBasis;line.CoordinateFrame=CapturedLap.AccCoordinateFrame;
            foreach(var s in line.Samples){s.X=s.DistanceM;s.Z=0;}
            coach=Coach(line);events=Drive(coach,line,f=>f.Z+=2,game:"ACC");
            Check(events.Count==2&&events.All(e=>Math.Abs(e.MeanAbsoluteLineOffsetM-2)<.000001),"native ACC course position and world coordinates observe a two-metre offset");
            Check(events.All(e=>e.LineObservation.Contains("평균 2.0")&&e.Observation.Contains(e.LineObservation)),"line evidence reaches the existing overlay observation field");
            Check(events.All(e=>e.NextAction.Contains("재현")&&e.Estimate.Contains("시간 손실 원인으로 단정하지")),"line difference alone never prescribes steering or claims lost time");
            Check(coach.ExportJson().Contains("meanAbsoluteLineOffsetM\":2"),"delivered line evidence is exported");
            events=Drive(Coach(line),line,game:"ACC");Check(events.All(e=>e.MeanAbsoluteLineOffsetM==0),"identical native lines report zero offset");
            events=Drive(Coach(line),line,f=>{f.X=f.Z=double.NaN;},game:"ACC");Check(events.All(e=>double.IsNaN(e.MeanAbsoluteLineOffsetM)&&e.LineObservation==""),"missing own coordinates cannot become a line");
            events=Drive(Coach(line),line,f=>{if(f.LapDistance>=300&&f.LapDistance<460)f.X=f.Z=double.NaN;},game:"ACC");
            Check(events.Count==2&&double.IsNaN(events[0].MeanAbsoluteLineOffsetM)&&events[1].MeanAbsoluteLineOffsetM==0,"low coverage suppresses only affected line observation");
            events=Drive(Coach(line),line,f=>{if(f.LapDistance==450)f.Z=60;},game:"ACC");Check(double.IsNaN(events[0].MeanAbsoluteLineOffsetM),"coordinate spike cannot create line coaching");
            events=Drive(Coach(line),line,f=>f.Z=40,game:"ACC");Check(events.All(e=>double.IsNaN(e.MeanAbsoluteLineOffsetM)),"large origin discrepancy is not labelled driver line error");
            line.CoordinateFrame=ReferenceLap.LmuCoordinateFrame;events=Drive(Coach(line),line,game:"ACC");Check(events.Count==2&&events.All(e=>double.IsNaN(e.MeanAbsoluteLineOffsetM)),"unmatched coordinate frames retain timing but suppress line comparison");
            line.CoordinateFrame="unknown";events=Drive(Coach(line),line,game:"ACC");Check(events.All(e=>e.LineObservation==""),"unknown source coordinate frame never enables line evidence");
            line.CoordinateFrame=CapturedLap.AccCoordinateFrame;
            events=Drive(Coach(line),line,f=>f.DistanceAgeSeconds=double.NaN,game:"ACC");Check(events.Count==0,"ACC unknown source age needs explicit observed graphics freshness");
            events=Drive(Coach(source),source,f=>{f.DistanceAgeSeconds=double.NaN;f.DistanceObservationAgeSeconds=0;f.DistanceObservationIntervalSeconds=.2;});
            Check(events.Count==0,"ACC observation fallback does not relax missing LMU source-age checks");
            events=Drive(Coach(line),line,f=>{f.DistanceAgeSeconds=double.NaN;f.DistanceObservationAgeSeconds=.6;f.DistanceObservationIntervalSeconds=.2;},game:"ACC");Check(events.Count==0,"stale observed ACC graphics cannot coach");
            events=Drive(Coach(line),line,f=>{f.DistanceAgeSeconds=double.NaN;f.DistanceObservationAgeSeconds=0;f.DistanceObservationIntervalSeconds=.6;},game:"ACC");Check(events.Count==0,"slow observed ACC graphics cadence cannot coach");
            events=Drive(Coach(line),line,f=>{f.DistanceAgeSeconds=double.PositiveInfinity;f.DistanceObservationAgeSeconds=0;f.DistanceObservationIntervalSeconds=.2;},game:"ACC");Check(events.Count==0,"invalid source age is not replaced by observed timing");
            events=Drive(Coach(line),line,f=>{f.DistanceAgeSeconds=double.NaN;f.DistanceObservationAgeSeconds=0;f.DistanceObservationIntervalSeconds=.2;f.Time=100;},game:"ACC");Check(events.Count==0,"advancing positions without advancing game lap clock cannot coach");
            var nativeBytes=AssettoReaderTests.Fixture(AssettoGame.Acc);TelemetryFrame native;string nativeStatus;
            if(!AssettoReader.TryDecode(AssettoGame.Acc,nativeBytes[0],nativeBytes[1],nativeBytes[2],1200,out native,out nativeStatus))throw new Exception(nativeStatus);
            line.Car=native.Car;line.Track=native.Track;line.GameVersion=native.GameVersion;
            coach=Coach(line);events=DriveAccNative(coach,line,true);
            Check(events.Count==2,"decoded ACC native frames with unknown source age and advancing observed graphics deliver complete-corner feedback");
            Check(events.All(e=>e.UsedObservedGraphicsTiming&&e.Estimate.Contains("동기 오차는 미확인")&&e.Estimate.Contains("오차 상한이 아닙니다")),"observed timing explains unresolved source synchronization rather than asserting zero latency");
            Check(events.All(e=>Math.Abs(e.TimeLossSeconds)<.00001&&Math.Abs(e.MeanAbsoluteLineOffsetM-2)<.00001),"ACC native distance plus coordinates reaches corner timing and line observation");
            Check(coach.ExportJson().Contains("usedObservedGraphicsTiming\":true"),"coaching export preserves observed-graphics timing qualification");
            events=DriveAccNative(Coach(line),line,false);Check(events.Count==0,"unchanged native graphics packet cannot activate ACC coaching despite changing fixture time");
            CheckCornerReview();
            return checks;
        }
        static LiveReferenceView[] Reviews(ReferenceLap reference,ReferenceLap own)
        {
            return LiveReferenceCoach.ReviewLap(reference,own,ReferenceComparison.Compare(reference,own));
        }
        static void CheckCornerReview()
        {
            var reference=Source();
            reference.CoordinateFrame=ReferenceLap.LmuCoordinateFrame;
            foreach(var p in reference.Samples){p.X=p.DistanceM;p.Z=0;}
            var own=ReferenceLap.LoadJson(reference.ToJson());
            foreach(var p in own.Samples)
            {
                if(p.DistanceM>=300 && p.DistanceM<340)p.Steering=0;
                p.Z=Math.Max(0,Math.Min(3,(p.DistanceM-415)*.1));
                p.Time+=Math.Max(0,Math.Min(1.5,(p.DistanceM-420)/60));
            }
            var result=ReferenceComparison.Compare(reference,own);
            var reviews=LiveReferenceCoach.ReviewLap(reference,own,result);
            Check(reviews.Length==2 && reviews[0].LineDivergenceM==435 && reviews[0].EvidenceKind=="steer-onset-before-line","stored corner locates sustained line divergence with earlier steering event");
            Check(reviews[0].NextAction.Contains("첫 조향") && reviews[0].Estimate.Contains("340 m") && reviews[0].TimeLossSeconds>1,"recorded steering landmarks, qualifying time loss and one practice action");
            var live=Drive(Coach(reference),own);
            Check(live.Count==2 && live[0].LineDivergenceM==reviews[0].LineDivergenceM && live[0].EvidenceKind==reviews[0].EvidenceKind,"same zone yields the same diagnosis in the overlay and file review");
            Check(double.IsNaN(reviews[1].LineDivergenceM),"continuously displaced line has no fictitious divergence point");
            var faster=ReferenceLap.LoadJson(own.ToJson());
            for(int i=0;i<faster.Samples.Length;i++)faster.Samples[i].Time=reference.Samples[i].Time*.85;
            var fast=Reviews(reference,faster)[0];
            Check(fast.TimeLossSeconds<0 && fast.EvidenceKind=="steer-onset-before-line" && fast.NextAction.Contains("재현"),"quicker alternative line is described without a corrective prescription");
            var lateInput=ReferenceLap.LoadJson(own.ToJson());
            foreach(var p in lateInput.Samples){p.Time=reference.Samples[(int)(p.DistanceM/10)].Time;p.Z=Math.Max(0,Math.Min(3,(p.DistanceM-292)*.1));}
            var later=Reviews(reference,lateInput)[0];
            Check(later.EvidenceKind=="line-onset" && later.Observation.Contains("구분하기 어렵"),"an input after the line change is not presented as its precursor");
            var entryGap=ReferenceLap.LoadJson(own.ToJson());entryGap.Samples[20].SpeedKph+=18;
            Check(Reviews(reference,entryGap)[0].NextAction.Contains("같은 진입 속도"),"earlier input is not a comparable cause at materially different approach speed");
            var spike=ReferenceLap.LoadJson(reference.ToJson());spike.Samples[43].Z=4;
            Check(double.IsNaN(Reviews(reference,spike)[0].LineDivergenceM),"one isolated position excursion cannot mark a sustained divergence");
            var incompatible=ReferenceLap.LoadJson(own.ToJson());incompatible.GameVersion="different";
            Check(Reviews(reference,incompatible).Length==0 && LiveReferenceCoach.ReviewLap(reference,incompatible,result).Length==0,"incompatible pair or a reused result cannot be reviewed");
            var gap=ReferenceLap.LoadJson(own.ToJson());gap.Samples=gap.Samples.Where(p=>p.DistanceM<340||p.DistanceM>470).ToArray();
            Check(Reviews(reference,gap).Length==1 && Reviews(reference,gap)[0].CornerName.EndsWith("2"),"long gap excludes only the affected complete corner");
            var spatial=ReferenceLap.LoadJson(reference.ToJson());
            spatial.TimeBasis=ReferenceLap.DistanceOnlyTimeBasis;foreach(var p in spatial.Samples)p.Time=double.NaN;
            var untimed=Reviews(spatial,own)[0];
            Check(double.IsNaN(untimed.TimeLossSeconds) && untimed.EvidenceKind=="steer-onset-before-line" && untimed.Estimate.Contains("초 단위"),"position-only source has a measured input explanation without invented time");
            var noWorld=ReferenceLap.LoadJson(reference.ToJson());var other=ReferenceLap.LoadJson(own.ToJson());
            noWorld.LateralPositionBasis=other.LateralPositionBasis=ReferenceLap.LmuLateralBasis;
            foreach(var p in noWorld.Samples){p.PathLateralM=0;p.X=p.Z=double.NaN;}
            foreach(var p in other.Samples){p.PathLateralM=p.Z;p.X=p.Z=double.NaN;}
            var channel=Reviews(noWorld,other)[0];
            Check(channel.LineDivergenceM==435 && channel.LineObservation.Contains("게임 횡위치") && double.IsNaN(channel.MeanAbsoluteLineOffsetM),"matching measured lateral channels are explicitly labeled separately from map coordinates");
            noWorld.LateralPositionBasis="unknown";
            Check(double.IsNaN(Reviews(noWorld,other)[0].LineDivergenceM),"missing channel basis does not synthesize a line");
            var gasSource=ReferenceLap.LoadJson(reference.ToJson());var gasOwn=ReferenceLap.LoadJson(own.ToJson());
            for(int i=0;i<gasOwn.Samples.Length;i++)
            {
                var r=gasSource.Samples[i];var p=gasOwn.Samples[i];
                p.Steering=r.Steering;
                if(r.DistanceM>=400 && r.DistanceM<500)r.Throttle=1;
                if(p.DistanceM>=355 && p.DistanceM<500)p.Throttle=1;
                p.Z=Math.Max(0,Math.Min(3,(p.DistanceM-445)*.1));
            }
            var gas=Reviews(gasSource,gasOwn)[0];
            Check(gas.LineDivergenceM==465 && gas.EvidenceKind=="throttle-return-before-line" && gas.NextAction.Contains("가속"),"one confirmed throttle return before a sustained path divergence produces a controlled pickup drill");
            foreach(var p in gasOwn.Samples.Where(p=>p.DistanceM>=410 && p.DistanceM<=430))p.Throttle=.3;
            Check(Reviews(gasSource,gasOwn)[0].EvidenceKind=="line-onset","multiple observed pickups in a complex do not imply aligned throttle landmarks");
            var both=ReferenceLap.LoadJson(own.ToJson());
            foreach(var p in both.Samples.Where(p=>p.DistanceM>=300 && p.DistanceM<340))p.Brake=0;
            Check(Reviews(reference,both)[0].EvidenceKind=="overlapping-inputs-before-line","two overlapping antecedent changes are disclosed together");
            var onlyBrake=ReferenceLap.LoadJson(both.ToJson());
            for(int i=0;i<onlyBrake.Samples.Length;i++)
            {
                var p=onlyBrake.Samples[i];p.Steering=reference.Samples[i].Steering;p.Z=0;
                if(p.DistanceM>=340 && p.DistanceM<=360)p.SpeedKph+=12;
            }
            Check(Reviews(reference,onlyBrake)[0].EvidenceKind=="brake-start-speed","resolved brake start and corresponding speed remain actionable without a line change");
        }
        // Optional local probe: replay the downloaded source as an artificial driver.
        // The actual author's samples are never embedded in this test or repository.
        public static int RunPrivate(string path) {return RunPrivate(path,5793);}
        public static int RunPrivate(string path,double syntheticNativeLength)
        {
            checks=0;var data=ReferenceLap.LoadJson(File.ReadAllText(path));var coach=Coach(data,true);
            Check(coach.PreparedZoneCount>=4,"actual file contains multiple usable braking/lift complexes");
            var events=new List<LiveReferenceView>();
            foreach(var s in data.Samples)
            {
                var f=new TelemetryFrame {Time=100+s.Time,LapDistance=s.DistanceM,TrackLength=syntheticNativeLength,DistanceAgeSeconds=.1,Lap=1,Car=data.Car,Track=data.Track,GameVersion="unknown",OnTrack=true,ValidLapKnown=true,IsValidLap=true,SpeedKph=s.SpeedKph,Brake=s.Brake,Throttle=s.Throttle,Steering=s.Steering};
                var view=coach.Update("LMU",f);if(view.NewFeedback)events.Add(view);
            }
            Check(events.Count==coach.PreparedZoneCount,"actual-file artificial mirror delivers once for every observed complete complex");
            Check(events.All(v=>Math.Abs(v.TimeLossSeconds)<.000001),"actual source self-mirror produces no invented loss");
            Check(events.All(v=>v.Estimate.Contains("실제 주행 검증 전")),"actual-source mapping limitation remains in every delivered event");
            Console.WriteLine("Actual source replay: "+coach.PreparedZoneCount+" complexes, "+events.Count+" events (synthetic mirror only)");
            return checks;
        }
    }
}
