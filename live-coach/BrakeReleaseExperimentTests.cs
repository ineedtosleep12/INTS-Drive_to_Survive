using System;
using System.Linq;

namespace IntsLiveCoach
{
    public static class BrakeReleaseExperimentTests
    {
        static int checks;
        static void Check(bool value,string name) {checks++;if(!value) throw new Exception("Brake release experiment: "+name);}
        static bool Near(double? value,double expected) {return value.HasValue && Math.Abs(value.Value-expected)<.00001;}
        static BrakeReleaseExperiment Arm(double start=100.5,double end=340.5)
        {
            var e=new BrakeReleaseExperiment();e.Arm("시케인",start,end);return e;
        }
        static double Pedal(double d,double width)
        {
            if(d<120 || d>=160+width) return 0;
            if(d<130) return (d-120)/10;
            return d<160?1:1-(d-160)/width;
        }
        static TelemetryFrame Frame(int lap,double d,double width=60)
        {
            return new TelemetryFrame { Time=lap*100+d/40,Lap=lap,Track="Synthetic track",Car="Synthetic GT3",GameVersion="9",
                TrackLength=1000,LapDistance=d,DistanceAgeSeconds=.05,OnTrack=true,ValidLapKnown=true,IsValidLap=true,
                SpeedKph=144+d/1000,Brake=Pedal(d,width),FuelLitres=40 };
        }
        static int RunPass(BrakeReleaseExperiment e,int lap,double width=60,Action<TelemetryFrame> edit=null,int start=0,int end=400)
        {
            int events=0;
            for(int d=start;d<=end;d+=2)
            {
                var f=Frame(lap,d,width);if(edit!=null) edit(f);if(e.Update(f)) events++;
            }
            return events;
        }
        static ReferenceLap Captured()
        {
            var lap=new ReferenceLap {Game="LMU",Track="Synthetic track",Car="Synthetic GT3",GameVersion="9",
                TrackLengthM=1000,DistanceBasis=ReferenceLap.LmuDistanceBasis,Complete=true,Validity="valid",
                Samples=Enumerable.Range(0,501).Select(i=>new ReferenceSample {DistanceM=2*i,Time=i/20.0,
                    SpeedKph=144+2*i/1000.0,Brake=Pedal(2*i,60)}).ToArray()};
            lap.Conditions["sourceReader"]="LMU live shared-memory reader";
            lap.Conditions["sourceLapNumber"]="1";lap.Conditions["sourceTrackLabel"]=lap.Track;lap.Conditions["sourceCarLabel"]=lap.Car;
            return lap;
        }
        public static int Run()
        {
            checks=0;
            var e=Arm();
            Check(RunPass(e,1)==1 && e.Latest.Lap==1 && e.Previous==null,"first crossing observed once");
            var first=e.Latest;
            Check(Near(first.BrakeStartM,121.5) && Near(first.ReleaseSeconds,.75) && Near(first.ReleaseRatePctPerSecond,50/.75),"interpolated pedal crossings");
            Check(Near(first.SegmentSeconds,6) && Near(first.EntrySpeedKph,144.1005) && Near(first.ExitSpeedKph,144.3405),"fixed positions including interpolation");
            Check(RunPass(e,2,100,f=>{f.Time+=f.LapDistance*.001;f.SpeedKph+=f.LapDistance/100;})==1,"new full pass");
            Check(e.Previous==first && Near(e.Latest.ReleaseSeconds,1.30) && Near(e.Latest.SegmentSeconds-first.SegmentSeconds,.24) &&
                Near(e.Latest.ExitSpeedKph-first.ExitSpeedKph,3.405),"independent measured release, fixed segment duration and speed deltas");
            Check(e.ResultText.Contains("+3.4") && e.ResultText.Contains("+0.24") && e.ComparisonNote.Contains("길게"),"signed prior-pass outcome");
            RunPass(e,3,60,f=>f.SpeedKph-=9);Check(e.Previous.Lap==2 && e.ComparisonNote.Contains("진입 속도"),"entry speed flagged");
            RunPass(e,4,60,f=>{f.SpeedKph-=9;f.Brake=Pedal(f.LapDistance-35,60);});
            Check(e.ComparisonNote.Contains("제동 시작점"),"different onset flagged");
            RunPass(e,5,60,f=>{f.SpeedKph-=9;f.Brake=.75*Pedal(f.LapDistance-35,60);});
            Check(e.ComparisonNote.Contains("최대 제동"),"changed peak cannot support single-change experiment");
            e.Stop();Check(e.Count==5 && !e.Armed && !e.Update(Frame(6,102)),"stop preserves results");
            e.Arm("다른 구간",99.5,339.5);Check(e.Count==0 && e.Recent.Length==0,"rearm clears mixed corners");
            RunPass(e,6,60,null,130);Check(e.Count==0,"joining mid-corner does not form pass");
            RunPass(e,7,60,f=>{if(f.LapDistance==220) f.IsValidLap=false;});
            Check(e.Count==0,"invalid lap cannot resume inside zone");
            RunPass(e,8,60,null,0,190);e.Interrupt();RunPass(e,8,60,null,192,400);
            Check(e.Count==0,"no stitching after disconnect");
            RunPass(e,9,60,f=>{if(f.LapDistance==196)f.DistanceAgeSeconds=.55;});
            Check(e.Count==0,"stale scoring distance suppresses current pass");
            RunPass(e,10,60,null,0,170);RunPass(e,10,60,null,220,400);Check(e.Count==0,"gap at release");
            RunPass(e,11,60,f=>f.Car="Other");Check(e.Count==0,"another car never added");
            RunPass(e,11);Check(e.Count==1,"restored context accepts a fresh whole pass");
            RunPass(e,12,60,f=>{if(f.LapDistance>135&&f.LapDistance<148)f.Brake=Double.NaN;});
            Check(e.Count==2 && !e.Latest.ReleaseSeconds.HasValue && Near(e.Latest.SegmentSeconds,6) && e.ComparisonNote.Contains("보류"),"missing pedal does not fabricate slope or remove outcomes");
            RunPass(e,13,60,f=>{double d=f.LapDistance;f.Brake=d<220?.45*Pedal(d,40):Pedal(d-100,50);});
            Check(!e.Latest.ReleaseSeconds.HasValue && e.Latest.BrakeStartM.Value<140,"shallow first application cannot borrow a second chicane braking action");
            RunPass(e,14,60,f=>{double d=f.LapDistance;f.Brake=d<240?Pedal(d,60):Pedal(d-120,12);});
            Check(Near(e.Latest.ReleaseSeconds,.75),"second application cannot replace a measured first release");
            RunPass(e,15,60,f=>{double d=f.LapDistance;if(d>=202 && d<=214)f.Brake=.77;});
            Check(!e.Latest.ReleaseSeconds.HasValue && e.Latest.ReleaseNote.Contains("재제동"),"material reapplication during release");
            RunPass(e,16,60,f=>{if(f.LapDistance==168)f.Brake=.02;});
            Check(!e.Latest.ReleaseSeconds.HasValue || e.Latest.ReleaseSeconds>.58,"one noisy frame cannot end the release early");
            var chatter=Arm();RunPass(chatter,1,60,f=>f.Brake=f.LapDistance==148?.55:.70*f.Brake);
            Check(Near(chatter.Latest.ReleaseSeconds, .5/.70*60/40),"single shallow dip across 60 percent cannot start the release early");
            RunPass(e,17,60,f=>{double d=f.LapDistance;if(d>159)f.Brake=d<162?1:0;});
            Check(!e.Latest.ReleaseSeconds.HasValue && e.Latest.ReleaseNote.Contains("표본"),"digital jump below sampling resolution");
            for(int i=18;i<=34;i++)RunPass(e,i);
            Check(e.Recent.Length==12 && e.Previous.Lap==33 && e.Recent[0].Lap==23 && e.Recent[11].AttemptNumber==e.Count,"bounded chronological history");
            var array=e.Recent;array[0]=null;Check(e.Recent[0]!=null,"snapshots cannot replace list entries");
            Check(e.ExportJson().Contains("valid-at-segment-exit") && !e.ExportJson().Contains("NaN"),"portable export");
            RunPass(e,1);Check(e.Count==1 && e.Previous==null,"fresh native session cannot compare across restarts");

            var stairs=Arm();
            RunPass(stairs,1,60,f=>{f.LapDistance=8*Math.Floor(f.LapDistance/8);});
            Check(stairs.Count==1 && Near(stairs.Latest.ReleaseSeconds,.75),"5 Hz scoring staircase preserves physics pedal timing");
            RunPass(stairs,2,60,f=>{double d=f.LapDistance;if(d>170)f.LapDistance=d<202?170:d-32;});
            Check(stairs.Count==1,"frozen position at speed cannot stay fresh indefinitely by repeating timestamps");
            RunPass(stairs,3,60,f=>{double d=f.LapDistance;if(d>170 && d<=202){f.LapDistance=170;f.SpeedKph=0;}else if(d>202)f.LapDistance=d-32;});
            Check(stairs.Count==2,"standing still is timed as part of the attempt");
            var seeded=Arm();var cap=Captured();
            Check(seeded.SeedFromLap(cap) && seeded.Count==1 && Near(seeded.Latest.ReleaseSeconds,first.ReleaseSeconds.Value) &&
                Near(seeded.Latest.SegmentSeconds,first.SegmentSeconds),"seed uses live gate and pedal algorithm");
            RunPass(seeded,1);Check(seeded.Count==1,"do not re-count the seeded lap number");
            RunPass(seeded,2);Check(seeded.Count==2 && seeded.Previous.Lap==1,"next actual pass compared to calibration");
            Check(!seeded.Previous.FuelLitres.HasValue && seeded.Previous.TimeResolutionSeconds>first.TimeResolutionSeconds+.8,"missing captured fuel and age remain unknown or conservative");
            Check(!seeded.SeedFromLap(cap),"cannot replace history with seed");
            var fresh=Arm();foreach(var s in cap.Samples){s.CapturedDistanceAgeSeconds=.05;s.CapturedFuelLitres=40;}
            Check(fresh.SeedFromLap(cap) && Near(fresh.Latest.FuelLitres,40) && Near(fresh.Latest.TimeResolutionSeconds,first.TimeResolutionSeconds) &&
                Near(fresh.Latest.BrakeStartResolutionM,first.BrakeStartResolutionM),"fresh capture has matching gate precision and measured fuel");
            RunPass(fresh,2,60,f=>f.FuelLitres=34);Check(fresh.ComparisonNote.Contains("연료"),"compare known fuel from capture and current attempt");
            seeded=Arm();cap.Conditions["sourceCarLabel"]="original native";Check(!seeded.SeedFromLap(cap),"mapped car denied");
            cap=Captured();cap.Complete=false;Check(!seeded.SeedFromLap(cap),"partial calibration denied");
            cap=Captured();cap.Validity="unknown";Check(!seeded.SeedFromLap(cap),"unknown calibration validity");
            cap=Captured();cap.Conditions.Clear();Check(!seeded.SeedFromLap(cap),"unverified source denied");
            var wrap=Arm(970,1000);
            RunPass(wrap,1,60,null,950,998);RunPass(wrap,2,60,f=>f.Time-=75,0,20);
            Check(wrap.Count==1 && wrap.Latest.Lap==1 && Near(wrap.Latest.SegmentSeconds,.75),"exit gate at finish line across native lap change");
            var inputs=Arm();
            RunPass(inputs,1,60,f=>{f.Steering=f.LapDistance>160?.3:0;f.Throttle=f.LapDistance>220?1:0;});
            RunPass(inputs,2,60,f=>{f.Steering=f.LapDistance>192?.3:0;f.Throttle=f.LapDistance>260?1:0;});
            Check(inputs.ComparisonNote.Contains("첫 조향") && inputs.ComparisonNote.Contains("가속 복귀"),"measured steering and power differences flagged");
            return checks;
        }
    }
}
