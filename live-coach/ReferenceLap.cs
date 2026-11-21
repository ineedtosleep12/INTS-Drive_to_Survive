using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace IntsLiveCoach
{
    public sealed class ReferenceSample
    {
        public double Time, DistanceM;
        public double SpeedKph=double.NaN, Brake=double.NaN, Throttle=double.NaN, Steering=double.NaN;
        public double SteeringDegrees=double.NaN; // Raw source angle for viewing; never normalized by an assumed lock.
        public double X=double.NaN, Z=double.NaN, PathLateralM=double.NaN;
        public ReferenceSample Copy() { return (ReferenceSample)MemberwiseClone(); }
    }

    // A transport format for recorded evidence, not a claim that a lap is optimal.
    public sealed class ReferenceLap
    {
        public const string LmuDistanceBasis="lmu-scoring-distance-m", LmuCoordinateFrame="lmu-world-xz-m";
        public const string LmuLateralBasis="lmu-scoring-path-lateral-m", LmuMotecDistanceBasis="lmu-motec-lap-distance-m", LmuMotecLateralBasis="lmu-motec-path-lateral-m";
        public const int MaximumSamples=100000, MaximumJsonCharacters=32*1024*1024;
        public int SchemaVersion=1;
        public const string RecordedTimeBasis="recorded-time-s",DistanceOnlyTimeBasis="unavailable-distance-only";
        public string TimeBasis=RecordedTimeBasis;
        public bool HasTime {get{return TimeBasis==RecordedTimeBasis;}}
        public string Game="",Car="",Track="",Layout="unknown",GameVersion="unknown";
        public string DistanceBasis="",CoordinateFrame="unknown",LateralPositionBasis="unknown";
        public string SourceUrl="",Author="unknown",RecordedAt="unknown",Licence="unknown",Provenance="";
        public string Validity="unknown";
        public bool Complete;
        public double TrackLengthM=double.NaN;
        public Dictionary<string,string> Conditions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public ReferenceSample[] Samples=new ReferenceSample[0];

        internal static bool Finite(double n) { return !double.IsNaN(n)&&!double.IsInfinity(n); }
        internal static bool Unknown(string s) { return string.IsNullOrWhiteSpace(s)||string.Equals(s.Trim(),"unknown",StringComparison.OrdinalIgnoreCase)||s.Trim()=="미확인"; }
        internal static string Key(string s) { return (s??"").Trim().ToLowerInvariant(); }
        static object Number(double n) { return Finite(n)?(object)n:null; }
        static string Text(IDictionary<string,object> d,string key,string fallback)
        {
            object v;if(!d.TryGetValue(key,out v)||v==null)return fallback;
            if(!(v is string))throw new FormatException(key+": 문자열이 필요합니다.");
            return (string)v;
        }
        static double Numeric(IDictionary<string,object> d,string key,bool required)
        {
            object v;if(!d.TryGetValue(key,out v)||v==null){if(required)throw new FormatException(key+": 필수 수치가 없습니다.");return double.NaN;}
            if(v is string||v is bool)throw new FormatException(key+": 숫자 형식이 필요합니다.");
            double n;try{n=Convert.ToDouble(v,CultureInfo.InvariantCulture);}catch(Exception){throw new FormatException(key+": 숫자 형식이 필요합니다.");}
            if(!Finite(n))throw new FormatException(key+": 유한한 숫자가 필요합니다.");return n;
        }
        static bool Boolean(IDictionary<string,object> d,string key)
        {object v;if(!d.TryGetValue(key,out v))return false;if(!(v is bool))throw new FormatException(key+": true/false가 필요합니다.");return (bool)v;}
        static Dictionary<string,object> Object(object value,string label)
        {var d=value as Dictionary<string,object>;if(d==null)throw new FormatException(label+": JSON 객체가 필요합니다.");return d;}
        public static ReferenceLap LoadJson(string json)
        {
            if(string.IsNullOrWhiteSpace(json)||json.Length>MaximumJsonCharacters)throw new FormatException("기준 주행 JSON이 비어 있거나 32 MiB 제한을 넘었습니다.");
            var serializer=new JavaScriptSerializer {MaxJsonLength=MaximumJsonCharacters,RecursionLimit=24};
            Dictionary<string,object> d;
            try{d=Object(serializer.DeserializeObject(json),"reference");}
            catch(ArgumentException ex){throw new FormatException("기준 주행 JSON 구문을 확인하세요.",ex);}
            catch(InvalidOperationException ex){throw new FormatException("기준 주행 JSON 구조/깊이를 확인하세요.",ex);}
            if(Numeric(d,"schemaVersion",true)!=1)throw new FormatException("지원하지 않는 기준 주행 스키마입니다.");
            var lap=new ReferenceLap {
                TimeBasis=Text(d,"timeBasis",RecordedTimeBasis),Game=Text(d,"game",""),Car=Text(d,"car",""),Track=Text(d,"track",""),Layout=Text(d,"layout","unknown"),GameVersion=Text(d,"gameVersion","unknown"),
                TrackLengthM=Numeric(d,"trackLengthM",false),DistanceBasis=Text(d,"distanceBasis",""),CoordinateFrame=Text(d,"coordinateFrame","unknown"),LateralPositionBasis=Text(d,"lateralPositionBasis","unknown"),
                SourceUrl=Text(d,"sourceUrl",""),Author=Text(d,"author","unknown"),RecordedAt=Text(d,"recordedAt","unknown"),Licence=Text(d,"licence","unknown"),Provenance=Text(d,"provenance",""),
                Validity=Text(d,"validity","unknown"),Complete=Boolean(d,"complete")};
            object raw;
            if(d.TryGetValue("conditions",out raw)&&raw!=null)
            {
                var conditions=Object(raw,"conditions");if(conditions.Count>40)throw new FormatException("주행 조건 항목이 너무 많습니다.");
                foreach(var item in conditions)
                {
                    if(item.Value!=null&&!(item.Value is string))throw new FormatException("conditions 값은 문자열 또는 null이어야 합니다.");
                    if(lap.Conditions.ContainsKey(item.Key))throw new FormatException("대소문자만 다른 중복 주행 조건이 있습니다.");
                    lap.Conditions.Add(item.Key,(string)item.Value??"unknown");
                }
            }
            if(!d.TryGetValue("samples",out raw))throw new FormatException("samples가 없습니다.");
            var source=raw as object[];if(source==null||source.Length<2||source.Length>MaximumSamples)throw new FormatException("주행 표본은 2~100000개가 필요합니다.");
            var samples=new List<ReferenceSample>(source.Length);
            foreach(object item in source)
            {
                var s=Object(item,"sample");var point=new ReferenceSample {Time=Numeric(s,"time",lap.HasTime),DistanceM=Numeric(s,"distanceM",true),SpeedKph=Numeric(s,"speedKph",false),Brake=Numeric(s,"brake",false),Throttle=Numeric(s,"throttle",false),Steering=Numeric(s,"steering",false),SteeringDegrees=Numeric(s,"steeringDegrees",false),X=Numeric(s,"x",false),Z=Numeric(s,"z",false),PathLateralM=Numeric(s,"pathLateralM",false)};
                if(samples.Count>0&&point.Time==samples[samples.Count-1].Time)
                {
                    var previous=samples[samples.Count-1];
                    if(SameSample(previous,point))continue;
                    throw new FormatException("같은 시각에 서로 다른 표본이 있습니다.");
                }
                samples.Add(point);
            }
            lap.Samples=samples.ToArray();lap.Validate();return lap;
        }
        static bool Same(double a,double b) {return a==b||(double.IsNaN(a)&&double.IsNaN(b));}
        static bool SameSample(ReferenceSample a,ReferenceSample b)
        {return Same(a.DistanceM,b.DistanceM)&&Same(a.SpeedKph,b.SpeedKph)&&Same(a.Brake,b.Brake)&&Same(a.Throttle,b.Throttle)&&Same(a.Steering,b.Steering)&&Same(a.SteeringDegrees,b.SteeringDegrees)&&Same(a.X,b.X)&&Same(a.Z,b.Z)&&Same(a.PathLateralM,b.PathLateralM);}
        static void Label(string text,string name,bool required,int max)
        {
            if(text==null||(required&&string.IsNullOrWhiteSpace(text))||text.Length>max)throw new FormatException(name+": 이름/길이를 확인하세요.");
            foreach(char c in text)if(char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t')throw new FormatException(name+": 제어문자를 사용할 수 없습니다.");
        }
        public void Validate()
        {
            if(SchemaVersion!=1)throw new FormatException("지원하지 않는 기준 주행 스키마입니다.");
            if(TimeBasis!=RecordedTimeBasis&&TimeBasis!=DistanceOnlyTimeBasis)throw new FormatException("지원하지 않는 표본 시각 기준입니다.");
            Label(Game,"game",true,128);Label(Car,"car",true,256);Label(Track,"track",true,256);Label(Layout,"layout",false,128);Label(GameVersion,"gameVersion",false,256);
            Label(DistanceBasis,"distanceBasis",true,128);Label(CoordinateFrame,"coordinateFrame",false,256);Label(LateralPositionBasis,"lateralPositionBasis",false,256);Label(SourceUrl,"sourceUrl",false,2048);Label(Author,"author",false,512);
            Label(RecordedAt,"recordedAt",false,128);Label(Licence,"licence",false,2048);Label(Provenance,"provenance",false,4096);
            if(Key(Validity)!="valid"&&Key(Validity)!="unknown"&&Key(Validity)!="invalid")throw new FormatException("validity는 valid/unknown/invalid 중 하나여야 합니다.");
            if(!double.IsNaN(TrackLengthM)&&(!Finite(TrackLengthM)||TrackLengthM<100||TrackLengthM>100000))throw new FormatException("트랙 길이 범위를 확인하세요.");
            if(Conditions==null||Conditions.Count>40)throw new FormatException("주행 조건 항목을 확인하세요.");
            foreach(var item in Conditions){Label(item.Key,"condition",true,128);Label(item.Value,"condition value",false,1024);}
            if(Samples==null||Samples.Length<2||Samples.Length>MaximumSamples)throw new FormatException("주행 표본은 2~100000개가 필요합니다.");
            ReferenceSample previous=null;
            foreach(var s in Samples)
            {
                if(s==null||!Finite(s.DistanceM)||s.DistanceM<0||s.DistanceM>100000||(HasTime?(!Finite(s.Time)||s.Time<0):!double.IsNaN(s.Time)))throw new FormatException("시각과 거리 표본을 확인하세요. 시각 미제공 기록에는 null만 허용합니다.");
                if(previous!=null&&(HasTime?(s.Time<=previous.Time||s.DistanceM<previous.DistanceM):s.DistanceM<=previous.DistanceM))throw new FormatException("한 랩 안의 시각·거리 순서를 확인하세요.");
                if(Finite(TrackLengthM)&&s.DistanceM>TrackLengthM+5)throw new FormatException("표본이 명시한 트랙 길이를 벗어납니다.");
                Optional(s.SpeedKph,0,550,"speedKph");Optional(s.Brake,0,1,"brake");Optional(s.Throttle,0,1,"throttle");Optional(s.Steering,-1,1,"steering");Optional(s.X,-10000000,10000000,"x");Optional(s.Z,-10000000,10000000,"z");
                Optional(s.PathLateralM,-1000,1000,"pathLateralM");
                Optional(s.SteeringDegrees,-2000,2000,"steeringDegrees");
                if(Finite(s.X)!=Finite(s.Z))throw new FormatException("평면 위치는 x와 z를 함께 제공해야 합니다.");
                previous=s;
            }
            if(Samples[Samples.Length-1].Time-Samples[0].Time>7200)throw new FormatException("한 랩 기록이 2시간을 넘었습니다.");
            if(Samples[Samples.Length-1].DistanceM-Samples[0].DistanceM<1)throw new FormatException("거리 진행이 없는 기록은 비교할 수 없습니다.");
            if(Complete&&(!Finite(TrackLengthM)||Samples[0].DistanceM>5||Samples[Samples.Length-1].DistanceM<TrackLengthM-5))throw new FormatException("완전한 랩으로 표시했지만 출발선부터 끝까지의 데이터가 없습니다.");
        }
        static void Optional(double n,double lo,double hi,string name)
        {if(double.IsNaN(n))return;if(!Finite(n)||n<lo||n>hi)throw new FormatException(name+": 단위 또는 수치 범위를 확인하세요.");}
        public string ToJson()
        {
            Validate();var serializer=new JavaScriptSerializer {MaxJsonLength=MaximumJsonCharacters,RecursionLimit=24};
            return serializer.Serialize(new {schemaVersion=SchemaVersion,timeBasis=TimeBasis,game=Game,car=Car,track=Track,layout=Layout,gameVersion=GameVersion,trackLengthM=Number(TrackLengthM),distanceBasis=DistanceBasis,coordinateFrame=CoordinateFrame,lateralPositionBasis=LateralPositionBasis,
                sourceUrl=SourceUrl,author=Author,recordedAt=RecordedAt,licence=Licence,provenance=Provenance,validity=Validity,complete=Complete,conditions=Conditions,
                samples=Samples.Select(s=>new {time=Number(s.Time),distanceM=s.DistanceM,speedKph=Number(s.SpeedKph),brake=Number(s.Brake),throttle=Number(s.Throttle),steering=Number(s.Steering),steeringDegrees=Number(s.SteeringDegrees),x=Number(s.X),z=Number(s.Z),pathLateralM=Number(s.PathLateralM)}).ToArray()});
        }
    }
}
