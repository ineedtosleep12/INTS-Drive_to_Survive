using System;
using System.Threading;
using System.Linq;
using System.Reflection;

namespace IntsLiveCoach
{
    internal static class SelfTestMain
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--probe")
                {
                    string game = args.Length > 1 ? args[1].ToLowerInvariant() : "lmu";
                    ITelemetryReader source;
                    switch(game) { case "acc": source=new AssettoReader(AssettoGame.Acc);break;case "ac":source=new AssettoReader(AssettoGame.Ac);break;case "acr":source=new AcrReader();break;case "ams2":source=new Ams2Reader();break;case "wrc":source=new WrcReader();break;case "lmu":source=new LmuReader();break;default:throw new ArgumentException("Game: lmu / acc / ams2 / ac / acr / wrc"); }
                    using (var reader = source)
                    {
                        string status = ""; TelemetryFrame frame;
                        for (int i = 0; i < 60; i++)
                        {
                            if (reader.TryRead(out frame, out status))
                            {
                                Console.WriteLine("LIVE " + game.ToUpperInvariant() + " frame received: " + frame.Track + " / " + frame.Car + " / lap " + frame.Lap + " / " + Math.Round(frame.SpeedKph) + " km/h");
                                return 0;
                            }
                            Thread.Sleep(50);
                        }
                        Console.WriteLine("NO LIVE FRAME: " + status);
                        return 2;
                    }
                }
                int total=0;
                foreach(Type test in typeof(SelfTestMain).Assembly.GetTypes().Where(t=>t.Name.EndsWith("Tests",StringComparison.Ordinal)).OrderBy(t=>t.Name))
                {
                    MethodInfo run=test.GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
                    if(run==null)throw new Exception("Missing test entry point: "+test.Name);
                    int count=(int)run.Invoke(null,null); total+=count; Console.WriteLine("PASS: "+test.Name+" ("+count+")");
                }
                Console.WriteLine("PASS: " + total + " synthetic assertions.");
                Console.WriteLine("Synthetic data tests only. This is not proof of a live driving session.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
