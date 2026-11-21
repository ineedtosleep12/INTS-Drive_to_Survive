using System;
using System.Linq;

namespace IntsLiveCoach
{
    public static class LmuCoursePracticeTests
    {
        static int assertions;
        static void Check(bool ok,string message)
        {
            assertions++;
            if(!ok)throw new Exception("Circuit practice: "+message);
        }
        static ReferenceLap Metadata(string track="Imola ELMS", string car="Manthey #92")
        {
            return new ReferenceLap {Game="LMU", Car=car, Track=track, GameVersion="12", TrackLengthM=4909,
                Complete=true, Validity="valid", DistanceBasis=ReferenceLap.LmuDistanceBasis};
        }
        public static int Run()
        {
            assertions=0;
            var session=new LmuCoursePractice();
            var lap=Metadata();
            Check(!session.Record(lap),"capture must be armed");
            session.Begin(LmuCircuitGuide.Courses[0]);
            Check(!session.Record(Metadata("Monza")),"another venue cannot seed the session");
            Check(!session.Record(Metadata("Imola WEC")),"explicit alternative layout cannot seed the session");
            var invalid=Metadata();invalid.Validity="unknown";
            Check(!session.Record(invalid),"unknown validity rejected");
            var partial=Metadata();partial.Complete=false;
            Check(!session.Record(partial),"partial capture rejected");
            var other=Metadata();other.Game="ACC";
            Check(!session.Record(other),"another game rejected");
            Check(session.Record(lap)&&session.ComparisonLaps==0,"first valid lap freezes a baseline");
            Check(!session.Record(Metadata("Imola ELMS","Different team")),"livery remains exact");
            var patch=Metadata();patch.GameVersion="13";
            Check(!session.Record(patch),"build mismatch rejected");
            var differentLength=Metadata();differentLength.TrackLengthM+=2;
            Check(!session.Record(differentLength),"game length mismatch rejected");
            Check(!session.Record(Metadata("Imola")),"different native label rejected even in same family");
            Check(session.Record(Metadata()) && session.ComparisonLaps==1 && ReferenceEquals(lap,session.Baseline),"later lap does not replace baseline");
            session.Begin(LmuCircuitGuide.Courses[1]);
            Check(session.Baseline==null && session.ComparisonLaps==0&&!session.Record(lap),"new course starts fresh");
            session.Begin(null);
            Check(!session.Accepts("Imola ELMS",4909,"Manthey #92","12"),"stopped session cannot accept frames");
            CheckLiveLayout(session);
            return assertions;
        }

