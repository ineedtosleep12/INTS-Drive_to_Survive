using System;

namespace IntsLiveCoach
{
    public static class LmuCoursePracticeTests
    {
        static void Check(bool ok,string message)
        {
            if(!ok)throw new Exception("Circuit practice: "+message);
        }
        static ReferenceLap Metadata(string track="Imola ELMS", string car="Manthey #92")
        {
            return new ReferenceLap {Game="LMU", Car=car, Track=track, GameVersion="12", TrackLengthM=4909,
                Complete=true, Validity="valid", DistanceBasis=ReferenceLap.LmuDistanceBasis};
        }
        public static int Run()
        {
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
            return 14;
        }
    }
}
