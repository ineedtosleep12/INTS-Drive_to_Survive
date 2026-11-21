using System;
using System.Collections.Generic;
using System.Linq;

namespace IntsLiveCoach
{
    public static class BrakePracticeCourseTests
    {
        static int checks;
        static void Check(bool value, string label)
        { checks++; if (!value) throw new Exception("Brake practice course: " + label); }

        // Two artificial, fully observed brake complexes. No creator telemetry is copied.
        static ReferenceLap Native(double shift=0)
        {
            const int length=5793;
            var samples=new List<ReferenceSample>();
            double time=0;
            for (int i=0;; i=Math.Min(length,i+4))
            {
                double s=i-shift, brake=0, speed=215, steer=0, throttle=1;
                // The brake application, first rotation, and opposite rotation occur in order.
                if (s>=690 && s<810) {brake=1;throttle=0;speed=215-(s-690)*.9;}
                if (s>=810 && s<840) {brake=(840-s)/30;throttle=0;speed=100;}
                if (s>=828 && s<865) {steer=.42;speed=85;throttle=.15;}
                if (s>=865 && s<905) {steer=-.38;speed=77;throttle=.2;}
                if (s>=905 && s<960) speed=90+(s-905)*.5;
                if (s>=2000 && s<2090) {brake=.78;speed=140;throttle=0;}
                if (s>=2080 && s<2130) {steer=-.31;speed=101;throttle=.2;}
                double theta=2*Math.PI*i/length;
                samples.Add(new ReferenceSample {DistanceM=i,Time=time,SpeedKph=speed,Brake=brake,Throttle=throttle,
                    Steering=steer,X=length/(2*Math.PI)*Math.Sin(theta),Z=length/(2*Math.PI)*Math.Cos(theta)});
                if (i==length) break;
                time+=Math.Min(4,length-i)/(speed/3.6);
            }
            return new ReferenceLap {Game="LMU",Track="Autodromo Nazionale Monza",Car="Manthey #92",GameVersion="fixture-build",
                TrackLengthM=length,DistanceBasis=ReferenceLap.LmuDistanceBasis,CoordinateFrame=ReferenceLap.LmuCoordinateFrame,
                Complete=true,Validity="valid",Samples=samples.ToArray()};
        }
        static TelemetryFrame Frame(ReferenceLap lap,double at)
        {
            var sample=ReferenceComparison.SampleAtDistance(lap,at);
            return new TelemetryFrame {Track=lap.Track,Car=lap.Car,GameVersion=lap.GameVersion,TrackLength=lap.TrackLengthM,
                LapDistance=at,X=sample.X,Z=sample.Z,OnTrack=true,ValidLapKnown=true,IsValidLap=true};
        }
        public static int Run()
        {
            checks=0;
            var lap=Native();
            Check(BrakePracticeCourse.IsIdentifiedPorsche992("Porsche 911 GT3 R (992)") &&
                !BrakePracticeCourse.IsIdentifiedPorsche992("Porsche 992") && !BrakePracticeCourse.IsIdentifiedPorsche992("Manthey #92"),
                "target model distinct from generation and livery");
            Check(!BrakePracticeCourse.IsKnownOtherCar("Manthey #92") && !BrakePracticeCourse.IsKnownOtherCar("Private team #963") &&
                BrakePracticeCourse.IsKnownOtherCar("Porsche 992 GT3 Cup") && BrakePracticeCourse.IsKnownOtherCar("Porsche 963") &&
                BrakePracticeCourse.IsKnownOtherCar("Ferrari 296 GT3") && BrakePracticeCourse.IsKnownOtherCar("DKR Engineering #4:ELMS25"),
                "known conflicting models rejected while team labels remain unconfirmed");
            var corner=BrakePracticeCourse.MonzaFirstChicane(lap);
            Check(corner!=null && corner.StartM<690 && corner.EndM>905 && corner.EndM<1050,"complete pair of turns and fixed exit");
            var later=BrakePracticeCourse.MonzaFirstChicane(Native(84));
            Check(later!=null && later.StartM==corner.StartM+84 && later.EndM==corner.EndM+84,"calibrate to observed native origin");
            var input=Frame(lap,corner.EndM);
            Check(corner.MatchesFrame(input),"same native path at exit");
            input.X+=60;
            Check(!corner.MatchesFrame(input),"sibling route with displaced actual positions");
            input=Frame(lap,corner.StartM);input.TrackLength+=.1;
            Check(!corner.MatchesFrame(input),"native scoring length exact");
            input=Frame(lap,corner.StartM);input.GameVersion="new-build";
            Check(!corner.MatchesFrame(input),"different game build");
            input=Frame(lap,corner.StartM);input.Car="other livery";
            Check(!corner.MatchesFrame(input),"different livery");
            input=Frame(lap,corner.StartM-3);input.Z=Double.NaN;
            Check(!corner.MatchesFrame(input),"gate predecessor requires native position");
            lap.Layout="Curva Grande";
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"explicit conflicting layout overrides same length and turn shape");
            lap.Layout="unknown"; lap.Track="Monza Curva Grande";
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"other Monza driving configuration");
            lap.Track="Monza WEC";lap.Car="Porsche 911 RSR";
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"known non-992 cannot bind to preset");
            lap=Native();
            foreach (var p in lap.Samples.Where(p=>p.DistanceM>810 && p.DistanceM<1050)) p.Steering=Math.Abs(p.Steering);
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"one long turn cannot stand in for a chicane");
            lap=Native(); lap.Track="Independent alternate"; lap.Car="Track day entry #22";
            var courses=BrakePracticeCourse.FromNativeLap(lap);
            Check(courses.Length==2 && courses[0].EndM<courses[1].StartM && courses[1].EndM>2130,"another native circuit offers observed complexes in order");
            foreach (var p in lap.Samples.Where(p=>p.DistanceM>1800)) p.Brake*=.5;
            Check(BrakePracticeCourse.FromNativeLap(lap).Length==1,"exclude sections without a substantial brake application");
            lap=Native();lap.Conditions["sourceCarLabel"]="Original #51";lap.Conditions["sourceTrackLabel"]="Original short";
            var mapped=BrakePracticeCourse.FromNativeLap(lap);
            Check(mapped.Length>0 && mapped[0].Car=="Original #51" && mapped[0].Track=="Original short" &&
                BrakePracticeCourse.MonzaFirstChicane(lap)==null,"raw identity wins over a mapped UI label");
            lap=Native();lap.CoordinateFrame="unknown";
            Check(BrakePracticeCourse.FromNativeLap(lap).Length==0,"unverified world positions excluded");
            lap=Native();lap.Complete=false;
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null && BrakePracticeCourse.FromNativeLap(lap).Length==0,"no partial lap");
            lap=Native();lap.DistanceBasis=ReferenceLap.LmuMotecDistanceBasis;
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null && BrakePracticeCourse.FromNativeLap(lap).Length==0,"MoTeC cannot seed native gates");
            lap=Native();lap.Validity="unknown";
            Check(BrakePracticeCourse.FromNativeLap(lap).Length==0,"unknown lap validity");
            lap=Native();lap.Samples=lap.Samples.Where(p=>p.DistanceM<750 || p.DistanceM>875).ToArray();
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"no stitched chicane across missing braking samples");
            lap=Native();foreach (var p in lap.Samples) {p.X=0;p.Z=0;}
            Check(BrakePracticeCourse.FromNativeLap(lap).Length==0 && BrakePracticeCourse.MonzaFirstChicane(lap)==null,"frozen coordinates cannot verify an interval");
            lap=Native();foreach (var p in lap.Samples.Where(p=>p.DistanceM>760)) p.Time+=.6;
            Check(BrakePracticeCourse.MonzaFirstChicane(lap)==null,"gap during measured release invalidates interval");
            return checks;
        }
    }
}