        static ReferenceLap ShortLayout(double offset)
        {
            var lap=Metadata("Native short route");
            lap.TrackLengthM=2000.125;
            lap.CoordinateFrame=ReferenceLap.LmuCoordinateFrame;
            lap.Samples=Enumerable.Range(0,201).Select(i => {
                double angle=i*Math.PI/100;
                return new ReferenceSample { DistanceM=i*lap.TrackLengthM/200, Time=i*.2, SpeedKph=180,
                    Throttle=i<60 || i>=85?1:.1, Brake=i>=60 && i<75?.7:0, Steering=i>=60 && i<85?.4:0,
                    X=offset+320*Math.Cos(angle), Z=320*Math.Sin(angle) };
            }).ToArray();
            return lap;
        }
        static void CheckLiveLayout(LmuCoursePractice session)
        {
            var lap=ShortLayout(0);
            var sample=lap.Samples[40];
            var f=new TelemetryFrame {Track=lap.Track, Car=lap.Car, TrackLength=lap.TrackLengthM, GameVersion=lap.GameVersion,
                Time=12, LapDistance=sample.DistanceM, Lap=3, DistanceAgeSeconds=.1, IsValidLap=true, ValidLapKnown=true,
                Brake=0, Throttle=1, Steering=0, SpeedKph=180, X=sample.X, Z=sample.Z};
            Check(!session.TryBindLiveLayout(f),"inactive session cannot adopt a layout");
            session.Begin(LmuCircuitGuide.LiveLayout);
            Check(!session.Record(lap) && !session.TryBindLiveLayout(f),"out-of-session frames cannot seed an arbitrary layout");
            f.OnTrack=true;f.InPit=true;
            Check(!session.TryBindLiveLayout(f),"ignore garage and pit contexts");f.InPit=false;f.IsReplay=true;
            Check(!session.TryBindLiveLayout(f),"ignore replay");f.IsReplay=false;f.GameVersion="";
            Check(!session.TryBindLiveLayout(f),"build required");f.GameVersion=lap.GameVersion;f.TrackLength=Double.NaN;
            Check(!session.TryBindLiveLayout(f),"measured length required");f.TrackLength=lap.TrackLengthM;
            Check(session.TryBindLiveLayout(f) && session.LiveTrack==lap.Track && session.LiveCar==lap.Car &&
                session.LiveLengthMeters==lap.TrackLengthM,"bind unlisted layout, exact live labels and scoring length");
            f.Track="Another short route";
            Check(!session.TryBindLiveLayout(f) && session.LiveTrack==lap.Track && !session.Record(Metadata()),
                "subsequent frames cannot silently change the route before a baseline exists");
            Check(!session.Accepts(lap.Track,lap.TrackLengthM+.001,lap.Car,lap.GameVersion) &&
                !session.Accepts(lap.Track,lap.TrackLengthM,"Alternate",lap.GameVersion),"raw length and car locked before first lap");
            Check(session.Record(lap) && ReferenceComparison.NeedsNativePositionCheck(lap),"baseline requires layout position check");
            var saved=ReferenceLap.LoadJson(lap.ToJson());
            Check(!new ReferenceLibraryEntry("own.json",saved).IsCommunityRecording,"saved personal baseline remains personal");
            Check(!session.Record(ShortLayout(350)) && session.ComparisonLaps==0,"shifted origin cannot count as comparable lap");
            var other=ShortLayout(5);
            Check(session.Record(other) && session.ComparisonLaps==1 && ReferenceEquals(session.Baseline,lap),"native full lap compares");
            var branch=ShortLayout(5);
            for(int i=12;i<=18;i++)branch.Samples[i].X+=80;
            Check(!session.Record(branch) && session.ComparisonLaps==1 && !ReferenceComparison.Compare(saved,branch).Comparable,
                "route branches and rejoins between the coarse anchors");
            var onePacket=ShortLayout(5);
            onePacket.Samples[12].X+=80;
            Check(ReferenceComparison.Compare(saved,onePacket).Comparable,"one stray packet does not invalidate a whole lap");
            Check(ReferenceComparison.Compare(saved,other).Comparable && !ReferenceComparison.Compare(saved,ShortLayout(350)).Comparable,
                "saved reference retains physical origin guard");
            other.Conditions["sourceTrackLabel"]="Renamed wrong variant";
            Check(!ReferenceComparison.Compare(saved,other,true,true).Comparable,"manual alias cannot substitute for native layout after reload");
            other.Conditions.Clear();other.GameVersion="updated";
            Check(!ReferenceComparison.Compare(saved,other,true,true).Comparable,"manual version override cannot bypass original build");
            var coach=new LiveReferenceCoach();
            coach.Configure(new ReferenceLiveSettings(saved,true,true,true,"Remapped team",lap.Track,lap.Layout));
            f.Car="Remapped team";f.Track=lap.Track;
            Check(!coach.Update("LMU",f).Ready,"live probe retains actual team name during manual alias");
            f.Car=lap.Car;
            Check(coach.Update("LMU",f).Ready,"native position and exact live identity accepted");
            f.X+=350;f.Time+=.2;
            Check(!coach.Update("LMU",f).Ready,"coaching suppressed on alternate timing line");
            sample=lap.Samples[44];f.LapDistance=sample.DistanceM;f.X=sample.X+350;f.Z=sample.Z;f.Time+=.8;
            Check(!coach.Update("LMU",f).Ready,"measured route divergence persists");
            sample=lap.Samples[45];f.LapDistance=sample.DistanceM;f.X=sample.X;f.Z=sample.Z;f.Time+=.2;
            Check(!coach.Update("LMU",f).Ready,"rejoining at the same distance cannot resume cues on a diverged lap");
            f.Lap++;f.Time+=.2;
            Check(coach.Update("LMU",f).Ready,"next matching lap can resume");
            session.Begin(LmuCircuitGuide.Courses[0]);
            Check(session.LiveTrack==null && session.LiveCar==null && Double.IsNaN(session.LiveLengthMeters) &&
                !session.TryBindLiveLayout(f),"switching to guide clears live route binding");
        }
    }
}
