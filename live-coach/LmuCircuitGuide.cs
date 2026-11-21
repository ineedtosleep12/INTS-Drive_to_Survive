using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IntsLiveCoach
{
    public sealed class LmuCircuitCourse
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Layout;
        public readonly int LengthMeters;
        public readonly string Summary;
        public readonly IReadOnlyList<string> Drills;
        public readonly string OfficialUrl;
        public readonly string VideoUrl;
        private readonly string[] aliases;

        internal LmuCircuitCourse(string id, string name, string layout, int lengthMeters, string summary, string[] drills,
            string officialUrl, string videoUrl, params string[] otherAliases)
        {
            Id = id;
            Name = name;
            Layout = layout;
            LengthMeters = lengthMeters;
            Summary = summary;
            Drills = Array.AsReadOnly(drills);
            OfficialUrl = officialUrl;
            VideoUrl = videoUrl;
            aliases = otherAliases.Concat(new[] { id, name })
                .Select(NormalizeWords).Distinct().OrderByDescending(alias => alias.Length).ToArray();
        }

        // Use this only to suggest a guide. Telemetry comparisons use the actual session and reference metadata.
        public bool MatchesTrackFamily(string label)
        {
            if (String.IsNullOrWhiteSpace(label)) return false;
            string words = NormalizeWords(label);
            foreach (string alias in aliases)
                if (words.Contains(alias)) return true;
            return false;
        }

        // The published lap length is a plausibility check for starting personal practice.
        // Two layouts can share a length. An explicit conflicting label always wins.
        public bool MatchesPracticeLayout(string track, double lengthM)
        {
            if (String.IsNullOrWhiteSpace(track) || !ReferenceLap.Finite(lengthM)) return false;
            // The organizer's LMU Sebring 12H track entry lists 5820 m (event-2889
            // at simracing-gp.net). The official venue guide lists 6019 m. This
            // accepts either documented scale for the drill, without converting a lap.
            bool sebringScoringLength = Id == "sebring" && Math.Abs(lengthM - 5820) <= 10;
            if (!sebringScoringLength && Math.Abs(lengthM - LengthMeters) > LengthMeters * 0.01) return false;
            string remainder = NormalizeWords(track);
            bool foundVenue = false;
            foreach (string alias in aliases)
            {
                if (!remainder.Contains(alias)) continue;
                foundVenue = true;
                remainder = remainder.Replace(alias, " ");
            }
            if (!foundVenue) return false;
            remainder = remainder.Replace(" " + Layout.ToLowerInvariant() + " ", " ");
            if (Layout == "RC") remainder = remainder.Replace(" road course ", " ");
            if (Layout == "GP") remainder = remainder.Replace(" grand prix ", " ");
            // Bare venue names, years and these optional descriptors carry no variant information.
            return Regex.IsMatch(remainder, @"^(?:\s+|\b(?:19|20)\d{2}\b|\b(?:layout|circuit)\b)*$");
        }

        private static string NormalizeWords(string value)
        {
            string decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            string folded = Regex.Replace(decomposed, @"\p{M}+", "").Normalize(NormalizationForm.FormC);
            string words = " " + Regex.Replace(folded, @"[^\p{L}\p{Nd}]+", " ").Trim() + " ";
            return words.Replace(" le mans ultimate ", " ");
        }

        public override string ToString() { return Name + " (" + Layout + ")"; }
    }

    public static class LmuCircuitGuide
    {
        // These are exercises and links to published onboard videos. No sampled inputs or racing lines
        // are derived from the videos. Raw lap ownership and compatibility belong to ReferenceLibrary.
        public static readonly IReadOnlyList<LmuCircuitCourse> Courses = Array.AsReadOnly(new[] {
            new LmuCircuitCourse("imola", "이몰라", "ELMS", 4909,
                "시케인에서 첫 연석을 지난 뒤에도 다음 코너를 돌 공간이 남는지 연습합니다.",
                new[] {
                    "탐부렐로 · 첫 왼쪽 진입을 조금 늦춰 보세요. 바로 이어지는 오른쪽에서 핸들을 한 번에 돌릴 수 있는지 확인하세요.",
                    "토사 · 직선에서 속도를 줄인 뒤 핸들을 감으면서 브레이크를 서서히 푸세요. 차가 안쪽을 향하기 전에 바깥으로 밀리는지 느껴 보세요.",
                    "리바차 · 첫 왼쪽을 나올 때 트랙 폭을 조금 남겨 보세요. 두 번째 왼쪽 출구에서 핸들을 더 일찍 풀 수 있는지 비교하세요."
                }, "https://lemansultimate.com/circuit/imola/", "https://www.youtube.com/watch?v=owWwuADvhdQ",
                "autodromo internazionale enzo e dino ferrari", "autodromo enzo e dino ferrari", "enzo e dino ferrari"),
            new LmuCircuitCourse("fuji", "후지", "WEC", 4563,
                "긴 직선 끝의 제동과 마지막 코너 탈출을 따로 연습합니다. 출구 속도 차이는 직선 내내 이어집니다.",
                new[] {
                    "1번 코너 · 직선 옆 표지판 하나를 기준으로 제동을 시작하세요. 다음 랩에는 시작점은 그대로 두고 브레이크를 푸는 속도만 바꿔 보세요.",
                    "마지막 코너 · 꼭짓점을 조금 늦게 지나면서 핸들을 펴는 만큼 가속하세요. 다음 랩과 결승선 통과 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/fuji/", "https://www.youtube.com/watch?v=ZkKdtd-81pk",
                "fuji speedway", "fuji international speedway"),
            new LmuCircuitCourse("silverstone", "실버스톤", "WEC", 5891,
                "윙 피트에서 출발하는 전체 코스입니다. 고속 연속 코너에서 다음 방향 전환에 필요한 공간을 남겨 보세요.",
                new[] {
                    "콥스 · 같은 진입 속도에서 조향을 시작하는 위치만 바꿔 보세요. 출구 연석에 닿기 전에 핸들을 얼마나 풀 수 있는지 확인하세요.",
                    "매거츠–베케츠–채플 · 첫 방향 전환에서 속도를 조금 양보해 보세요. 마지막 채플에서 핸들을 일찍 펴면 행거 스트레이트의 같은 표지판에서 속도가 얼마나 달라지는지 비교하세요."
                }, "https://lemansultimate.com/circuit/silverstone-international/", "https://www.youtube.com/watch?v=ZdTEOLcqpaQ",
                "silverstone international circuit"),
            new LmuCircuitCourse("sebring", "세브링", "WEC", 6019,
                "콘크리트 노면의 요철 위에서 차가 흔들릴 때, 제동과 조향을 어떻게 이어갈지 연습합니다.",
                new[] {
                    "7번 헤어핀 · 바퀴가 곧게 선 상태에서 먼저 감속하세요. 핸들을 감을 때 브레이크를 조금씩 풀면 차가 안쪽을 향하는 느낌이 어떻게 달라지는지 확인하세요.",
                    "17번 선셋 벤드 · 요철에서 차가 흔들릴 때는 가속 페달을 일정하게 유지해 보세요. 차가 가라앉고 핸들을 펴면서 더 밟아 다음 결승선에서 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/sebring/", "https://www.youtube.com/watch?v=hwGPxLUk_Kw",
                "sebring international raceway", "sebring 12h", "sebring international raceway 12h"),
            new LmuCircuitCourse("monza", "몬자", "WEC", 5793,
                "긴 직선 끝에서 충분히 감속하고 첫 시케인의 두 번째 꼭짓점을 준비합니다.",
                new[] {
                    "레티필로 · 150m 보드를 관찰 기준으로 삼으세요. 제동을 시작하는 위치는 연료와 타이어에 맞춰 정하고, 두 번째 연석을 지난 뒤 추가 조향 없이 가속할 수 있는지 보세요.",
                    "아스카리 · 첫 왼쪽 진입 속도를 조금 줄여 가운데 오른쪽에서 차가 어디에 놓이는지 보세요. 마지막 왼쪽 출구를 지난 직선의 같은 지점에서 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/monza/", "https://www.youtube.com/watch?v=jtPPwxAlu0c",
                "autodromo nazionale monza", "autodromo nazionale di monza"),
            new LmuCircuitCourse("spa", "스파", "WEC", 7004,
                "오르막 고속 구간에 들어가기 전에 방향을 잡아 긴 케멜 직선에서의 손실을 줄입니다.",
                new[] {
                    "오 루주–라이디옹 · 오르막에 들어가기 전에 차의 위치를 일정하게 맞추세요. 언덕을 넘어갈 때 핸들을 되감아야 했다면 다음 랩에는 진입 속도를 낮춰 비교하세요.",
                    "라 소스 · 안쪽 연석을 조금 늦게 스쳐 보세요. 탈출 때 두 번째로 가속 페달을 들어야 했는지 기록하세요."
                }, "https://lemansultimate.com/circuit/spa/", "https://www.youtube.com/watch?v=F2b4f4jgDj4",
                "circuit de spa francorchamps", "spa francorchamps"),
            new LmuCircuitCourse("le-mans", "르망", "WEC", 13626,
                "긴 직선을 사이에 둔 저속 코너에서 탈출 속도가 다음 제동 구간까지 이어지는지 살핍니다.",
                new[] {
                    "아르나주 · 우회전 전에 속도를 충분히 줄이고 안쪽을 향한 뒤 가속하세요. 출구에서 핸들을 더 감느라 페달을 다시 놓는지 확인하세요.",
                    "뮬산 직선의 첫 시케인 · 도로 옆 고정 표지를 골라 직선 제동을 반복하세요. 두 번째 방향 전환을 마치고 같은 지점에서 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/circuit-de-la-sarthe/", "https://www.youtube.com/watch?v=KyBXVChpf8I",
                "circuit de la sarthe", "circuit des 24 heures du mans", "le mans la sarthe", "lemans"),
            new LmuCircuitCourse("portimao", "포르티망", "WEC", 4653,
                "언덕 너머 출구가 보이기 전에도 반복해서 찾을 수 있는 진입 표지를 고릅니다.",
                new[] {
                    "라고스 · 앞의 우회전에서 폭을 조금 남기고 더 느린 라고스에 진입하세요. 출구에서 핸들을 펴는 시점이 어떻게 달라졌는지 비교하세요.",
                    "마지막 갈프 코너 · 긴 오른쪽에서 한 번에 전가속했을 때 다시 페달을 들어야 했다면, 다음 랩에는 핸들을 펴는 속도에 맞춰 밟아 보세요."
                }, "https://lemansultimate.com/circuit/portimao/", "https://www.youtube.com/watch?v=UYHhD2EDuKY",
                "autodromo internacional do algarve", "algarve international circuit", "algarve int", "algarve"),
            new LmuCircuitCourse("bahrain", "바레인", "WEC", 5412,
                "조향이 들어간 제동 구간에서 앞바퀴가 감속과 회전에 쓸 수 있는 접지력을 나눠 봅니다.",
                new[] {
                    "9–10번 · 내리막 왼쪽으로 핸들을 감을수록 브레이크 압력을 천천히 줄여 보세요. 안쪽을 지나쳐 핸들을 더 감아야 했는지 확인하세요.",
                    "1번 · 긴 직선에서 제동을 끝까지 미룬 랩과 여유 있게 감속한 랩을 비교하세요. 바로 이어지는 왼쪽으로 차를 옮기기 쉬운 쪽을 찾아보세요."
                }, "https://lemansultimate.com/circuit/bahrain/", "https://www.youtube.com/watch?v=81g6ebI2OnE", "bahrain international circuit", "sakhir"),
            new LmuCircuitCourse("interlagos", "인터라고스", "WEC", 4314,
                "내리막 방향 전환과 오르막 직선 앞 저속 코너에서 출구에 필요한 공간을 읽습니다.",
                new[] {
                    "세나 S · 첫 왼쪽에서 안쪽에 너무 오래 붙지 말고 다음 오른쪽을 바라보세요. 내리막에서 핸들을 한 번에 반대로 돌릴 수 있는지 확인하세요.",
                    "준상 · 느린 왼쪽 출구에서 핸들을 펴며 가속하세요. 이어지는 오르막의 같은 지점에서 속도 차이를 확인하세요."
                }, "https://lemansultimate.com/circuit/interlagos/", "https://www.youtube.com/watch?v=m2UE0mKyJW8", "autodromo jose carlos pace", "jose carlos pace"),
            new LmuCircuitCourse("cota", "서킷 오브 디 아메리카스", "WEC", 5514,
                "오르막 헤어핀과 연속 방향 전환을 나눠 연습합니다.",
                new[] {
                    "1번 · 언덕 위 왼쪽 헤어핀에 들어갈 때 같은 제동 표지를 쓰세요. 조향 시작만 조금 늦춰 내리막 출구에서 핸들을 얼마나 일찍 펼 수 있는지 보세요.",
                    "3–6번 에스 · 첫 방향 전환에서 폭을 덜 쓰고 다음 연석을 보세요. 마지막 방향 전환까지 수정 조향이 줄어드는지 비교하세요."
                }, "https://lemansultimate.com/circuit/cota/", "https://www.youtube.com/watch?v=qjmank7qxzA", "circuit of the americas", "circuit of americas"),
            new LmuCircuitCourse("lusail", "루사일", "WEC", 5419,
                "여러 꼭짓점이 이어지는 고속 오른쪽에서 조향과 페달을 일정하게 유지합니다.",
                new[] {
                    "12–14번 · 세 개의 오른쪽을 한 묶음으로 보고 첫 꼭짓점 진입 속도를 조금 낮추세요. 마지막 출구에서 핸들을 더 감는 횟수가 줄었는지 보세요.",
                    "16번 · 메인 직선 앞 마지막 우회전에서 가속을 시작한 뒤 다시 놓지 않을 만큼 출구를 넓게 잡으세요. 결승선 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/lusail-international/", "https://www.youtube.com/watch?v=k5hsuU5Ll5Q", "lusail international circuit",
                "lusail international", "losail international circuit", "losail", "qatar"),
            new LmuCircuitCourse("barcelona", "바르셀로나", "ELMS", 4657,
                "마지막 시케인이 없는 전체 코스에서 긴 오른쪽 코너의 탈출을 연습합니다.",
                new[] {
                    "3번 · 긴 우회전 중간에 바깥으로 밀리면 다음 랩에는 진입을 조금 늦춰 보세요. 출구에서 페달을 다시 들어야 하는지 확인하세요.",
                    "마지막 우회전 · 직선이 열리면서 핸들을 펴는 만큼 가속하세요. 출구 연석 안에 머무른 랩끼리 결승선 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/circuit-de-barcelona-catalunya/", "https://www.youtube.com/watch?v=QMV7WKtBC04", "circuit de barcelona", "circuit de barcelona catalunya",
                "barcelona catalunya", "circuit de catalunya"),
            new LmuCircuitCourse("paul-ricard", "폴 리카르", "ELMS", 5770,
                "넓은 포장 구역에서 트랙의 흰 선과 고정 표지를 기준으로 진입 위치를 반복합니다.",
                new[] {
                    "시뉴 · 미스트랄 직선 끝에서 같은 지점을 기준으로 가속 페달을 놓아 보세요. 조향을 한 번만 더해도 흰 선 안에서 출구를 찾을 수 있는지 확인하세요.",
                    "보세 · 긴 우회전 초반에 안쪽에 너무 빨리 붙었다면 다음 랩에는 꼭짓점을 늦춰 보세요. 후반에 핸들을 더 감는지 비교하세요."
                }, "https://lemansultimate.com/circuit/circuit-paul-ricard/", "https://www.youtube.com/watch?v=bqElrEy3sXE", "circuit paul ricard", "le castellet"),
            new LmuCircuitCourse("daytona", "데이토나", "RC", 5740,
                "뱅크 구간에서 평지 제동으로 넘어갈 때와 백스트레치 시케인 탈출을 연습합니다.",
                new[] {
                    "1번 · 뱅크에서 인필드로 내려올 때 같은 제동 표지를 쓰세요. 핸들을 감으면서 브레이크를 조금씩 풀면 첫 왼쪽에 차를 대기가 쉬워지는지 보세요.",
                    "르망 시케인 · 첫 연석을 지나자마자 마지막 출구를 보세요. 시케인 진입 속도를 낮췄을 때 다음 뱅크에서 속도를 더 오래 유지하는지 비교하세요."
                }, "https://lemansultimate.com/circuit/daytona-international-speedway/", "https://www.youtube.com/watch?v=CpqSXHT9q3Y", "daytona international speedway"),
            new LmuCircuitCourse("road-atlanta", "로드 애틀랜타", "RC", 4088,
                "내리막 에스 구간에서 다음 방향 전환이 보이기 전에 차의 위치를 준비합니다.",
                new[] {
                    "에스 구간 · 첫 방향 전환을 마치자마자 다음 연석으로 시선을 옮기세요. 마지막 연석에서 바깥으로 밀리면 첫 진입 속도를 줄여 보세요.",
                    "12번 · 언덕을 넘기 전 반복해서 볼 수 있는 조향 기준점을 고르세요. 같은 연료량에서 결승선 속도와 출구 여유 폭을 비교하세요."
                }, "https://lemansultimate.com/circuit/michelin-raceway-road-atlanta/", "https://www.youtube.com/watch?v=ZOuYoC8dKms", "michelin raceway road atlanta"),
            new LmuCircuitCourse("laguna-seca", "라구나 세카", "RC", 3602,
                "언덕 너머 보이지 않는 내리막을 앞두고 차가 가벼워지기 전에 속도와 방향을 맞춥니다.",
                new[] {
                    "코크스크루 · 언덕 위 왼쪽에 닿기 전에 속도를 충분히 줄이세요. 다음 랩에는 같은 진입 속도에서 오른쪽 전환을 시작하는 위치만 바꿔 보세요.",
                    "마지막 헤어핀 · 왼쪽 꼭짓점을 조금 늦게 스쳐 보세요. 바깥 흰 선 안에서 한 번에 가속한 랩끼리 결승선 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/weathertech-raceway-laguna-seca/", "https://www.youtube.com/watch?v=1d6AkW39UZk", "weathertech raceway laguna seca"),
            new LmuCircuitCourse("long-beach", "롱비치", "GP", 3167,
                "벽 사이에서 안전 여유를 두고 저속 헤어핀의 회전과 가속을 분리합니다.",
                new[] {
                    "분수 구간 · 첫 진입에서 벽과의 간격을 일정하게 두세요. 다음 방향 전환에서 핸들을 급히 되감아야 했다면 첫 진입 속도를 줄여 보세요.",
                    "마지막 헤어핀 · 차가 쇼어라인 드라이브를 향할 때까지 기다렸다가 핸들을 펴며 밟으세요. 벽과 거리를 유지한 랩끼리 직선 속도를 비교하세요."
                }, "https://lemansultimate.com/circuit/streets-of-long-beach-circuit/", "https://www.youtube.com/watch?v=JseIfqKTIMQ", "streets of long beach circuit",
                "streets of long beach")
        });
    }
}
