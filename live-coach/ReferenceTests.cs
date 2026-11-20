using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    // Artificial evidence solely for unit checks; never shipped as a hotlap.
    public static class ReferenceTests
    {
        static int assertions;
        static void Check(bool okay,string label) {assertions++;if(!okay)throw new Exception("Reference test: "+label);}
        static void Near(double actual,double wanted,string label) {Check(Math.Abs(actual-wanted)<.00001,label);}
        static void Reject(Action action,string label)
        {bool rejected=false;try{action();}catch(FormatException){rejected=true;}Check(rejected,label);}
        static ReferenceLap Fixture(double timeScale=1,double offset=0,int brakeShift=0)
        {
            var lap=new ReferenceLap {Game="SyntheticSim",Car="fixture-car",Track="fixture-track",Layout="fixture-layout",GameVersion="fixture-v1",
                TrackLengthM=1000,DistanceBasis="synthetic-course-distance-m",CoordinateFrame="synthetic-world-xz-m",SourceUrl="local:synthetic-test-only",Author="synthetic fixture",RecordedAt="unknown",
                Licence="test-only",Provenance="ARTIFICIAL unit-test trace, never a driving reference",Validity="valid",Complete=true};
            lap.Conditions["fuelLitres"]="30";lap.Conditions["tyres"]="fixture-dry";
            lap.Samples=Enumerable.Range(0,201).Select(i=>new ReferenceSample {Time=i*.1*timeScale,DistanceM=i*5,SpeedKph=180/timeScale,
                Brake=i*5>=200+brakeShift&&i*5<350+brakeShift?.8:0,Throttle=i*5>=200+brakeShift&&i*5<350+brakeShift?0:.9,Steering=.1,X=i*5,Z=offset}).ToArray();
            return lap;
        }
        public static int Run()
        {
            assertions=0;var reference=Fixture();var own=Fixture(1.1,2,20);
            var restored=ReferenceLap.LoadJson(reference.ToJson());
            Check(restored.Samples.Length==reference.Samples.Length,"canonical roundtrip preserves actual sample count");
            Check(restored.SourceUrl==reference.SourceUrl&&restored.Provenance==reference.Provenance,"provenance survives serialization");
            Check(restored.Conditions["fuelLitres"]=="30","conditions survive serialization");
            var missing=Fixture();missing.Samples[0].X=missing.Samples[0].Z=double.NaN;
            string json=missing.ToJson();Check(!json.Contains("NaN")&&json.Contains("\"x\":null"),"missing channels are JSON null");
            Check(double.IsNaN(ReferenceLap.LoadJson(json).Samples[0].X),"missing position never fabricated as zero");
            var degrees=Fixture();degrees.Samples[0].Steering=double.NaN;degrees.Samples[0].SteeringDegrees=-32.5;
            var preserved=ReferenceLap.LoadJson(degrees.ToJson());
            Check(preserved.Samples[0].SteeringDegrees==-32.5&&double.IsNaN(preserved.Samples[0].Steering),"raw source degrees survive without invented normalized steering");
            var result=ReferenceComparison.Compare(reference,own);
            Check(result.Comparable&&result.HasLineComparison,"same source basis and coordinates compare");
            Near(result.LapDeltaSeconds,2,"full-lap elapsed delta from exact boundaries");
            Near(result.Points[100].DeltaSeconds,1,"mid-lap delta uses same distance, not same timestamp");
            Near(result.Points[100].LineOffsetM,2,"lateral difference projected on reference normal");
            Check(result.Sections.Length==10,"nonoverlapping fixed distance sections");
            Near(result.Sections.Sum(s=>s.TimeLossSeconds),2,"section losses telescope instead of summing cumulative delta");
            Check(result.Sections.All(s=>s.EndDistanceM-s.StartDistanceM<=100.0001),"sections have explicit physical boundaries");
            Near(result.Sections[0].MeanLineOffsetM,2,"line difference summary uses absolute lateral offset");
            Check(result.BrakeZones.Length==1,"one sustained brake zone matched");
            Near(result.BrakeZones[0].StartDeltaM,20,"positive onset delta means later on course");
            Near(result.BrakeZones[0].ReleaseDeltaM,20,"release changes compared at same course basis");
            Check(result.BrakeZones[0].EstimatedUncertaintyM>=10,"brake sampling uncertainty is exposed");
            var tiny=ReferenceComparison.Compare(reference,Fixture(1,0,5));
            Check(tiny.BrakeZones.Length==1&&!tiny.BrakeZones[0].StartDifferenceResolved,"small brake delta is not an exact correction cue");
            var quicker=ReferenceComparison.Compare(reference,Fixture(.9));Near(quicker.LapDeltaSeconds,-2,"negative delta means own run faster");
            var different=Fixture();different.Game="DifferentSim";Check(!ReferenceComparison.Compare(reference,different).Comparable,"game mismatch held");
            different=Fixture();different.Car="other-car";Check(!ReferenceComparison.Compare(reference,different).Comparable,"car mismatch held");
            different=Fixture();different.Track="other-track";Check(!ReferenceComparison.Compare(reference,different).Comparable,"track mismatch held");
            var unidentified=Fixture();unidentified.Car="unknown";different=Fixture();different.Car="unknown";
            Check(!ReferenceComparison.Compare(unidentified,different).Comparable,"matching unknown vehicle strings do not establish identity");
            different=Fixture();different.Layout="short";Check(!ReferenceComparison.Compare(reference,different).Comparable,"layout mismatch held");
            different=Fixture();different.GameVersion="fixture-v2";Check(!ReferenceComparison.Compare(reference,different).Comparable,"known build mismatch held by default");
            result=ReferenceComparison.Compare(reference,different,true);Check(result.Comparable&&result.Warnings.Any(w=>w.Contains("버전이 다른")),"explicit version override remains labelled exploratory");
            different=Fixture();different.GameVersion="unknown";result=ReferenceComparison.Compare(reference,different);Check(result.Comparable&&result.Warnings.Any(w=>w.Contains("버전이 미확인")),"unknown version produces visible qualification");
            different=Fixture();different.Validity="invalid";Check(!ReferenceComparison.Compare(reference,different).Comparable,"invalid lap never earns performance comparison");
            different=Fixture();different.Validity="unknown";result=ReferenceComparison.Compare(reference,different);Check(result.Comparable&&result.Warnings.Any(w=>w.Contains("유효성 미확인")),"unknown validity not falsely certified");
            different=Fixture();different.Conditions["fuelLitres"]="80";Check(ReferenceComparison.Compare(reference,different).Warnings.Any(w=>w.Contains("80")),"known conditions difference retained");
            different=Fixture();different.Conditions.Clear();Check(ReferenceComparison.Compare(reference,different).Warnings.Any(w=>w.Contains("조건 미확인")),"missing conditions retained");
            different=Fixture();different.DistanceBasis="distance-travelled-m";Check(!ReferenceComparison.Compare(reference,different).Comparable,"travelled distance cannot masquerade as course progress");
            var travelled=Fixture();travelled.DistanceBasis="distance-travelled-m";Check(!ReferenceComparison.Compare(travelled,different).Comparable,"matching integrated path-distance labels still not comparable");
            different=Fixture();different.TrackLengthM=1020;different.Complete=false;Check(!ReferenceComparison.Compare(reference,different).Comparable,"track length mismatch held");
            different=Fixture();different.CoordinateFrame="unrelated-world";result=ReferenceComparison.Compare(reference,different);Check(result.Comparable&&!result.HasLineComparison&&result.Points.All(p=>double.IsNaN(p.LineOffsetM)),"time remains available while unknown line frame abstains");
            different=Fixture(1,200);result=ReferenceComparison.Compare(reference,different);Check(!result.HasLineComparison,"large origin displacement is not silently fitted into a racing line");
            different=Fixture();foreach(var s in different.Samples)s.X=s.Z=double.NaN;Check(!ReferenceComparison.Compare(reference,different).HasLineComparison,"missing line coordinates abstain");
            different=Fixture();different.Complete=false;different.Samples=different.Samples.Where(s=>s.DistanceM>=200&&s.DistanceM<=600).ToArray();
            result=ReferenceComparison.Compare(reference,different);Check(result.Comparable&&double.IsNaN(result.LapDeltaSeconds),"partial traces compare only overlap and cannot claim lap delta");
            Near(result.Points.First().DistanceM,200,"overlap starts at actual shared sample");Near(result.Points.Last().DistanceM,600,"overlap ends at actual shared sample");
            different=Fixture();different.Samples=different.Samples.Where(s=>s.DistanceM<300||s.DistanceM>400).ToArray();result=ReferenceComparison.Compare(reference,different);
            Check(result.Comparable&&result.Warnings.Any(w=>w.Contains("공백")),"long sampling gap explicitly disclosed");
            Check(result.Sections.All(s=>s.EndDistanceM<300||s.StartDistanceM>400),"sections touching large data gaps are excluded");
            Check(ReferenceComparison.SampleAtDistance(different,350)==null,"missing time segment not interpolated");
            Check(ReferenceComparison.SampleAtDistance(reference,-1)==null&&ReferenceComparison.SampleAtDistance(reference,1001)==null,"no start or finish extrapolation");
            Near(ReferenceComparison.SampleAtDistance(reference,2.5).Time,.05,"distance interpolation exact on uniform fixture");
            different=Fixture();different.Samples[1].Time=0;Reject(()=>different.Validate(),"contradictory duplicate times rejected");
            different=Fixture();different.Samples[10].DistanceM=1;Reject(()=>different.Validate(),"backwards course distance rejected");
            different=Fixture();different.Samples[10].SpeedKph=double.PositiveInfinity;Reject(()=>different.Validate(),"infinite optional channel rejected");
            different=Fixture();different.Samples[10].Brake=2;Reject(()=>different.Validate(),"wrong brake units rejected");
            different=Fixture();different.Samples[10].Z=double.NaN;Reject(()=>different.Validate(),"partial coordinate tuple rejected");
            different=Fixture();different.Samples=different.Samples.Skip(20).ToArray();Reject(()=>different.Validate(),"truncated start cannot declare complete lap");
            Reject(()=>ReferenceLap.LoadJson("{}"),"missing required schema rejected");
            Reject(()=>ReferenceLap.LoadJson("{broken}"),"malformed JSON rejected with a format failure");
            Reject(()=>ReferenceLap.LoadJson(reference.ToJson().Replace("\"fuelLitres\":\"30\"","\"fuelLitres\":\"30\",\"FUELlitres\":\"40\"")),"case-insensitive duplicate conditions rejected");
            Reject(()=>ReferenceLap.LoadJson(reference.ToJson().Replace("\"schemaVersion\":1","\"schemaVersion\":2")),"future schema not guessed");
            var parsed=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(reference.ToJson());
            var entries=(object[])parsed["samples"];var repeated=new List<object>();repeated.Add(entries[0]);repeated.AddRange(entries);parsed["samples"]=repeated.ToArray();
            Check(ReferenceLap.LoadJson(new JavaScriptSerializer().Serialize(parsed)).Samples.Length==entries.Length,"identical duplicate frame safely deduplicated");
            var contradictory=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(reference.ToJson());
            var repeatedEntries=(object[])contradictory["samples"];((Dictionary<string,object>)repeatedEntries[1])["time"]=0;
            Reject(()=>ReferenceLap.LoadJson(new JavaScriptSerializer().Serialize(contradictory)),"different data at same timestamp rejected at load");
            Check(!ReferenceComparison.Compare(null,own).Comparable,"missing input does not invent a reference");
            var motec=Fixture();motec.Game="LMU";motec.DistanceBasis=ReferenceLap.LmuMotecDistanceBasis;motec.LateralPositionBasis=ReferenceLap.LmuMotecLateralBasis;motec.CoordinateFrame="unknown";
            var native=Fixture(1.1);native.Game="LMU";native.DistanceBasis=ReferenceLap.LmuDistanceBasis;native.LateralPositionBasis=ReferenceLap.LmuLateralBasis;native.CoordinateFrame="unknown";
            foreach(var s in motec.Samples){s.X=s.Z=double.NaN;s.PathLateralM=4;}
            foreach(var s in native.Samples){s.X=s.Z=double.NaN;s.PathLateralM=6;}
            var lateralRoundtrip=ReferenceLap.LoadJson(motec.ToJson());
            Check(lateralRoundtrip.LateralPositionBasis==ReferenceLap.LmuMotecLateralBasis,"lateral channel provenance survives serialization");
            Near(lateralRoundtrip.Samples[10].PathLateralM,4,"recorded lateral values survive serialization");
            Check(!ReferenceComparison.Compare(motec,native).Comparable,"cross logger mapping is disabled by default");
            result=ReferenceComparison.Compare(motec,native,false,true);
            Check(result.Comparable&&result.HasLateralComparison&&!result.HasLineComparison,"explicit mapping compares channel lateral values without inventing a world map");
            Check(result.Warnings.Any(w=>w.Contains("실제 주행 검증 전")),"exploration exposes unverified mapping prominently");
            Near(result.Points[100].PathLateralDeltaM,2,"lateral channel delta is own minus reference");
            Near(ReferenceComparison.SampleAtDistance(motec,2.5).PathLateralM,4,"lateral sample interpolation");
            var sameLogger=ReferenceLap.LoadJson(motec.ToJson());result=ReferenceComparison.Compare(motec,sameLogger);
            Check(result.Comparable&&result.HasLateralComparison,"same logger distance and lateral basis compare without cross-channel override");
            native.Game="OtherGame";Check(!ReferenceComparison.Compare(motec,native,true,true).Comparable,"mapping override never bypasses game identity");native.Game="LMU";
            var noLateral=ReferenceLap.LoadJson(native.ToJson());foreach(var s in noLateral.Samples)s.PathLateralM=double.NaN;
            result=ReferenceComparison.Compare(motec,noLateral,false,true);
            Check(result.Comparable&&!result.HasLateralComparison&&result.Points.All(p=>double.IsNaN(p.PathLateralDeltaM)),"missing lateral channel does not fabricate zero offset");
            var partialMotec=ReferenceLap.LoadJson(motec.ToJson());partialMotec.TrackLengthM=double.NaN;partialMotec.Complete=false;
            partialMotec.Samples=partialMotec.Samples.Where(s=>s.DistanceM>=5&&s.DistanceM<=995).ToArray();
            result=ReferenceComparison.Compare(partialMotec,native,false,true);
            Check(result.Comparable&&double.IsNaN(result.LapDeltaSeconds)&&double.IsNaN(partialMotec.TrackLengthM),"near-complete unknown-length source enables only explicit partial exploration without invented length");
            Check(result.Warnings.Any(w=>w.Contains("전체 트랙 길이는 미확인")),"unknown source length remains visible");
            var tooPartial=ReferenceLap.LoadJson(partialMotec.ToJson());tooPartial.Samples=tooPartial.Samples.Where(s=>s.DistanceM>=30).ToArray();
            Check(!ReferenceComparison.Compare(tooPartial,native,false,true).Comparable,"less than 98 percent source extent cannot qualify mapping");
            var wrongLength=ReferenceLap.LoadJson(motec.ToJson());wrongLength.TrackLengthM=1020;wrongLength.Complete=false;
            Check(!ReferenceComparison.Compare(wrongLength,native,false,true).Comparable,"known mismatched lengths are not bypassed by exploratory mapping");
            return assertions;
        }
    }
}
