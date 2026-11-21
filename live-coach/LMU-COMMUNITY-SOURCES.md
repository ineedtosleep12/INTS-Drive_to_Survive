# LMU 공개 기준 주행

이 기록은 Vasco Duarte가 LMU에서 주행하고 공개한 세션에서 가져왔다. 차량, 게임 내 코스명, 브레이크·조향·가속 입력, 속도와 평면 위치를 같은 시점에서 비교할 수 있다. 원본은 저장소의 테스트와 분석에 쓰인 실제 주행 자료이며 각 배포 JSON에 원저작자의 MIT 고지 전문을 넣었다. [원본 기록과 설명](https://github.com/vasco-duarte-oikosofy/lap-telemetry/tree/ee11c95b8c64047da55acfe7c22f5cfa7043368a/dev/sessions) · [라이선스](https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/ee11c95b8c64047da55acfe7c22f5cfa7043368a/LICENSE)

| 게임이 보고한 코스 | 코스 구성 | 차량 | 계측선 간격 | 표본 | 파일 |
| --- | --- | --- | ---: | ---: | --- |
| Fuji Speedway | 기본 코스, 시케인 포함 | 지네타 LMP3 | 1:44.187 | 521 | fuji-ginetta-lmp3.reference.json |
| Autodromo Enzo e Dino Ferrari | 이몰라 풀코스, WEC·ELMS 구성 미확인 | 지네타 LMP3 | 1:41.463 | 507 | imola-ginetta-lmp3.reference.json |
| Bahrain International Circuit | 그랑프리 | 지네타 LMP3 | 1:56.684 | 584 | bahrain-ginetta-lmp3.reference.json |
| Circuit de Barcelona | 2025 ELMS, 마지막 시케인 없는 코스 | 지네타 LMP3 | 1:36.953 | 484 | barcelona-ginetta-lmp3.reference.json |
| Circuit of the Americas | 풀코스 | 지네타 LMP3 | 2:04.135 | 621 | cota-ginetta-lmp3.reference.json |
| Lusail International Circuit | 풀코스 | 지네타 LMP3 | 1:52.373 | 561 | lusail-ginetta-lmp3.reference.json |
| Paul Ricard - 3A | 단축 3A | 지네타 LMP3 | 1:19.135 | 395 | paul-ricard-3a-ginetta-lmp3.reference.json |
| Silverstone International Circuit | 단축 International | 페라리 296 LMGT3 Evo | 1:03.102 | 315 | silverstone-international-ferrari-296-evo.reference.json |

지네타의 게임 차량명은 `DKR Engineering #4:ELMS25`, 페라리는 `Vista AF Corse 2026 #21:LM`이다. 지네타의 ELMS 표기는 차량 도색을 식별한다. 이몰라의 원본 코스명만으로는 어느 경기의 출발선을 썼는지 확정할 수 없어 레이아웃을 미확인으로 저장했다. 다른 서킷은 원본 코스명과 한 바퀴의 거리 범위를 게임 제작사의 코스 설명과 대조했다. 폴 리카르 3A의 마지막 표본은 3,743m, 실버스톤 International은 2,951m 부근이다. [게임의 지네타 차량 설명](https://lemansultimate.com/cars/ginetta-g61-lt-p325-evo/) · [페라리 21번 공식 엔트리](https://assets.lemans.org/explorer/pdf/courses/2026/24-heures-du-mans/spotter-le-mans-2026.pdf) · [후지·바레인·COTA 코스 구성](https://lemansultimate.com/february-update-public-release-notes/) · [바르셀로나·폴 리카르·실버스톤·이몰라 코스 구성](https://lemansultimate.com/le-mans-ultimate-releases-v1-3-update-with-final-elms-content-performance-updates/) · [루사일 코스 구성](https://lemansultimate.com/circuit/lusail-international/)

## 계측 범위

게임의 거리와 시계는 초당 약 5회 갱신된다. 같은 시각에 여러 번 읽은 값 중 최초의 조작·위치 표본을 선택했다. 일곱 코스에는 게임의 원래 코스 거리 `mLapDist`, 횡위치 `mPathLateral`, 월드 좌표 `mPos.x/z`가 있다. 후지의 이전 형식에는 거리 갱신 사이를 속도로 추정한 열과 월드 좌표만 남아 있어 후지의 거리 비교에는 탐색 정렬이 필요하다. [기록 프로그램의 채널 정의](https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/ee11c95b8c64047da55acfe7c22f5cfa7043368a/product/python/lap_telemetry/recorder/connect.py) · [후지 녹화 당시 거리 산출 코드](https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/461aebfe23d278340e4d1850bd33ffb326b4d765/lap_telemetry/recorder/connect.py)

표의 시간은 연속된 두 랩의 시작 시각 차이다. 짧게 잘라 공개된 실버스톤 기록은 게임의 랩 카운터 증가 및 직전 랩 기록 63.102081초도 확인했다. 결승선을 정확히 통과한 순간의 위치·조작은 저장되지 않아 코너별로 관측된 범위만 비교한다. 게임의 원래 코스 길이도 파일에서 확인할 수 없어 해당 값은 비워 두었다. 소스의 랩 카운트 표시는 무효 시간으로 완주한 경우까지 포함할 수 있으므로 모든 기록의 트랙 리밋 승인 여부는 미확인이다. 패치·BoP·셋업 조건을 확인할 자료도 없다.

## 이몰라 두 랩 복기 예제

게임 없이 복기를 체험할 때 비교하는 두 랩은 Vasco Duarte가 공개한 2026년 5월 14일 이몰라 세션의 지네타 LMP3 기록이다. 두 랩 모두 같은 세션에서 받은 랩 번호, 거리, 시각, 입력, 속도와 위치를 사용한다. 배포 파일마다 원본 MIT 고지 전문이 들어 있다. [원본 세션 데이터](https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/ee11c95b8c64047da55acfe7c22f5cfa7043368a/dev/sessions/session_20260514T173300Z_autodromo-enzo-e-dino-ferrari_lmu.parquet) · [세션 정보](https://github.com/vasco-duarte-oikosofy/lap-telemetry/blob/ee11c95b8c64047da55acfe7c22f5cfa7043368a/dev/sessions/session_20260514T173300Z_autodromo-enzo-e-dino-ferrari_lmu.json)

| 역할 | 원본 랩 번호 | 계측선 간격 | 표본 | 첫 위치 → 마지막 위치 |
| --- | ---: | ---: | ---: | ---: |
| 기준 랩 | 11 | 1:41.463 | 507 | 12.4 → 4,895.7 m |
| 비교 랩 | 8 | 1:42.778 | 513 | 12.9 → 4,888.3 m |

전체 계측선 간격 차이는 약 1.316초다. 출력 그래프의 양 끝은 두 기록이 함께 관측된 거리로 제한되므로 그래프 마지막 지점의 시간차는 약 1.319초다. 이 차이를 특정 코너나 한 가지 조작의 탓으로 돌릴 수는 없다.

| 재생성 확인 대상 | SHA-256 |
| --- | --- |
| 원본 Parquet | `b91f69b3a3dd322607239d542c06b8dcbdeda1559c5770dc5651cc9bd41c3f47` |
| 원본 세션 JSON | `81b7cabc99713df63be3a9634810f1c999c6f73d530f44705400a0145c8582fd` |
| 원본 LICENSE | `15168c8cb518d6f1f982f8e236117c3a3d18dd6e4fdd61393c966b19be108e9b` |
| imola-ginetta-lmp3.reference.json | `0a71bc61b0536f73814212dbc849cffe1d1365f46a675364c4943a27b11a02a3` |
| review-example/imola-comparison.json | `1fbe5c2044404ea209376af721557cb925b41f8b078a20680d5185b4730639b0` |

## 원본으로 다시 만들기

실행 파일에는 완성된 기준 주행이 들어 있다. 재생성 시에는 Python 3.9 이상과 DuckDB가 필요하다. 위 원본 버전의 루트 `LICENSE`와 `lmu-community-sessions.json`에 명시된 세션의 `.parquet`, `.json`을 한 폴더에 둔다. 아래 명령은 해시, 차량·트랙 식별자, 랩 전환, 시간과 거리 순서, 위치 연속성, 입력 범위를 검사한 뒤 출력한다.

```sh
python -m pip install duckdb==1.4.4
python import-lmu-community.py source-files public-references
python import-lmu-community.py source-files public-references --only fuji
python import-lmu-community.py source-files public-references --only imola
python import-lmu-community.py source-files review-example --review-example imola
```
