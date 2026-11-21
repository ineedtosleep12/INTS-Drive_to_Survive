using System;
using System.Collections.Generic;
using System.Linq;

namespace IntsLiveCoach
{
    public static class LmuCircuitGuideTests
    {
        private static int checks;
        private static void Check(bool value, string description)
        {
            ++checks;
            if (!value) throw new Exception("LMU circuit guide: " + description);
        }

        public static int Run()
        {
            checks = 0;
            var courses = LmuCircuitGuide.Courses;
            Check(courses.Count == 18, "all 18 official venues have a representative layout");
            Check(courses.Sum(c => c.AvailableLayouts.Count) == 38 &&
                courses.All(c => c.AvailableLayouts[0] == c.Layout && c.AvailableLayouts.Distinct().Count() == c.AvailableLayouts.Count),
                "38 documented and locally installed layout packages, with one representative per venue");
            Check(LmuCircuitGuide.LiveLayout.IsLiveLayout && !courses.Contains(LmuCircuitGuide.LiveLayout) &&
                LmuCircuitGuide.LiveLayout.VideoUrl == "" && !LmuCircuitGuide.LiveLayout.MatchesTrackFamily("live-layout") &&
                !LmuCircuitGuide.LiveLayout.MatchesPracticeLayout("live-layout",0), "free layout has no named corner, video or preset reference match");
            Check(String.Join(",", courses.Take(8).Select(c => c.Id)) ==
                "imola,fuji,silverstone,sebring,monza,spa,le-mans,portimao", "practice and existing reference order");
            var byId = courses.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var urls = new HashSet<string>();
            foreach (var c in courses)
            {
                Check(c.Drills.Count >= 2 && c.Drills.All(d => !String.IsNullOrWhiteSpace(d)), c.Id + " corner drills");
                Check(new Uri(c.OfficialUrl).Host == "lemansultimate.com" && urls.Add(c.OfficialUrl), c.Id + " official source");
                Check(c.MatchesPracticeLayout(c.Name + " (" + c.Layout + ")", c.LengthMeters), c.Id + " displayed course");
                Check(c.MatchesPracticeLayout(c.Id, c.LengthMeters * 0.995), c.Id + " native length tolerance");
                Check(!c.MatchesPracticeLayout(c.Name, c.LengthMeters * 0.98), c.Id + " incompatible lap distance");
                Check(!c.MatchesPracticeLayout(c.Name, Double.NaN), c.Id + " missing length");
                Check(!c.MatchesPracticeLayout(c.Name, Double.PositiveInfinity), c.Id + " infinite length");
                Check(!c.MatchesPracticeLayout(null, c.LengthMeters), c.Id + " missing track name");
                Check(!c.MatchesPracticeLayout(c.Id + " unknown variant", c.LengthMeters), c.Id + " unknown layout");
            }
            Check(courses.All(c => new Uri(c.VideoUrl).Host == "www.youtube.com"), "published onboard links for all venues");
            Check(!byId["fuji"].MatchesTrackFamily("Fujiwara"), "partial word is not a venue");
            Check(!byId["le-mans"].MatchesTrackFamily("Le Mans Ultimate / Fuji"), "game name is not a venue");
            Check(byId["imola"].MatchesPracticeLayout("Autodromo Enzo e Dino Ferrari (ELMS)", 4910), "native Imola name");
            Check(!byId["imola"].MatchesPracticeLayout("Imola WEC", 4909), "same-length Imola WEC");
            Check(byId["fuji"].MatchesTrackFamily("Fuji Speedway - Classic") &&
                !byId["fuji"].MatchesPracticeLayout("Fuji Speedway - Classic", 4563), "Fuji Classic is only a family match");
            Check(byId["silverstone"].MatchesPracticeLayout("Silverstone International Circuit (WEC)", 5891),
                "International in full GP venue name");
            Check(!byId["silverstone"].MatchesPracticeLayout("Silverstone (International)", 5891) &&
                !byId["silverstone"].MatchesPracticeLayout("Silverstone International Circuit - National", 5891) &&
                !byId["silverstone"].MatchesPracticeLayout("Silverstone (ELMS)", 5891), "Silverstone alternatives");
            Check(byId["sebring"].MatchesPracticeLayout("Sebring International Raceway", 6019), "full Sebring name");
            Check(byId["sebring"].MatchesPracticeLayout("Sebring International Raceway", 5820) &&
                byId["sebring"].MatchesPracticeLayout("Sebring - 12H", 5820), "LMU 12H scoring length");
            Check(!byId["sebring"].MatchesPracticeLayout("Sebring School",5820) &&
                !byId["sebring"].MatchesPracticeLayout("Sebring",5900), "variant and undocumented length remain excluded");
            Check(!byId["spa"].MatchesPracticeLayout("Spa-Francorchamps (Endurance)", 7004), "Spa timing-line variation");
            Check(byId["portimao"].MatchesPracticeLayout("Autódromo Internacional do Algarve", 4653) &&
                !byId["portimao"].MatchesPracticeLayout("Portimão (ELMS)", 4653), "accented Portuguese venue and alternate grid");
            Check(!byId["le-mans"].MatchesPracticeLayout("Circuit de la Sarthe (Mulsanne)", 13626), "uninterrupted Mulsanne");
            Check(byId["interlagos"].MatchesPracticeLayout("Autódromo José Carlos Pace (WEC)", 4314), "São Paulo venue alias");
            Check(byId["daytona"].MatchesPracticeLayout("Daytona International Speedway - Road Course", 5740) &&
                !byId["daytona"].MatchesPracticeLayout("Daytona International Speedway - Moto", 5740), "Daytona sports car layout");
            Check(byId["lusail"].MatchesPracticeLayout("Qatar WEC", 5419) &&
                !byId["lusail"].MatchesPracticeLayout("Lusail International Circuit - Short", 5419), "Qatar variants");
            Check(byId["paul-ricard"].MatchesPracticeLayout("Circuit Paul Ricard ELMS", 5770) &&
                !byId["paul-ricard"].MatchesPracticeLayout("Paul Ricard 1a-V2", 5770), "Paul Ricard variants");
            Check(byId["long-beach"].MatchesPracticeLayout("Streets of Long Beach Grand Prix", 3167),
                "Long Beach expanded layout");
            return checks;
        }
    }
}
