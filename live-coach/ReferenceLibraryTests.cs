using System;
using System.IO;

namespace IntsLiveCoach
{
    public static class ReferenceLibraryTests
    {
        static int checks;
        static void Check(bool value,string name) {checks++;if(!value)throw new Exception("Reference library: "+name);}
        static ReferenceLap Lap(string track)
        {return new ReferenceLap {Game="LMU",Car="synthetic car",Track=track,DistanceBasis=ReferenceLap.LmuDistanceBasis,
            TrackLengthM=1000,Complete=false,Author="SYNTHETIC TEST ONLY",Samples=new[]{new ReferenceSample {Time=0,DistanceM=10},new ReferenceSample {Time=1,DistanceM=40}}};}
        public static int Run()
        {
            checks=0;string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ints-reference-library-"+Guid.NewGuid().ToString("N"));
            Check(ReferenceLibrary.Scan(root).Entries.Length==0,"absent folder does not invent a default track");
            Directory.CreateDirectory(root);
            string first=System.IO.Path.Combine(root,"a.json"),second=System.IO.Path.Combine(root,"b.json"),bad=System.IO.Path.Combine(root,"broken.json"),nested=System.IO.Path.Combine(root,"nested");
            try
            {
                File.WriteAllText(first,Lap("Synthetic Circuit A").ToJson());File.WriteAllText(second,Lap("Synthetic Circuit B").ToJson());File.WriteAllText(bad,"{broken");Directory.CreateDirectory(nested);File.WriteAllText(System.IO.Path.Combine(nested,"extra.json"),Lap("nested").ToJson());
                var scan=ReferenceLibrary.Scan(root);
                Check(scan.Entries.Length==2&&scan.Errors.Length==1,"bad file is isolated and nested data is not scanned");
                Check(scan.Entries[0].Title.Contains("Synthetic Circuit A")&&scan.Entries[1].Title.Contains("Synthetic Circuit B"),"circuit identity is read from data without Monza assumptions");
                Check(ReferenceLibrary.Read(scan.Entries[1].Path).Track=="Synthetic Circuit B","selected file supplies its own actual reference");
                var source=Lap("Snapshot");var settings=new ReferenceLiveSettings(source,true,false,true,"livery","track","layout");
                source.Car="mutated producer";source.Samples[0].DistanceM=20;
                var consumer=settings.Reference;Check(consumer.Car=="synthetic car"&&consumer.Samples[0].DistanceM==10,"snapshot is isolated from source mutation");
                consumer.Car="mutated consumer";consumer.Conditions["sourceAlias"]="mutated";
                Check(settings.Reference.Car=="synthetic car"&&!settings.Reference.Conditions.ContainsKey("sourceAlias"),"consumer cannot mutate stored snapshot");
                Check(settings.Enabled&&settings.AllowMotecAlignment&&!settings.AllowVersionMismatch&&settings.ConfirmedSourceCar=="livery","explicit choices and source tuple preserved");
                Check(new ReferenceLiveSettings(null,false,false,false).Reference==null,"disabled snapshot accepts absent reference");
                var recorded=Lap("View only");recorded.Game="ACC";recorded.DistanceBasis="acc-integrated-travel-distance-m";
                Check(ReferenceLibrary.ViewOnly(recorded)&&new ReferenceLibraryEntry(first,recorded).Title.Contains("입력 열람 전용"),"integrated distance recording is labelled view-only in library");
                Check(!ReferenceComparison.Compare(recorded,recorded,true,true).Comparable,"same speed-integral trace cannot bypass course-position comparison gates");
            }
            finally
            {
                foreach(string file in new[]{first,second,bad,System.IO.Path.Combine(nested,"extra.json")})if(File.Exists(file))File.Delete(file);
                if(Directory.Exists(nested))Directory.Delete(nested);
                Directory.Delete(root);
            }
            return checks;
        }
    }
}
