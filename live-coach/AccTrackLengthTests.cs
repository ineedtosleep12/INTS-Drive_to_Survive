using System;
using System.IO;
using System.Text;

namespace IntsLiveCoach
{
    public static class AccTrackLengthTests
    {
        private static int checks;
        public static int Run()
        {
            checks = 0;
            var state = new AccTrackDataState();
            Check(!state.Accept(Track(21,"nurburgring",5137),1),"unregistered track rejected");
            Check(Double.IsNaN(state.LengthFor("nurburgring",1)),"missing registration never gives metres");
            Check(state.Accept(Register(21,true),1)&&state.Registered&&state.ConnectionId==21,"v4 registration result");
            Check(!state.Accept(Track(22,"nurburgring",5137),1),"connection ID mismatch");
            Check(state.Accept(Track(21,"nurburgring",5137),2),"native TrackMeters and track identity accepted");
            Check(state.LengthFor("nurburgring",2)==5137,"native track length retained without guessed table");
            Check(Double.IsNaN(state.LengthFor("nurburgring_24h",2)),"different layout cannot reuse length");
            Check(Double.IsNaN(state.LengthFor("spa",2)),"different track cannot reuse length");
            Check(Double.IsNaN(state.LengthFor("nurburgring",5.01)),"stale track data expires");
            Check(Double.IsNaN(state.LengthFor("nurburgring",1)),"clock rollback cannot make old data fresh");
            Check(!state.Accept(Register(22,true),3)&&state.ConnectionId==21,"duplicate registration cannot replace connection");
            byte[] full=Track(21,"nurburgring",5137);
            for(int i=0;i<full.Length;i++) {byte[] prefix=new byte[i];Array.Copy(full,prefix,i);Check(!state.Accept(prefix,4),"truncated track datagram "+i);}
            byte[] tail=new byte[full.Length+1];Array.Copy(full,tail,full.Length);Check(!state.Accept(tail,4),"unknown trailing format rejected");
            Check(!state.Accept(Track(21,"nurburgring",0),4),"unused zero length rejected");
            Check(!state.Accept(Track(21,"nurburgring",100001),4),"implausible length rejected");
            Check(!state.Accept(Track(21,"nu\nrb",5137),4),"control characters in track name rejected");
            Check(!state.Accept(new byte[8193],4),"oversize packet rejected");
            Check(!state.Accept(Track(21,"nurburgring",5137),Double.NaN),"nonfinite capture time rejected");
            state.ForgetTrack();Check(Double.IsNaN(state.LengthFor("nurburgring",4))&&state.Registered,"track switch clears extent while retaining registration");
            state.Reset();Check(!state.Registered&&Double.IsNaN(state.LengthFor("nurburgring",4)),"disconnect clears all extent state");
            Check(state.Accept(Register(9,false),4)&&!state.Registered,"registration refusal leaves unavailable");

            int port;string password;
            const string config="{\"updListenerPort\":9000,\"connectionPassword\":\"synthetic-password\",\"commandPassword\":\"never-used\"}";
            Check(AccTrackLengthProvider.TryConfigurationJson(config,out port,out password)&&port==9000&&password=="synthetic-password","existing config connection password only");
            Check(!AccTrackLengthProvider.TryConfigurationJson(config.Replace("9000","0"),out port,out password),"disabled UDP port");
            Check(!AccTrackLengthProvider.TryConfigurationJson(config.Replace("9000","65536"),out port,out password),"out of range UDP port");
            Check(!AccTrackLengthProvider.TryConfigurationJson(config.Replace("9000","\"9000\""),out port,out password),"string UDP port rejected");
            Check(!AccTrackLengthProvider.TryConfigurationJson("{}",out port,out password)&&password=="","missing config has no credentials");
            Check(!AccTrackLengthProvider.TryConfigurationJson("{bad",out port,out password),"malformed config fails without secret errors");
            string temp=Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(temp,new UnicodeEncoding(false,false,true).GetBytes(config));
                Check(AccTrackLengthProvider.TryReadConfiguration(temp,out port,out password)&&port==9000,"ACC UTF16LE configuration without BOM");
                File.WriteAllText(temp,config,new UTF8Encoding(false));
                Check(AccTrackLengthProvider.TryReadConfiguration(temp,out port,out password)&&port==9000,"UTF8 configuration");
            }
            finally {File.Delete(temp);}
            using(var reader=new BinaryReader(new MemoryStream(AccTrackLengthProvider.Registration("fixture"))))
            {
                Check(reader.ReadByte()==1&&reader.ReadByte()==4,"register command uses installed official protocol v4");
                ReadString(reader);Check(ReadString(reader)=="fixture","connection password sent only in local registration");
                Check(reader.ReadInt32()==1000&&ReadString(reader)==""&&reader.BaseStream.Position==reader.BaseStream.Length,"empty command password and conservative update rate");
            }
            byte[] request=AccTrackLengthProvider.TrackRequest(21);Check(request.Length==5&&request[0]==11&&BitConverter.ToInt32(request,1)==21,"request only track metadata for registered connection");
            return checks;
        }
        private static byte[] Register(int id,bool success)
        {using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write((byte)1);writer.Write(id);writer.Write((byte)(success?1:0));writer.Write((byte)0);WriteString(writer,"");return stream.ToArray();}}
        private static byte[] Track(int id,string name,int length)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
            {writer.Write((byte)5);writer.Write(id);WriteString(writer,name);writer.Write(7);writer.Write(length);writer.Write((byte)1);WriteString(writer,"cameraSet");writer.Write((byte)1);WriteString(writer,"camera");writer.Write((byte)1);WriteString(writer,"HUD");return stream.ToArray();}
        }
        private static void WriteString(BinaryWriter writer,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);writer.Write((ushort)bytes.Length);writer.Write(bytes);}
        private static string ReadString(BinaryReader reader){return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));}
        private static void Check(bool value,string name){++checks;if(!value)throw new Exception("ACC track metadata fixture: "+name);}
    }
}
