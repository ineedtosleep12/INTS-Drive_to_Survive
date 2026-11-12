/* INTS driving curriculum. Authored exercises; primary sources support principles, not a rating promise. */
(function () {
  'use strict';
  const categories = [
    {id:'foundation',label:'기초 원리',subtitle:'먼저 거동을 읽기'},
    {id:'racing',label:'GT · 레이싱',subtitle:'LMU GT3에서 출발'},
    {id:'rally',label:'랠리',subtitle:'Rally1 · 노면과 경로'},
    {id:'drift',label:'드리프트',subtitle:'AE86 · 시작부터 전환까지'},
    {id:'formula',label:'포뮬러',subtitle:'속도에 따라 달라지는 접지'}
  ];
  const sources = [
    {id:'grip',title:'Michelin — The Tyre: Grip',url:'https://docenti.ing.unipi.it/guiggiani-m/Michelin_Tire_Grip.pdf',note:'타이어 제조사 기술 자료 · 하중 민감도와 결합 슬립'},
    {id:'tire',title:'Michelin Engineering & Services — Tire',url:'https://engineering-and-services.michelin.com/mesures-et-modeles/tire/',note:'슬립각과 타이어 힘'},
    {id:'circle',title:'Motorsport UK — Grip Circle',url:'https://motorsportuk.tv/videos/grip-circle-and-driving-on-the-edge/',note:'제동·가속·선회의 힘 배분'},
    {id:'under',title:'Driver61 — Understeer',url:'https://driver61.com/uni/understeer/',note:'앞축 한계와 원인 진단'},
    {id:'over',title:'Driver61 — Oversteer',url:'https://driver61.com/uni/oversteer/',note:'진입·리프트·가속 오버스티어'},
    {id:'vision',title:'Driver61 — Vision on Track',url:'https://driver61.com/uni/vision/',note:'시선을 다음 구간으로 이동하기'},
    {id:'line',title:'Driver61 — Racing Line',url:'https://driver61.com/uni/racing-line/',note:'진입·에이펙스·출구와 다음 구간의 관계'},
    {id:'trail',title:'Driver61 — Trail Braking',url:'https://driver61.com/uni/trail-braking/',note:'선회하며 제동을 해제하는 과정'},
    {id:'data',title:'RACELOGIC — Circuit Tools Manual',url:'https://www.racelogic.co.uk/_downloads/vbox/Manuals/Software/CircuitTools_Software_Manual-EN.pdf',note:'같은 위치의 속도와 시간 차이 비교'},
    {id:'rallyschool',title:'Team O’Neil — Rally School Curriculum',url:'https://teamoneil.com/compare-rally-school-days/',note:'하중 이동·스키드 교정·노면 읽기의 학습 순서'},
    {id:'leftfoot',title:'Team O’Neil — Left Foot Braking',url:'https://teamoneil.com/blog/left-foot-braking-explained/',note:'왼발 제동 교육'},
    {id:'pendulum',title:'Team O’Neil — Pendulum Turn',url:'https://teamoneil.com/blog/howtodoapendulumturn/',note:'펜듈럼 턴 교육'},
    {id:'driftstudy',title:'Stanford Dynamic Design Lab — Dynamics and Control of Drifting',url:'https://ddl.stanford.edu/publications/thesis/dynamics-and-control-drifting-automobiles',note:'큰 차체 슬립에서 속도·회전·경로의 관계'},
    {id:'toyota',title:'Toyota — 1983 Corolla Levin archive',url:'https://www.toyota-global.com/company/history_of_toyota/75years/vehicle_lineage/car/id60003763/index.html',note:'순정 AE86 대표 사양, 차종별 차이'},
    {id:'hyundai',title:'Hyundai Motorsport — 2025 Rally1 specifications',url:'https://motorsport.hyundai.com/Corporate%20Site/Car%20Spec/i20%20N%20Rally1/2025/Hyundai%20Rally1%20Tech%20Spec.pdf',note:'2025 비하이브리드 Rally1의 4WD·변속기 사양'},
    {id:'lmuabs',title:'Le Mans Ultimate — LMGT3 ABS',url:'https://guide.lemansultimate.com/hc/en-gb/articles/13211078435983-ABS-Anti-Lock-Braking-System-for-LMGT3-Cars',note:'차량 내장 ABS와 게임 보조의 구분'},
    {id:'lmutc',title:'Le Mans Ultimate — Traction Control',url:'https://guide.lemansultimate.com/hc/en-gb/articles/13182869047311-How-do-I-configure-my-traction-control-in-Le-Mans-Ultimate',note:'TC·Power Cut·Slip Angle의 구분'},
    {id:'aero',title:'Mercedes-AMG F1 — Downforce Explained',url:'https://www.mercedesamgf1.com/news/feature-downforce-in-formula-one-explained',note:'공력 하중, 속도·기류 조건의 영향'},
    {id:'dirtyair',title:'Formula 1 — Glossary: Turbulence',url:'https://www.formula1.com/en/latest/article/f1-glossary-p-t.GBhLQtpeu9b8LrK5VM0F1',note:'다른 차를 따라갈 때 기류가 미치는 영향'},
    {id:'irating',title:'iRacing — What is iRating?',url:'https://iracing.freshdesk.com/support/solutions/articles/31000133523-what-is-irating',note:'상대와의 경기 결과로 변하는 매칭 지표'}
  ];
  function lesson(category,id,title,summary,prerequisites,paragraphs,steps,signs,errors,practice,pass,preset,vehicle,sourceIds,scope) {
    return {category,id,title,summary,prerequisites,paragraphs,steps,signs,errors,practice,pass,preset,vehicle,sourceIds,scope:scope || '개념 실험에서는 입력·앞뒤 힘·경로의 관계를 비교합니다. 차량별 감각과 실제 합격 여부는 아래 실제 심 실습에서 확인합니다.'};
  }
  const lessons = [
    lesson('foundation','load-grip','01 · 하중이 늘면 접지도 늘까?',
      '타이어가 낼 수 있는 힘과 내가 요구하는 힘을 분리합니다.',[],[
        '타이어가 차를 떠받치는 수직 힘을 하중이라고 합니다. 제동하면 일반적으로 앞축 하중이 늘고 뒤축 하중이 줄며, 가속하면 그 반대가 됩니다. 차체가 눈에 띄게 기울지 않아도 하중 이동은 생깁니다. 하중이 증가하면 한 타이어가 낼 수 있는 힘은 대체로 커지지만, 하중 증가와 같은 비율로 커지는 것은 아닙니다.',
        '접지는 무한한 자원이 아닙니다. 같은 타이어가 제동과 선회를 동시에 맡으면 두 방향의 요구가 한계를 공유합니다. 그래서 브레이크로 앞을 눌렀다고 더 세게 꺾어도 되는 것은 아닙니다. 마찰원은 이 공유를 이해하는 그림이며, 실제 한계는 완벽한 원도 고정된 숫자도 아닙니다.'
      ],['GT3와 직선 제동 코스를 선택하고 가속 없이 제동량만 바꿉니다. 앞뒤 하중이 움직이는 방향을 먼저 예상합니다.','초기 조건을 되돌리고 같은 속도에서 약한 조향을 넣습니다. 이번에는 제동을 조금씩 더합니다.','앞축 하중과 앞축 포화를 동시에 관찰합니다. 하중은 늘었는데 경로가 벌어지는 시도가 있는지 비교합니다.'],
      ['앞 하중 증가와 앞 포화가 동시에 나타날 수 있습니다.','핸들을 더 꺾어도 경로가 좁아지지 않으면 요구가 한계에 가까운지 봅니다.'],
      ['앞 하중 증가를 앞 접지 여유 증가와 같은 말로 쓰기.','타이어를 많이 미끄러뜨릴수록 항상 빨라진다고 생각하기.'],
      'LMU GT3로 한 느린 코너를 고릅니다. 같은 연료·셋업·타이어 준비 과정으로 3회씩 달려 제동을 끝낸 시도와 조금 남긴 시도의 출구 위치를 기록합니다.',
      '앞·뒤 중 어느 축이 먼저 한계에 닿았는지 설명하고, 자기 첫 세트보다 경로 이탈을 줄인 세트를 재현합니다.','brake','gt3',['grip','tire','circle']),
    lesson('foundation','slip-yaw','02 · 미끄러지는 방향과 도는 속도',
      '슬립각 α, 차체 슬립각 β, 요레이트를 구별합니다.',['load-grip'],[
        '바퀴가 향한 방향과 그 바퀴 접지점이 움직이는 방향의 차이가 타이어 슬립각 α입니다. 작은 슬립각은 정상적인 코너링 힘을 만드는 과정에도 필요합니다. 차체가 향한 방향과 차량 중심이 실제 이동하는 방향의 차이인 β와는 다릅니다.',
        '요레이트는 위에서 보았을 때 차체가 얼마나 빠르게 도는지입니다. 차가 옆으로 크게 미끄러져도 회전이 줄고 있을 수 있고, 지금 각도는 작아도 빠르게 스핀으로 진행할 수 있습니다. 각도 한 장면과 각도의 변화 속도를 함께 보아야 회복 입력을 결정할 수 있습니다.'
      ],['낮은 속도로 일정한 원을 돌며 차체 방향과 이동 방향 화살표를 비교합니다.','같은 시작 조건에서 조향을 더 빠르게 넣고 요레이트가 변하는 순서를 봅니다.','작은 뒤 슬립이 생기면 구동 요구와 조향을 조절해 회전을 줄입니다. 회전이 멎는 동안 카운터도 회수합니다.'],
      ['β가 작아져도 경로가 원하는 출구를 향하는지 따로 확인합니다.','카운터를 유지한 채 접지가 돌아오면 반대쪽 회전이 커질 수 있습니다.'],
      ['차체 각도만 보고 드리프트가 안정되었다고 판단하기.','슬립각이 0이어야 가장 잘 돈다고 생각하기.'],
      'AC의 넓은 평지에서 작은 슬립만 만들고 회복합니다. 리플레이에서 처음 회전한 순간, 카운터를 시작한 순간, 회수한 순간을 표시합니다.',
      '자기 첫 5회보다 반대쪽으로 튕기는 횟수를 줄이고, 각도와 회전 속도가 다른 값임을 설명합니다.','playground','ae86',['tire','driftstudy','over']),
    lesson('foundation','layout-drive','03 · 구동 방식과 엔진 위치는 다르다',
      'FWD·RWD·AWD와 앞·중앙·뒤 엔진을 두 축으로 이해합니다.',['load-grip'],[
        '구동 방식은 어느 바퀴가 엔진 힘을 노면에 전달하는지입니다. FWD는 앞바퀴가 조향과 구동을 함께 맡고, RWD는 뒤바퀴가 구동하며, AWD는 앞뒤가 나눕니다. 모두 제동·선회와 힘을 공유하며, AWD라는 이유만으로 더 짧게 멈추는 것은 아닙니다.',
        '엔진 위치는 질량이 놓인 위치에 관한 분류입니다. 정적 앞뒤 하중과 회전 관성에 영향을 주지만 차의 성격을 혼자 결정하지는 않습니다. 앞 엔진 후륜구동, 중앙 엔진 후륜구동, 뒤 엔진 후륜구동은 같은 RWD여도 다르게 느껴질 수 있습니다. 타이어·디퍼렌셜·공력·셋업도 함께 작용합니다.'
      ],['같은 코너와 낮은 진입 속도를 정하고 GT3·전륜 투어링·Rally1 원형을 번갈아 선택합니다.','선회 중 가속을 조금씩 늘리며 먼저 포화되는 축과 경로를 기록합니다.','중앙·뒤 엔진 원형도 비교합니다. 각 프로파일은 여러 변수가 함께 달라지므로 엔진 위치 하나의 인과 실험으로 해석하지 않습니다.'],
      ['FWD에서 가속과 동시에 앞이 밀리면 앞축의 구동·선회 요구를 확인합니다.','RWD에서 가속과 함께 뒤가 돌면 뒤축 요구와 입력 변화 속도를 봅니다.'],
      ['앞 엔진을 전륜구동과 같은 뜻으로 쓰기.','AWD는 항상 언더, 중앙 엔진은 항상 스핀이라고 단정하기.'],
      'AMS2·AC 등 보유한 심에서 앞 엔진 RWD와 다른 배치 한 대를 고릅니다. 각 차 기본 셋업에서 천천히 적응한 뒤 진입·중간·탈출 중 차이가 큰 구간을 기록합니다.',
      '구동축과 엔진 위치를 각각 말하고, 거동 차이에 대한 원인 후보를 두 개 이상 남깁니다.','corner','touring_fwd',['grip','toyota','hyundai']),
    lesson('foundation','vision-markers','04 · 시선과 기준점 만들기',
      '내 차 앞 대신 곧 도착할 위치를 읽습니다.',['slip-yaw'],[
        '시선은 페달과 핸들보다 먼저 다음 구간을 준비합니다. 제동 중에는 진입과 안쪽 접점, 코너 중에는 출구, 출구에서는 다음 코너를 찾습니다. 한 점을 오래 노려보지 말고 경로 전체와 주변 상황을 함께 읽습니다.',
        '기준점은 반복을 돕는 위치 표시입니다. 제동 시작, 브레이크 해제, 조향 시작, 안쪽 접점, 출구 목표를 구분하면 어디가 바뀌었는지 알 수 있습니다. 가려질 수 있는 차나 움직이는 그림자보다 연석·표지판 같은 환경 요소를 사용하되, 날씨와 교통에 따라 여유를 조정합니다.'
      ],['코너를 천천히 지나며 진입·안쪽 접점·출구 목표를 말로 지정합니다.','한 번에 제동 기준점 하나만 바꾸고 출구까지의 경로가 어떻게 달라졌는지 확인합니다.','Monza 또는 Nürburgring GP 평면 근사에서 다음 코너 방향을 미리 읽어 봅니다. 실제 표지판과 높이 정보는 실제 심에서 다시 찾습니다.'],
      ['에이펙스를 지나서야 출구를 보면 스로틀과 조향 결정이 늦어질 수 있습니다.','앞차를 따라 보느라 기준점을 잃으면 제동이 흔들립니다.'],
      ['눈앞 연석이나 델타 숫자만 계속 보기.','다른 차의 제동등을 내 유일한 기준점으로 삼기.'],
      'LMU에서 한 코너를 골라 “제동—안쪽—출구”를 소리 내며 5회 달립니다. 리플레이에서 기준점을 지나친 횟수와 추가 조향 횟수를 비교합니다.',
      '기준점 3개를 설명하고 자기 첫 세트보다 늦은 수정 입력과 경로 편차를 줄입니다.','monza','gt3',['vision','line'],
      '이 화면은 위에서 보는 평면 개념 실험입니다. 실제 운전석 시야·FOV·고저차·연석·표지판 학습은 실제 심에서 진행합니다.'),
    lesson('racing','corner-phases','05 · 코너를 네 구간으로 나누기',
      '감속 → 진입 → 중간 → 탈출의 연결을 읽습니다.',['load-grip','vision-markers'],[
        '감속 구간에서는 다음 곡률에 맞는 속도를 준비하고, 진입에서는 브레이크를 풀며 방향을 바꿉니다. 중간 구간에서는 남은 회전을 만들고, 탈출에서는 조향을 풀면서 가속합니다. 네 단계는 분석을 위한 구분이며 실제 입력은 부드럽게 겹칠 수 있습니다.',
        '에이펙스는 코너 안쪽에 가장 가까워지는 지점입니다. 가장 늦게 브레이크를 밟거나 에이펙스 속도가 가장 높다는 이유만으로 전체가 빠르지는 않습니다. 다음 직선·연속 코너까지 포함해 라인을 고르면 입구에서 조금 양보한 것이 출구에서 이득이 될 수 있습니다.'
      ],['넉넉히 감속해 네 구간을 분리하고 경로를 익힙니다.','초기 속도를 맞춘 채 조향 시작 위치만 조금 바꿉니다.','다음 직선의 같은 지점에서 속도와 시간을 비교합니다.'],
      ['중간에서 조향을 다시 늘리면 진입 속도 또는 너무 이른 접점을 점검합니다.','출구에서 반복해서 가속을 뗀다면 앞 단계의 경로가 막혔는지 봅니다.'],
      ['모든 코너에 늦은 에이펙스를 강요하기.','제동 지점만으로 한 코너의 성공을 판단하기.'],
      'LMU GT3 한 코너를 네 구간으로 메모합니다. 첫 5랩과 다음 5랩에서 출구 뒤 고정 지점의 속도와 유효랩 비율을 비교합니다.',
      '손실이 처음 시작한 구간을 지목하고 자기 기준보다 출구 성능 또는 재현성이 개선된 라인을 반복합니다.','corner','gt3',['line','circle','data']),
    lesson('racing','trail-release','06 · 트레일 브레이킹: 남기는 양보다 푸는 과정',
      '조향이 늘 때 제동 요구를 줄이는 감각을 배웁니다.',['corner-phases','load-grip'],[
        '트레일 브레이킹은 선회를 시작한 뒤에도 일부 제동을 이어가며 해제하는 과정입니다. 목적은 감속과 회전을 자연스럽게 연결하는 것입니다. 코너마다 필요한 양과 길이가 다르며 끝까지 브레이크를 남기는 기술을 뜻하지는 않습니다.',
        '제동은 앞뒤 하중을 바꾸는 동시에 접지 능력을 사용합니다. 너무 많이 남기면 앞이 제동과 선회를 함께 감당하지 못하거나, 가벼워진 뒤가 회전할 수 있습니다. 브레이크를 얼마나 늦게 밟았는지보다 조향이 증가하는 동안 어떻게 풀었는지 먼저 비교합니다.'
      ],['같은 진입 속도로 일찍 제동을 마친 기준 시도를 만듭니다.','다음에는 작은 제동을 선회 초반까지만 남기고 점진적으로 풉니다.','속도·앞뒤 포화·요레이트·출구 위치를 함께 기록하고 차이가 난 원인을 한 문장으로 적습니다.'],
      ['해제 직후 경로가 크게 벌어지면 해제 속도와 진입 속도를 함께 살핍니다.','뒤 회전이 갑자기 커지면 더 남기는 대신 요구를 줄이는 비교가 필요합니다.'],
      ['모든 코너에서 일정 비율의 브레이크를 유지하기.','입구 속도도 바꿔 놓고 해제 방식만의 효과라고 해석하기.'],
      'LMU GT3 느린 코너에서 제동 시작점을 고정합니다. 해제 구간만 바꿔 3회씩 비교하고 실제 ABS 개입·조향·출구 속도를 확인합니다.',
      '자기 기준보다 진입 속도 편차를 줄인 상태로 해제 방식의 효과를 3회 재현합니다.','corner','gt3',['trail','grip','lmuabs']),
    lesson('racing','balance-diagnosis','07 · 언더·오버스티어의 원인 찾기',
      '증상을 이름 붙인 뒤 원인 하나를 검증합니다.',['slip-yaw','trail-release'],[
        '원하는 만큼 차가 돌지 않고 앞이 바깥으로 밀리는 경향을 언더스티어, 뒤가 바깥으로 돌며 차체 회전이 과해지는 경향을 오버스티어라고 부릅니다. 이름은 증상을 말할 뿐 원인을 확정하지 않습니다. 먼저 진입·중간·탈출 중 언제 시작했는지 표시합니다.',
        '과속, 과한 조향, 강한 제동, 급한 리프트, 가속 요구, 연석과 노면 변화가 서로 다른 원인이 될 수 있습니다. 같은 코너에서 같은 위치의 속도·페달·조향을 함께 보아야 합니다. 셋업은 입력과 조건을 반복해도 남는 문제를 설명할 때 다룹니다.'
      ],['같은 코너에서 기준 시도를 저장하고 문제가 처음 생긴 위치를 찾습니다.','진입 속도만 낮춘 시도와 조향 증가 속도만 낮춘 시도를 따로 만듭니다.','가설과 결과가 맞지 않으면 다음 원인 후보를 적고 다시 한 변수만 바꿉니다.'],
      ['앞 슬립각 증가와 경로 벌어짐이 함께 나타나는지 확인합니다.','뒤 포화와 요레이트 증가가 가속·리프트·제동 중 어느 입력 뒤에 오는지 봅니다.'],
      ['언더가 나면 무조건 더 꺾기.','스핀 한 번으로 차의 셋업 전체를 바꾸기.'],
      '실제 심 리플레이 한 장면을 골라 “언제—증상—원인 후보—다음 비교” 네 줄로 적습니다. 같은 조건으로 비교 시도를 만들어 가설을 확인합니다.',
      '자기 첫 진단보다 원인을 좁히고, 수정 입력으로 같은 증상 빈도가 줄어드는지 확인합니다.','corner','gt3',['under','over','grip']),
    lesson('racing','exit-throttle','08 · 탈출 가속: 빨리 밟기보다 계속 밟기',
      '경로를 열면서 구동 요구를 늘립니다.',['corner-phases','balance-diagnosis'],[
        '타이어는 선회와 구동을 동시에 감당합니다. 코너에서 아직 큰 회전이 필요한데 가속을 급하게 늘리면 앞이 밀리거나 뒤가 미끄러질 수 있습니다. 조향을 풀 수 있는 경로를 먼저 만들고 속도·각도·접지 여유에 맞춰 가속을 늘립니다.',
        '스로틀을 처음 밟은 지점만 앞당기는 것은 목표가 아닙니다. 일찍 밟았다가 다시 떼면 출구 속도가 오히려 나빠질 수 있습니다. TC는 차가 가진 제어 장치이며 모든 문제를 지우지는 않습니다. 실제 LMU의 TC, Power Cut, Slip Angle은 서로 다른 항목이므로 하나씩 확인합니다.'
      ],['같은 중간 속도에서 완만하게 가속하는 기준 시도를 만듭니다.','가속 시작만 앞당긴 시도를 만들고 다시 뗀 횟수를 셉니다.','조향을 더 자연스럽게 풀 수 있도록 진입 경로를 수정하고 출구 뒤 같은 위치의 속도를 비교합니다.'],
      ['초반 가속 후 재차 리프트하면 라인과 가속 증가 속도를 점검합니다.','뒤 포화가 커지는데 속도가 덜 늘면 구동 요구가 유효 가속으로 이어지는지 확인합니다.'],
      ['TC를 모두 끄는 것을 무조건 실력 향상으로 보기.','풀스로틀에 도달한 순간만 비교하고 직선의 속도를 무시하기.'],
      'LMU GT3에서 기본 전자장치를 유지하고 5랩씩 비교합니다. 출구 뒤 고정 지점 속도와 가속 재해제 횟수를 기록한 뒤 필요할 때만 TC 항목 하나를 바꿉니다.',
      '자기 첫 세트보다 재해제 횟수와 출구 속도 편차를 줄이면서 유효랩 비율을 유지합니다.','hairpin','gt3',['grip','line','lmutc']),
    lesson('racing','telemetry-distance','09 · 텔레메트리: 같은 장소끼리 비교하기',
      '델타가 벌어진 곳에서 한 단계 거슬러 올라갑니다.',['corner-phases'],[
        '텔레메트리는 속도·페달·조향 같은 상태를 기록한 데이터입니다. 두 랩의 같은 시각을 비교하면 서로 다른 코너에 있을 수 있습니다. 트랙 거리나 동일한 위치를 기준으로 맞춘 뒤 속도와 입력을 나란히 읽어야 합니다.',
        '델타는 기준 랩과의 누적 시간 차이입니다. 델타가 증가한 구간은 손실이 쌓인 장소지만 원인은 그보다 앞의 라인과 진입 속도일 수 있습니다. 최속랩 하나만 고르지 말고 비슷한 조건의 유효랩 여러 개에서 반복되는 차이를 찾습니다.'
      ],['한 코너를 같은 조건으로 여러 번 달리고 기준 시도를 정합니다.','시간 그래프만 보지 말고 같은 위치의 속도·브레이크·스로틀·조향을 비교합니다.','손실 시작 위치를 찾은 뒤 그 전의 제동 해제와 경로까지 확인합니다. 수정은 한 가지로 제한합니다.'],
      ['출구의 속도 차이가 직선 전체의 시간 차이로 이어질 수 있습니다.','브레이크 모양이 비슷해도 시작 속도와 라인이 다르면 결과가 달라집니다.'],
      ['빠른 드라이버의 제동 지점을 조건 확인 없이 복사하기.','유효하지 않은 랩이나 연료가 다른 랩을 공정한 비교로 쓰기.'],
      '실제 심에서 사용할 수 있는 데이터 도구로 거리 축을 맞춥니다. 5개 유효랩의 한 코너만 비교해 손실 원인 가설 하나와 다음 세션 과제를 남깁니다.',
      '자기 기준보다 선택 구간의 시간 중앙값을 개선하거나 편차를 줄이고, 바뀐 입력과 연결해 설명합니다.','monza','gt3',['data']),
    lesson('racing','long-run','10 · 롱런: 타이어와 연료가 변해도 반복하기',
      '최속랩과 지속 가능한 페이스를 따로 봅니다.',['exit-throttle','telemetry-distance'],[
        '한 번 빠른 랩과 여러 랩을 비슷하게 완주하는 능력은 다릅니다. 실제 심에서는 타이어 상태, 연료, 날씨, 노면이 함께 달라집니다. 후반에 느려졌다는 사실만으로 타이어 마모 탓이라고 단정할 수는 없습니다.',
        '비교에는 준비 과정도 포함합니다. 시작 연료와 셋업, 날씨, 타이어 준비 랩을 기록하고 유효랩 비율·중앙값·최대와 최소 차이·큰 실수 횟수를 봅니다. 연료가 줄며 빨라지는 효과와 타이어 변화가 겹칠 수 있으므로 한 스틴트의 숫자를 단순하게 해석하지 않습니다.'
      ],['개념 실험에서는 같은 코너 5회 반복으로 입력 일관성만 확인합니다.','가장 빠른 시도 외에 중앙값과 범위를 적고 경로 이탈도 셉니다.','다음 세트에는 여유를 조금 늘려 시간 편차와 성공률이 어떻게 달라지는지 비교합니다.'],
      ['후반에 수정 조향이 늘면 피로·시선·타이어 상태를 구분해 확인합니다.','빠른 한 랩이 있어도 유효랩 비율이 낮아졌다면 지속 가능한 변화인지 다시 봅니다.'],
      ['다른 연료·날씨·타이어 상태를 같은 조건으로 취급하기.','손실을 만회하려 매 코너 한계를 더 넘기기.'],
      'LMU에서 10랩 또는 익숙해지면 20분 스틴트를 두 번 진행합니다. 같은 시작 조건으로 유효랩 비율·중앙값·편차·사고·연료 사용을 기록하고 앞뒤 구간을 나눠 비교합니다.',
      '자기 첫 스틴트 대비 큰 실수와 편차를 줄이면서 완주합니다. 개선이 없으면 길이를 줄이고 원인 하나부터 다시 확인합니다.','nurbgp','gt3',['grip','data'],
      '이 도구는 타이어 온도·압력·마모·연료 소모를 계산하지 않습니다. 롱런 상태 변화와 피트 전략은 실제 심 실습입니다.'),
    lesson('racing','traffic-racecraft','11 · 교통 속에서 내 경로 남기기',
      '혼자 빠른 라인과 나란히 달리는 라인을 구분합니다.',['vision-markers','long-run'],[
        '다른 차가 있으면 평소의 폭과 제동 지점을 그대로 사용할 수 없습니다. 앞차만 보지 말고 원래 기준점과 옆 공간을 같이 확인합니다. 추월·방어에서는 내 진입뿐 아니라 상대가 지나갈 경로와 다음 코너까지 계획합니다.',
        '멀티클래스에서는 속도 차이가 크므로 서로 예측하기 쉬운 움직임이 중요합니다. 양보할 때도 갑자기 선을 바꾸기보다 선택한 경로를 일관되게 유지하고 확인합니다. 접촉 판정과 청색기 등 구체적인 의무는 참가 세션의 현행 규칙을 읽고 적용합니다.'
      ],['혼자 달리는 개념 실험에서 코너의 안쪽 절반과 바깥쪽 절반만 쓰는 경로를 각각 만듭니다.','좁아진 경로에서는 진입 속도와 제동 준비를 얼마나 바꿔야 하는지 비교합니다.','실제 심으로 옮기기 전에 거울·스포터·상대 위치 표시와 피트 진입 조작을 확인합니다.'],
      ['앞차를 보느라 제동 기준점을 놓치면 시선을 분배해야 합니다.','나란히 있을 때 평소 출구 끝까지 쓰려 한다면 상대 공간을 다시 확인합니다.'],
      ['빈틈이 보였다는 이유만으로 끝까지 추월을 강행하기.','오프라인이나 연석 위에서도 평소 제동 거리가 같다고 가정하기.'],
      'LMU AI 또는 연습 세션에서 10분 동안 추월 성공 수보다 접촉 없이 따라가기·양쪽 라인 완주·피트 진입을 연습합니다. 리플레이로 위험을 미리 줄일 수 있었던 지점을 하나 찾습니다.',
      '자기 첫 세션 대비 접촉·급회피·트랙 이탈을 줄이고, 상대 공간을 남긴 장면을 설명합니다.','nurbgp','gt3',['vision','line'],
      '개념 실험에는 다른 차·스포터·충돌 판정이 없습니다. 나란히 달리기와 규칙 적용은 실제 심에서 검증합니다.'),
    lesson('racing','gt3-hypercar','12 · GT3에서 다른 GT·하이퍼카로',
      '원리는 옮기고 제동·전자장치·공력은 다시 배웁니다.',['trail-release','long-run'],[
        'GT3에서 익힌 시선, 조건 고정, 제동 해제, 출구 분석은 다른 차에도 쓸 수 있습니다. 그러나 GT라는 이름만으로 제동 장치나 타이어가 같지는 않습니다. LMU 공식 안내는 LMGT3의 차량 내장 ABS와 다른 클래스에서 선택하는 게임 보조를 구분합니다.',
        '하이퍼카에서는 속도에 따른 공력 하중과 차종별 제동·구동 시스템을 새로 익혀야 합니다. 하이브리드 유무와 구동 방식도 차마다 다르므로 모든 하이퍼카를 AWD나 동일한 회생제동 차로 생각하지 마세요. 기본 설정에서 출발해 실제 차량 안내와 대시보드 표시를 먼저 확인합니다.'
      ],['GT3와 하이퍼카 원형을 같은 저속 코너에서 비교해 제동·조향 순서의 공통점을 찾습니다.','다른 속도 조건에서 비교하되 교육 모델의 수치를 실제 제동 지점으로 복사하지 않습니다.','실제 심에서는 첫 세션을 직선 제동과 짧은 코너 반복으로 나눠 잠김·회복·전자장치를 익힙니다.'],
      ['고속에서는 괜찮다가 감속 말기에 잠기면 속도 감소에 맞춘 압력 해제를 점검합니다.','GT3와 같은 페달 패턴이 통하지 않아도 원리 자체가 사라진 것은 아닙니다.'],
      ['빠른 차로 갈수록 무조건 더 늦게 제동하기.','차량 내장 TC·ABS와 게임의 운전 보조를 혼동하기.'],
      'LMU에서 익숙한 트랙과 하이퍼카 한 대를 고정합니다. 첫 5회는 일찍 제동하며 해제를 익히고, 기본 전자장치·회생/배터리 관련 안내를 확인한 뒤 한 설정씩 비교합니다.',
      '자기 첫 세트보다 제동 말기 실수와 이탈을 줄인 유효랩을 반복한 다음 롱런으로 넘어갑니다.','brake','hypercar',['lmuabs','lmutc','aero'],
      '이 하이퍼카 원형은 회생제동·배터리·하이브리드 배치·차량별 ABS/TC를 재현하지 않습니다. 제동과 공력의 관계만 개념적으로 비교합니다.'),
    lesson('rally','surface-notes','13 · 랠리: 노면과 페이스노트 읽기',
      '코너 하나보다 다음 두 구간을 준비합니다.',['load-grip','vision-markers'],[
        '랠리는 외운 서킷 라인만 반복하는 것과 다릅니다. 페이스노트는 코너 방향과 급격함, 길이, 위험과 연결을 미리 알려 주는 정보입니다. 숫자 체계는 게임과 노트 방식에 따라 다를 수 있으므로 현재 게임의 설명에서 뜻을 확인합니다.',
        '노면이 달라지면 같은 속도·조작의 결과도 달라집니다. 먼저 경로와 속도를 맞추고, 회전이 부족한 특정 상황에서만 슬라이드 기술을 추가합니다. 크게 옆으로 가는 모습 자체가 빠르거나 좋은 랠리 주행의 증거는 아닙니다.'
      ],['Rally1과 랠리 스테이지를 선택하고 낮은 속도로 다음 코너 방향을 미리 읽습니다.','노면 접지를 바꾼 뒤 먼저 같은 입력의 차이를 보고, 다음에는 진입 속도만 줄입니다.','연속 코너에서는 첫 코너 출구가 다음 진입을 얼마나 어렵게 만드는지 기록합니다.'],
      ['노트를 들은 뒤에도 눈앞 코너에서 허둥대면 속도를 낮춰 정보를 처리할 시간을 만듭니다.','긴 슬라이드 뒤 출구가 막히면 회전각보다 경로를 먼저 고칩니다.'],
      ['모든 코너를 핸드브레이크로 돌기.','코너 숫자만 듣고 길이·급격해짐·다음 방향을 놓치기.'],
      'EA WRC에서 짧은 스테이지·한 노면·날씨·Rally1의 연식을 고정합니다. 노트 호출 시점을 편하게 맞춘 뒤 3회 완주하며 리셋·큰 충돌·놓친 노트를 기록합니다.',
      '자기 첫 3회 대비 리셋과 큰 충돌을 줄이고, 어려웠던 연결 코너 두 곳의 노트를 설명합니다.','rally_stage','rally1',['rallyschool','hyundai','grip'],
      '평면 랠리 코스는 방향과 낮은 접지만 다룹니다. 요철·점프·노면 변형·오디오 페이스노트는 실제 심에서 학습합니다.'),
    lesson('rally','lift-leftfoot','14 · 리프트와 왼발 제동',
      '페달을 바꾸는 순간의 균형을 관찰합니다.',['surface-notes','balance-diagnosis'],[
        '리프트는 밟던 가속을 줄이는 것입니다. 감속과 구동력 변화로 차의 균형이 달라질 수 있지만, 리프트하면 항상 뒤가 도는 것은 아닙니다. 구동 방식, 속도, 타이어와 당시 입력에 따라 결과가 달라집니다.',
        '왼발 제동은 브레이크를 왼발로 조절하는 방법입니다. 단순히 두 페달을 동시에 끝까지 밟는 기술이 아닙니다. 랠리에서는 속도와 균형을 세밀하게 조절하는 데 쓰지만, 어느 발이든 필요한 힘을 정확히 내는 능력이 먼저입니다.'
      ],['낮은 접지의 같은 코너에서 가속을 서서히 줄인 시도와 갑자기 줄인 시도를 비교합니다.','같은 조건에서 작은 제동을 더하고 앞뒤 포화와 회전 변화를 봅니다.','각 시도에서 실제로 더 나은 출구 경로가 생겼는지 확인합니다. 회전이 늘었다는 사실만으로 성공 처리하지 않습니다.'],
      ['리프트 뒤 회전이 커지면 입력 변화 속도와 뒤축 여유를 확인합니다.','브레이크를 더했는데 앞이 밀리면 앞축의 결합 요구를 봅니다.'],
      ['엔진 위치와 관계없이 리프트가 같은 효과라고 생각하기.','왼발을 빨리 쓰는 것과 제동 압력을 정확히 만드는 것을 혼동하기.'],
      'EA WRC의 넓고 익숙한 저속 구간에서 먼저 직선 제동 압력을 반복합니다. 그다음 작은 선회 중 리프트와 가벼운 제동을 각각 비교하고 페달 중첩은 차량 반응을 확인하며 추가합니다.',
      '자기 첫 세트보다 목표 위치에서의 속도 편차와 의도치 않은 회전을 줄입니다.','slalom','rally1',['leftfoot','over','grip'],
      '이 도구의 페달 값은 왼발 감각을 훈련하지 않습니다. 엔진 브레이크·변속·디퍼렌셜의 실제 반응은 별도 모델링하지 않습니다.'),
    lesson('rally','pendulum','15 · 펜듈럼: 반대쪽 준비와 회수',
      '스칸디나비안 플릭을 하나의 연결된 움직임으로 봅니다.',['lift-leftfoot','slip-yaw'],[
        '펜듈럼 턴은 코너 반대쪽으로 짧게 준비한 뒤 방향을 바꾸며 회전을 유도하는 기술입니다. 목적은 좁은 코너의 출구를 향하도록 차를 준비하는 것입니다. 크게 흔들수록 좋은 기술이 아니며, 사용할 폭과 필요한 회전이 있어야 합니다.',
        '준비, 방향 전환, 회전 억제, 탈출은 연결되어 있습니다. 반대 조향만 크게 넣으면 입구를 놓치거나 과한 스핀을 만들 수 있습니다. 단순히 “반대—정방향”을 외우는 대신 언제 회전이 생겼고 언제 줄여야 하는지 관찰합니다.'
      ],['넓은 헤어핀에서 먼저 보통의 감속·선회로 기준 경로를 만듭니다.','낮은 속도로 아주 작은 반대 방향 준비를 넣고 방향 전환 시점을 비교합니다.','회전이 생기면 가속·조향으로 억제하며 출구로 이어 갑니다. 준비 폭은 한 번에 조금만 바꿉니다.'],
      ['준비 동작 중 도로 폭을 다 쓰면 더 빠른 전환보다 작은 준비와 낮은 진입 속도가 필요할 수 있습니다.','회전은 충분한데 출구를 지나치면 억제와 카운터 회수 시점을 봅니다.'],
      ['모든 코너에서 플릭을 쓰기.','준비 조향만 연습하고 회전 회복을 생략하기.'],
      'EA WRC에서 탈출 공간이 넓은 같은 헤어핀을 고정합니다. 보통 회전과 작은 펜듈럼을 각각 3회 비교하며 리셋·출구 위치·통과 시간을 함께 기록합니다.',
      '자기 기준보다 리셋을 늘리지 않고 출구 경로를 반복합니다. 효과가 없으면 보통 회전을 선택할 수 있어야 합니다.','hairpin','rally1',['pendulum','rallyschool','driftstudy']),
    lesson('rally','rally-handbrake','16 · 랠리 핸드브레이크와 클러치의 역할',
      '뒤축 회전 보조와 구동 연결을 구분합니다.',['pendulum'],[
        '핸드브레이크는 좁은 코너에서 뒤바퀴의 회전을 억제해 차량 회전을 돕는 데 쓰일 수 있습니다. 하지만 속도와 경로 준비 없이 당기면 정지하거나 바깥으로 미끄러질 수 있습니다. 먼저 감속과 조향으로 가능한 회전을 만들고 필요한 경우만 추가합니다.',
        '클러치는 엔진과 구동계의 연결을 조절합니다. AWD에서 핸드브레이크를 사용할 때 구동계 분리 방식과 필요한 클러치 조작은 차마다 다릅니다. Rally1이라는 이름만으로 공통 페달 순서를 정하지 말고 실제 심의 차량 사양과 조작 안내를 확인합니다.'
      ],['낮은 속도로 헤어핀의 기준 경로를 만듭니다.','개념 실험에서 짧은 핸드브레이크 입력을 추가해 뒤 포화와 경로 변화를 봅니다.','클러치를 따로 눌러 구동 토크가 끊기는 효과를 확인합니다. 두 조작의 목적을 말로 구분합니다.'],
      ['큰 각도 뒤 차가 멈추면 과한 속도 손실과 긴 입력 시간을 점검합니다.','회전 후 출구 방향이 맞아도 가속 연결이 늦으면 전체 시간이 손해일 수 있습니다.'],
      ['사륜구동이면 무조건 클러치를 밟거나 무조건 필요 없다고 단정하기.','이 도구의 뒤 포화를 실제 바퀴 잠김으로 해석하기.'],
      'EA WRC 선택 차량의 클러치·핸드브레이크 동작을 먼저 확인합니다. 같은 헤어핀에서 미사용과 짧은 사용을 비교하고 큰 충돌 없이 출구로 가속하는 과정까지 연습합니다.',
      '자기 기준보다 출구 경로를 반복하고, 필요 없는 코너에서는 사용하지 않는 판단을 설명합니다.','hairpin','rally1',['rallyschool','hyundai','grip'],
      '이 도구의 핸드브레이크는 뒤축 제동 요구와 마찰 포화만 계산하며 바퀴 잠김을 재현하지 않습니다. 클러치는 구동 토크 차단만 하며 차량별 구동계 분리 기구는 없습니다.'),
    lesson('drift','ae86-baseline','17 · AE86: 사양과 낮은 출력부터',
      '순정급 차와 튜닝 드리프트차를 같은 기준으로 보지 않습니다.',['layout-drive','slip-yaw'],[
        'AE86은 앞 엔진 후륜구동이지만 이름만으로 모든 사양이 같지는 않습니다. Toyota의 1983년 3도어 GTV 대표 제원은 940 kg와 130 PS gross입니다. 이는 현대식 net 출력이나 휠 출력, AC 모드 차량의 성능을 뜻하지 않습니다.',
        '낮은 출력의 차에서는 가속만 더해 모든 반경과 속도의 드리프트를 유지할 수 없습니다. 속도를 너무 잃으면 회복이 어렵고, 너무 큰 각도는 더 많은 에너지를 소모합니다. 먼저 순정/튜닝, 타이어, LSD, 조향각, 변속 사양을 확인하고 같은 차로 반복합니다.'
      ],['AE86 원형으로 큰 원을 그립 주행합니다. 원하는 반경과 속도를 먼저 유지합니다.','가속을 조금씩 늘려 뒤 포화가 시작되는 조건을 찾습니다. 유지되지 않으면 각도 욕심보다 속도·반경을 다시 봅니다.','같은 조건에서 작은 슬립 후 곧바로 회복하는 시도를 만듭니다.'],
      ['회전은 생겼지만 속도가 계속 줄면 지속 가능한 상태와 다릅니다.','풀스로틀에도 각도가 사라진다면 사양과 조건상 지속이 어려울 수 있습니다.'],
      ['고출력 튜닝차 영상의 페달을 순정 AE86에 그대로 적용하기.','모드 이름만 보고 타이어·LSD·출력이 원본과 같다고 가정하기.'],
      'AC에서 사용할 AE86 사양 하나와 평지 스키드패드를 고정합니다. 먼저 그립 원 3회, 작은 슬립과 회복 5회를 반복하고 속도 손실을 기록합니다.',
      '자기 첫 세트보다 원의 반경 편차와 회복 중 스핀을 줄이고, 선택한 차량 사양을 설명합니다.','drift','ae86',['toyota','driftstudy'],
      '교육 원형의 유효 출력·타이어·LSD·조향 특성은 Toyota 검증값이 아닙니다. 실제 RPM·기어비·클러치 관성도 계산하지 않습니다.'),
    lesson('drift','initiate-recover','18 · 슬립을 만들고 바로 회복하기',
      '드리프트 유지보다 회전 억제를 먼저 익힙니다.',['ae86-baseline'],[
        '드리프트 시작은 뒤축이 가진 횡방향 여유와 구동 요구의 균형을 바꾸는 과정입니다. 시작 방법이 무엇이든 차체 방향과 실제 이동 방향이 벌어진 뒤에는 회전이 더 커지는지 줄어드는지 읽어야 합니다.',
        '카운터스티어는 미끄러짐을 한 번에 지우는 버튼이 아닙니다. 필요한 양과 속도는 현재 슬립과 회전에 달렸고, 접지가 돌아오면 카운터도 회수해야 합니다. 급격한 페달 변화가 균형을 다시 바꿀 수 있으므로 스로틀을 무조건 모두 떼는 규칙으로 외우지 않습니다.'
      ],['넓은 원에서 작고 짧은 뒤 슬립만 유도합니다.','회전이 늘어나는 순간 구동 요구와 조향을 조절해 더 커지지 않도록 합니다.','회전이 줄어들면 카운터를 회수해 원래 경로로 돌아옵니다. 다음 시도는 초기 조건을 맞춥니다.'],
      ['카운터를 넣어도 회전이 빨라지면 이미 생긴 회전과 입력 시점을 점검합니다.','반대쪽으로 튕기면 회전이 줄어든 뒤 카운터를 남겼는지 봅니다.'],
      ['최대 카운터를 오래 고정하기.','실패할 때마다 더 큰 각도와 더 높은 속도로 다시 시도하기.'],
      'AC 평지에서 시작—억제—회수의 세 순간만 연습합니다. 휠 복원을 느끼되 손을 놓는 동작을 성공 기준으로 삼지 말고 리플레이로 입력을 확인합니다.',
      '자기 첫 5회보다 스핀과 반대쪽 튕김 횟수를 줄여 3회 연속 회복합니다.','playground','ae86',['over','driftstudy']),
    lesson('drift','donut','19 · 원돌이: 각도·반경·속도 맞추기',
      '한 번 회전에서 같은 원을 유지하는 단계로 갑니다.',['initiate-recover'],[
        '원돌이는 큰 각도만 만드는 기술이 아닙니다. 같은 중심을 도는 동안 속도, 회전 속도, 차체 슬립각이 서로 맞아야 합니다. 가속과 조향은 모두 반경과 각도에 영향을 주므로 “가속은 각도, 핸들은 반경”처럼 완전히 분리된 규칙으로 외우지 않습니다.',
        '원 중심이 계속 이동하거나 반경이 넓어지는 것을 관찰하면 입력을 얼마나 바꿔야 하는지 알 수 있습니다. 순정급 AE86에서는 선택한 노면과 반경에서 지속이 어려울 수 있으므로 긴 시간보다 작은 각도의 안정된 구간부터 만듭니다.'
      ],['큰 그립 원에서 일정 반경을 만든 후 작은 슬립을 시작합니다.','원 중심과 출구 경로를 보며 입력을 작게 조절합니다. 시선은 차 앞 범퍼에 고정하지 않습니다.','한 바퀴를 무리하게 완성하기보다 반원 유지 후 회복을 반복하고 가능한 구간을 늘립니다.'],
      ['속도가 줄며 원이 무너지면 각도를 줄이거나 지속 가능한 반경·노면인지 확인합니다.','원이 갑자기 좁아지며 회전하면 요레이트가 먼저 증가했는지 봅니다.'],
      ['각도 숫자만 유지하고 원 중심의 이동을 무시하기.','계속 풀스로틀이면 원이 유지될 것이라고 생각하기.'],
      'AC 스키드패드에서 일정한 반경의 표식을 사용합니다. 좌·우 각각 반원부터 한 원까지 늘리며 유지 구간, 경계 이탈, 회복 성공을 기록합니다.',
      '자기 첫 세트보다 같은 원 안에 머무는 구간을 늘리고 종료 후 의도적으로 그립을 회복합니다.','drift','ae86',['driftstudy','toyota']),
    lesson('drift','figure-eight','20 · 8자: 전환과 카운터 회수',
      '첫 원의 마무리가 다음 원의 시작을 만듭니다.',['donut'],[
        '8자에서는 한쪽 슬립을 줄인 뒤 반대쪽 슬립으로 연결합니다. 차체 각도가 0을 지났다고 회전이 사라진 것은 아닙니다. 방향이 바뀌는 동안 남은 요 운동 때문에 예상보다 빨리 반대쪽으로 돌아갈 수 있습니다.',
        '전환 직전의 속도와 경로가 다음 원을 결정합니다. 큰 각도와 급한 핸들만으로 연결하려 하지 말고, 첫 원에서 전환 공간을 만들고 가속·조향의 변화 시점을 맞춥니다. 익숙해지기 전에는 가운데에서 잠깐 그립을 회복해도 됩니다.'
      ],['좌·우 원을 따로 반복한 뒤 가운데를 직선으로 연결합니다.','첫 원의 슬립을 줄이며 카운터를 회수하고 차체 회전이 바뀌는 시점을 봅니다.','다음 원으로 작은 슬립을 유도합니다. 전환 속도를 고정한 뒤 한 입력의 시점만 바꿉니다.'],
      ['가운데를 지나 반대쪽으로 급히 스핀하면 남은 회전과 늦은 카운터 회수를 확인합니다.','연결할 때마다 속도가 크게 줄면 두 원의 반경과 전환 경로를 먼저 봅니다.'],
      ['반대쪽 조향을 빨리 넣는 것만이 전환이라고 생각하기.','두 방향의 원돌이가 불안정한데 연속 전환부터 시도하기.'],
      'AC의 평지 8자 구간에서 원—그립 회복—반대 원으로 시작합니다. 이후 회복 구간을 조금씩 줄이고 5회 중 통제된 전환 비율을 기록합니다.',
      '자기 기준보다 전환 성공률을 높이고, 좌우 양방향에서 종료 후 회복할 수 있습니다.','figure8','ae86',['driftstudy','over']),
    lesson('drift','continuous-touge','21 · 연속 드리프트와 가상 토게',
      '유지 시간보다 다음 구간에 도착하는 위치를 봅니다.',['figure-eight','vision-markers'],[
        '연속 주행은 원돌이를 길게 늘린 것과 다릅니다. 코너마다 반경과 연결이 달라져 어떤 구간에서는 각도를 줄이거나 잠시 그립을 회복해야 다음 코너를 준비할 수 있습니다. 유지가 끊기지 않는 모습보다 경로를 통제하는 것이 먼저입니다.',
        '낮은 출력의 AE86에서는 불필요한 큰 슬립이 속도를 잃게 만듭니다. 가상 토게의 좁은 길에서는 회복 공간도 작으므로 평지에서 검증한 속도·각도보다 여유를 두고 시작합니다. 실제 도로가 아닌 시뮬레이터 안의 연습을 뜻합니다.'
      ],['평지 연속 코너에서 그립 라인을 먼저 3회 반복합니다.','첫 코너에 작은 슬립을 넣고 다음 진입 위치가 유지되는지 봅니다.','구간을 하나씩 연결합니다. 경로를 잃으면 전 구간 재도전보다 해당 연결만 낮은 속도로 반복합니다.'],
      ['각도는 유지되지만 다음 코너의 바깥쪽을 잃으면 첫 코너 종료 위치를 수정합니다.','속도를 살리려고 가속을 늘렸는데 경계로 가면 필요한 회전과 경로를 다시 봅니다.'],
      ['모든 코너를 최대 각도로 이어야 성공이라고 보기.','평면 근사 코스의 속도를 실제 고저차 코스에 복사하기.'],
      'AC에서 먼저 평지 연속 코스를 연습한 뒤 가상 토게 한 짧은 구간을 고릅니다. 그립 완주 기준을 만든 후 작은 슬립을 추가하고 충돌·리셋·의도적 회복을 기록합니다.',
      '자기 첫 세트보다 충돌과 리셋을 줄인 연결 구간을 늘리고, 필요할 때 그립으로 돌아옵니다.','touge','ae86',['driftstudy','vision','toyota'],
      '이 토게는 평면 경로 연습용입니다. 경사·캠버·노면 요철·점프·차체 접촉과 실제 도로 조건을 재현하지 않습니다.'),
    lesson('drift','clutch-handbrake','22 · 클러치킥과 핸드브레이크 구분하기',
      '시작 기술을 유지·회복 기술 위에 더합니다.',['initiate-recover','figure-eight'],[
        '클러치킥은 엔진과 구동계 연결을 끊었다 다시 결합하는 과정에서 뒤타이어에 순간적인 구동 요구를 주는 시작·보조 기술입니다. 효과는 RPM, 기어, 관성, 클러치와 디퍼렌셜에 달려 있습니다. 단순히 클러치를 누르는 것만으로 같은 효과가 생기지는 않습니다.',
        '핸드브레이크는 뒤쪽 제동을 이용해 슬립과 회전을 유도하며 클러치킥과 힘의 방향이 다릅니다. 두 방법 모두 시작 뒤의 속도·반경·카운터 제어를 대신하지 못합니다. 기존의 작은 슬립과 회복이 안정된 다음 실제 심에서 각각 따로 연습합니다.'
      ],['개념 실험에서 클러치만 눌러 구동 토크가 사라지는 것을 확인합니다. 이것을 클러치킥 성공으로 세지 않습니다.','핸드브레이크만 짧게 추가해 뒤축 제동 요구가 커지는 것을 비교합니다.','시작 방식과 무관하게 회전 억제와 카운터 회수로 경로를 회복하는 단계를 반복합니다.'],
      ['새 시작 기술 뒤 스핀하면 시작 강도보다 회복 타이밍도 함께 점검합니다.','핸드브레이크를 오래 유지해 속도를 잃으면 시작 후 유지가 어려워집니다.'],
      ['클러치 토크 차단만 있는 도구로 킥의 RPM과 타이밍을 배웠다고 생각하기.','시작 기술을 연속 사용해 불안정한 유지 상태를 가리기.'],
      'AC에서 RPM·기어·수동 클러치와 차량 사양을 먼저 확인합니다. 같은 평지 진입에서 한 방법씩 짧게 사용하고 바로 회복합니다. 성공률이 안정된 뒤 원돌이에 연결합니다.',
      '자기 첫 세트보다 시작 뒤 스핀을 줄이고, 두 기술이 구동과 제동 중 무엇을 바꾸는지 구별합니다.','playground','ae86',['driftstudy','toyota','grip'],
      '이 도구는 RPM·기어·엔진/플라이휠 회전 에너지·클러치 결합 충격을 계산하지 않으므로 클러치킥을 재현하지 않습니다. 핸드브레이크도 뒤축 제동 포화만 다루며 실제 바퀴 잠김은 없습니다.'),
    lesson('formula','aero-release','23 · 포뮬러: 속도가 줄면 제동도 바꾸기',
      '고속의 접지 여유를 저속까지 그대로 가져가지 않습니다.',['trail-release','gt3-hypercar'],[
        '다운포스는 공기가 차를 노면 쪽으로 누르는 힘입니다. 같은 공력 상태에서 속도가 높아지면 일반적으로 공력 하중도 커집니다. 따라서 빠른 구간에서 가능한 제동·선회 요구가 느린 구간에서도 그대로 가능하다고 가정하면 안 됩니다.',
        '고속에서 강하게 시작한 제동도 속도가 내려가면 점진적으로 줄여야 할 수 있습니다. 여기에 조향이 더해지면 타이어 힘 배분이 다시 달라집니다. 정확한 압력 모양은 차·타이어·공력·보조 설정에 따라 달라지므로 한 그래프를 모든 포뮬러의 정답으로 쓰지 않습니다.'
      ],['포뮬러 원형으로 직선에서 다른 초기 속도의 제동을 비교합니다.','제동을 일정하게 유지한 시도와 속도에 맞춰 줄인 시도를 따로 만듭니다.','낮은 속도 코너와 빠른 코너에서 요구가 달라짐을 확인하되 모델의 접지 수치를 실제 심으로 옮기지 않습니다.'],
      ['감속 말기 제동 문제가 생기면 압력 해제와 남은 공력 하중을 함께 생각합니다.','빠른 코너에서 가능했던 조향을 저속 헤어핀에도 강요하지 않습니다.'],
      ['다운포스가 있으므로 더 빠르게 가면 항상 안전하게 돈다고 생각하기.','실제 차의 브레이크 잠김을 이 도구의 포화 표시와 동일시하기.'],
      '보유한 포뮬러 심에서 한 차·기본 셋업으로 직선 제동부터 익힙니다. 브레이크 잠김과 감속 말기 압력을 확인하고 저속·중속·고속 코너를 따로 반복합니다.',
      '자기 첫 세트 대비 감속 말기 잠김·이탈과 제동 종료 위치 편차를 줄입니다.','brake','formula',['aero','grip','trail'],
      '포뮬러는 교육용 고공력 원형이며 특정 F1 연식의 물리를 복제하지 않습니다. 실제 바퀴 잠김·공력 실속·차고·바닥 접촉은 재현하지 않습니다.'),
    lesson('formula','formula-adaptation','24 · 포뮬러: 보조·타이어·교통 적응',
      '차량과 게임 설정을 고정한 뒤 한 단계씩 확장합니다.',['aero-release','traffic-racecraft','long-run'],[
        '포뮬러 게임의 운전 보조, 실제 차량 장치, 차종별 설정은 같은 것이 아닙니다. ABS·TC·변속 보조의 현재 설정을 기록하고 하나씩 바꿔야 무엇을 새로 배워야 하는지 알 수 있습니다. 보조를 끈 개수로 실력을 평가하지 않습니다.',
        '실제 심의 타이어 상태와 다른 차의 기류 영향은 혼자 연습한 감각을 바꿀 수 있습니다. 빠른 반응보다 미리 보는 시선과 안정된 압력 조절이 먼저입니다. 혼자 반복한 뒤 낮은 부담의 교통 연습으로 옮겨 제동 여유와 추종 라인을 다시 찾습니다.'
      ],['개념 실험에서는 같은 코너를 5회 반복해 입력과 경로의 편차를 확인합니다.','실제 심의 차종·연식·보조·타이어·연료 조건을 메모합니다.','혼자 유효랩을 반복한 뒤 AI 추종, 나란히 달리기, 짧은 레이스 순서로 확장합니다.'],
      ['앞차를 따라갈 때 평소보다 밀리면 기류·시야·타이어·라인 변화를 각각 확인합니다.','보조 변경 뒤 실수가 늘면 여러 설정을 연속 변경하지 말고 적응 구간을 따로 둡니다.'],
      ['모든 포뮬러를 같은 공력·전자장치 특성으로 취급하기.','첫 랩과 준비된 타이어의 랩을 같은 한계로 몰기.'],
      '선택한 포뮬러 심에서 같은 조건의 짧은 스틴트 2개를 기록합니다. 그다음 AI 뒤를 따라 달리며 유효랩 비율·접촉·잠김·페이스 편차를 비교합니다.',
      '자기 첫 세트보다 사고와 편차를 줄이고, 혼자 달릴 때와 교통 속에서 달라진 조건을 설명합니다.','nurbgp','formula',['aero','dirtyair','data'],
      '타이어 열·마모·난류·다른 차량·ERS·변속과 보조 제어는 이 도구에 없습니다. 실제 심에서 각각 확인하는 확장 과제입니다.')
  ];
  const plan = [
    {week:1,title:'기초와 LMU GT3 기준 만들기',lessons:['load-grip','slip-yaw','layout-drive','vision-markers'],work:'한 GT3·한 트랙·기본 셋업을 고정합니다. 5랩 세트 두 개를 기록하고 시선 기준점 3개를 만듭니다.',metric:'유효랩 수/전체 랩 수, 랩타임 중앙값, 최대−최소, 이탈·스핀 수를 첫 기준으로 남깁니다.'},
    {week:2,title:'제동 해제와 탈출 연결',lessons:['corner-phases','trail-release','balance-diagnosis','exit-throttle'],work:'어려운 코너 두 곳을 골라 제동 해제와 가속 증가를 각각 한 변수씩 비교합니다.',metric:'첫 주 대비 코너 출구 뒤 같은 지점의 속도 편차와 가속 재해제 횟수를 줄입니다. 유효랩 비율도 함께 봅니다.'},
    {week:3,title:'데이터와 롱런',lessons:['telemetry-distance','long-run','traffic-racecraft','gt3-hypercar'],work:'거리 축 데이터로 손실 한 구간을 찾고 10랩 또는 20분 스틴트를 두 번 진행합니다. AI와 양쪽 라인도 연습합니다.',metric:'동일 시작 조건에서 중앙값·편차·큰 실수·접촉을 비교합니다. 단독 최속랩만으로 성공을 판단하지 않습니다.'},
    {week:4,title:'랠리의 노면과 연결',lessons:['surface-notes','lift-leftfoot','pendulum','rally-handbrake'],work:'EA WRC의 짧은 스테이지 한 곳을 고정합니다. 먼저 완주하고, 효과가 필요한 헤어핀에서만 회전 기술을 비교합니다.',metric:'첫 3회 대비 완주율, 큰 충돌·리셋 수, 놓친 노트 수를 개선합니다. 같은 노면·날씨·차량 연식을 유지합니다.'},
    {week:5,title:'AE86 작은 슬립에서 8자까지',lessons:['ae86-baseline','initiate-recover','donut','figure-eight'],work:'AC 평지에서 같은 AE86 사양을 고정합니다. 회복, 양방향 반원, 원돌이, 가운데에서 회복하는 8자 순서로 진행합니다.',metric:'5회 중 회복 성공률, 스핀·반대 튕김, 유지 가능한 구간 길이, 통제된 전환 비율을 자기 첫 세트와 비교합니다.'},
    {week:6,title:'확장하고 GT3로 돌아오기',lessons:['continuous-touge','clutch-handbrake','aero-release','formula-adaptation'],work:'연속 드리프트나 포뮬러 중 한 갈래를 선택해 실제 심에서 짧게 적응합니다. 마지막에는 첫 주 LMU GT3 조건으로 돌아와 재측정합니다.',metric:'첫 주 대비 유효랩 비율·중앙값·편차·사고를 비교하고 가장 반복되는 실수 하나를 다음 6주의 과제로 정합니다.'}
  ];
  const data = {categories,sources,lessons,plan,verifiedOn:'2026-11-12'};
  const KEY = 'intsGuideV1';
  const ids = new Set(lessons.map(item=>item.id));
  const categoryIds = new Set(categories.map(item=>item.id));
  const escapeHTML = value => String(value).replace(/[&<>"']/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  const emptyState = () => ({version:1,selected:lessons[0].id,read:[],practiced:[]});
  function validateState(value) {
    const clean = emptyState();
    if (!value || typeof value !== 'object' || value.version !== 1) return clean;
    if (ids.has(value.selected)) clean.selected = value.selected;
    for (const key of ['read','practiced']) if (Array.isArray(value[key])) clean[key] = [...new Set(value[key].filter(id=>typeof id==='string' && ids.has(id)))];
    return clean;
  }
  function addStyle() {
    if (document.getElementById('grip-guide-style')) return;
    const style=document.createElement('style'); style.id='grip-guide-style';
    style.textContent=`
      .guide-root{color:#dce5e9;line-height:1.7}.guide-root *{box-sizing:border-box}.guide-root button,.guide-root input,.guide-root select{font:inherit}.guide-root button,.guide-root summary{cursor:pointer}.guide-root button:focus-visible,.guide-root a:focus-visible,.guide-root input:focus-visible,.guide-root select:focus-visible,.guide-root summary:focus-visible{outline:2px solid var(--lime,#c9f457);outline-offset:3px}.guide-root h2,.guide-root h3,.guide-root h4{line-height:1.35}.guide-root p{margin:0 0 14px}.guide-root a{color:#b8d8f0;text-underline-offset:3px}.guide-head{margin-bottom:24px}.guide-eyebrow{font-size:12px;letter-spacing:.13em;color:var(--lime,#c9f457);font-weight:700}.guide-head h2{font-size:clamp(25px,4vw,38px);margin:7px 0 12px}.guide-subtle{font-size:13px;color:#a0b0bb}.guide-progress{display:flex;gap:10px;flex-wrap:wrap;align-items:center;margin:16px 0}.guide-chip{padding:5px 10px;border:1px solid #35434e;border-radius:7px;font-size:12px}.guide-plan{padding:18px 20px;border:1px solid #33423a;border-radius:12px;background:#121e19;margin:0 0 22px}.guide-plan summary{font-weight:700;color:#d5f39b}.guide-plan-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin:18px 0}.guide-week{padding:15px;background:#0c1512;border-radius:8px;border:1px solid #2c3c32}.guide-week h4{margin:0 0 8px;font-size:15px}.guide-week p{font-size:13px}.guide-toolbar{display:grid;grid-template-columns:minmax(0,1fr) 180px;gap:12px;margin-bottom:16px}.guide-toolbar label{display:grid;gap:5px;font-size:12px;color:#aebdc7}.guide-toolbar input,.guide-toolbar select{min-width:0;width:100%;background:#111b23;color:#e5edf1;border:1px solid #374651;border-radius:8px;padding:10px 12px}.guide-layout{display:grid;grid-template-columns:285px minmax(0,1fr);gap:20px;align-items:start}.guide-list{display:grid;gap:7px;max-height:850px;overflow:auto;padding:2px 6px 3px 2px}.guide-item{display:block;width:100%;text-align:left;color:#bdcbd4;background:#101b23;border:1px solid #293946;border-radius:8px;padding:12px;line-height:1.45}.guide-item[aria-current=true]{border-color:var(--lime,#c9f457);background:#1d2a21;color:#eff8e8}.guide-item strong{display:block;font-size:13px}.guide-item small{display:block;font-size:11px;color:#a0b2b9;margin-top:5px}.guide-detail{min-width:0;padding:26px;background:#101a22;border:1px solid #30414e;border-radius:12px}.guide-detail h3{font-size:25px;margin:8px 0 10px}.guide-detail h4{font-size:15px;margin:26px 0 10px;color:#d5e4ec}.guide-detail p,.guide-detail li{font-size:14px}.guide-detail ul,.guide-detail ol{padding-left:22px;margin:8px 0 16px}.guide-detail li+li{margin-top:7px}.guide-prereq{margin:14px 0 22px;font-size:12px;color:#9cafba}.guide-prereq button{background:none;border:0;padding:2px 3px;color:#bcd9ef;text-decoration:underline;text-underline-offset:3px;font-size:12px}.guide-scope{border-left:3px solid #82c1db;background:#132731;padding:13px 15px;border-radius:0 6px 6px 0;margin:22px 0}.guide-scope p{font-size:13px;margin:0}.guide-columns{display:grid;grid-template-columns:1fr 1fr;gap:18px}.guide-practice{border:1px solid #42533c;background:#1b261b;border-radius:9px;padding:18px;margin-top:22px}.guide-practice h4{margin:0 0 10px;color:#d8f3b6}.guide-practice p:last-child{margin-bottom:0}.guide-actions{display:flex;gap:9px;flex-wrap:wrap;margin:20px 0}.guide-actions button,.guide-reset,.guide-clear{border:1px solid #465965;border-radius:7px;padding:9px 12px;background:#172731;color:#e2edf2;line-height:1.4}.guide-actions button[aria-pressed=true]{border-color:#a4c96b;color:#d1f2a2;background:#263421}.guide-actions .guide-launch{background:var(--lime,#c9f457);color:#17200b;border-color:transparent;font-weight:700}.guide-sources{border-top:1px solid #2a3b47;padding-top:14px;margin-top:22px}.guide-sources summary{font-size:13px;color:#a9c3d4}.guide-sources li{font-size:12px;overflow-wrap:anywhere}.guide-navigation{display:flex;gap:10px;justify-content:space-between;border-top:1px solid #2b3b46;padding-top:16px;margin-top:22px}.guide-navigation button{background:none;border:0;color:#c1d4e0;font-size:13px;padding:7px 0}.guide-navigation button:disabled{opacity:.4;cursor:default}.guide-footer{margin-top:20px;font-size:12px;color:#9aadb9}.guide-reset{font-size:12px;padding:6px 9px}.guide-storage{color:#edc689}.guide-empty{padding:30px 12px;color:#a8bbc7;text-align:center}.guide-status{min-height:1.5em;font-size:12px;color:#d2e7ad}.guide-selected-note{font-size:12px;color:#b4c9d7;margin-bottom:12px}.guide-root [hidden]{display:none!important}@media(max-width:950px){.guide-layout{grid-template-columns:230px minmax(0,1fr)}.guide-detail{padding:20px}.guide-columns{grid-template-columns:1fr;gap:0}}@media(max-width:700px){.guide-layout{grid-template-columns:1fr}.guide-list{max-height:275px;grid-template-columns:repeat(2,minmax(0,1fr))}.guide-toolbar{grid-template-columns:1fr}.guide-plan-grid{grid-template-columns:1fr}.guide-detail{padding:18px}.guide-detail h3{font-size:22px}.guide-item{padding:10px}.guide-item strong{font-size:12px}}@media(max-width:380px){.guide-list{grid-template-columns:1fr}.guide-actions{flex-direction:column}}
    `; document.head.appendChild(style);
  }
  function mount(root,options={}) {
    if (!root || typeof root.addEventListener !== 'function') throw new TypeError('GripGuide.mount requires a root element');
    if (root.__gripGuideDispose) root.__gripGuideDispose();
    addStyle(); root.classList.add('guide-root');
    let state=emptyState(), storageOK=true, query='', category='all', resetArmed=false, planOpen=false, sourcesOpen=false, notice='';
    try { const raw=localStorage.getItem(KEY); if(raw) { try {state=validateState(JSON.parse(raw));} catch (_) {state=emptyState();} } } catch (_) { storageOK=false; }
    function save() {try {localStorage.setItem(KEY,JSON.stringify(state));} catch (_) {storageOK=false;}}
    function progress() {return {read:state.read.length,practiced:state.practiced.length,total:lessons.length};}
    function filtered() {
      const needle=query.trim().toLocaleLowerCase();
      return lessons.filter(item=>(category==='all'||item.category===category) && (!needle || [item.title,item.summary,...item.paragraphs,...item.steps,item.practice,...item.errors,item.vehicle].join(' ').toLocaleLowerCase().includes(needle)));
    }
    const listHTML=(items,ordered=false)=>`<${ordered?'ol':'ul'}>${items.map(item=>`<li>${escapeHTML(item)}</li>`).join('')}</${ordered?'ol':'ul'}>`;
    function renderList() {
      const target=root.querySelector('[data-guide-list]');
      if(target) target.innerHTML=lessonList();
      const count=root.querySelector('[data-guide-count]'); if(count) count.textContent=`${filtered().length}개 수업`;
      const selectedNote=root.querySelector('[data-guide-selected-note]');
      if(selectedNote) {selectedNote.hidden=filtered().some(item=>item.id===state.selected);selectedNote.textContent='현재 읽는 수업은 검색 결과 밖에 있습니다. 아래 본문은 그대로 유지됩니다.';}
    }
    function lessonList() {
      const matches=filtered();
      if(!matches.length) return '<div class="guide-empty">일치하는 수업이 없습니다.<br>다른 검색어를 입력하거나 필터를 초기화하세요.</div>';
      return matches.map(item=>`<button type="button" class="guide-item" data-guide="select" data-id="${item.id}" aria-current="${state.selected===item.id}"><strong>${escapeHTML(item.title)}</strong><small>${escapeHTML(categories.find(c=>c.id===item.category).label)} · ${state.read.includes(item.id)?'읽음':'읽기 전'} · ${state.practiced.includes(item.id)?'심 실습 기록':'실습 전'}</small></button>`).join('');
    }
    function render() {
      const item=lessons.find(item=>item.id===state.selected), index=lessons.indexOf(item), p=progress();
      const cat=categories.find(c=>c.id===item.category);
      root.innerHTML=`<header class="guide-head"><span class="guide-eyebrow">DRIVER'S FIELD GUIDE · 24 LESSONS</span><h2>원리를 익히고, 실제 심에서 확인하기</h2><p>LMU GT3를 중심으로 랠리·AE86 드리프트·하이퍼카·포뮬러까지. <strong>예측 → 한 변수 변경 → 관찰 → 설명 → 반복</strong>을 모든 차에 적용합니다.</p><p class="guide-subtle">처음이라면 01부터 순서대로 읽으세요. 각 수업은 선행 지식, 개념 실험, 실패 징후, 실제 심 실습으로 이어집니다. 기록 기준은 이전의 자신과 비교하는 학습 제안입니다.</p><div class="guide-progress"><span class="guide-chip">읽음 ${p.read} / ${p.total}</span><span class="guide-chip">실제 심 실습 기록 ${p.practiced} / ${p.total}</span><span class="guide-subtle">직접 체크한 기록이며 실력 평가가 아닙니다.</span></div></header>
        <details class="guide-plan" data-guide-plan ${planOpen?'open':''}><summary>6주 연습 계획 · GT3를 기준으로 확장하기</summary><p class="guide-subtle" style="margin-top:14px">주 3회, 회당 약 30–45분을 출발점으로 삼고 여건에 맞춰 조정하세요. 매회 개념 실험 5–10분 → 실제 심 20–30분 → 기록 5분. 한 주를 반복해도 괜찮습니다.</p><div class="guide-plan-grid">${plan.map(week=>`<section class="guide-week"><h4>${week.week}주 · ${escapeHTML(week.title)}</h4><p>${escapeHTML(week.work)}</p><p class="guide-subtle">비교 지표 · ${escapeHTML(week.metric)}</p><span class="guide-subtle">수업 ${week.lessons.map(id=>String(lessons.findIndex(item=>item.id===id)+1).padStart(2,'0')).join(' · ')}</span></section>`).join('')}</div><p>iRacing 2,000–3,000은 장기적인 희망 목표로 둘 수 있지만, 이 과정의 수료나 실험실 점수로 도달을 예측할 수는 없습니다. iRating은 상대와의 경기 결과에 따라 달라지는 지표입니다. <a href="${sources.find(s=>s.id==='irating').url}" target="_blank" rel="noopener noreferrer">iRacing 공식 설명</a></p><p class="guide-subtle">재측정할 때 차량·트랙·셋업·시작 연료·날씨·타이어 준비 조건을 맞추세요. 조건이 바뀌었다면 새로운 기준으로 남깁니다.</p></details>
        <div class="guide-toolbar"><label>수업 검색<input type="search" data-guide-search maxlength="160" placeholder="예: 트레일, 카운터, AE86" value="${escapeHTML(query)}"></label><label>분야<select data-guide-category aria-label="수업 분야"><option value="all" ${category==='all'?'selected':''}>전체 분야</option>${categories.map(c=>`<option value="${c.id}" ${category===c.id?'selected':''}>${escapeHTML(c.label)}</option>`).join('')}</select></label></div><div class="guide-subtle" style="margin-bottom:10px"><span data-guide-count>${filtered().length}개 수업</span> <button type="button" class="guide-clear" data-guide="clear" style="margin-left:8px;padding:3px 8px;font-size:12px">필터 초기화</button></div>
        <div class="guide-layout"><nav class="guide-list" data-guide-list aria-label="가이드 수업">${lessonList()}</nav><article class="guide-detail" aria-labelledby="guide-lesson-title"><p class="guide-selected-note" data-guide-selected-note ${filtered().some(l=>l.id===item.id)?'hidden':''}>현재 읽는 수업은 검색 결과 밖에 있습니다. 아래 본문은 그대로 유지됩니다.</p><span class="guide-eyebrow">${escapeHTML(cat.label)} · ${index+1} / ${lessons.length}</span><h3 id="guide-lesson-title" tabindex="-1">${escapeHTML(item.title)}</h3><p>${escapeHTML(item.summary)}</p><div class="guide-prereq">먼저 익힐 것 · ${item.prerequisites.length?item.prerequisites.map(id=>`<button type="button" data-guide="select" data-id="${id}">${escapeHTML(lessons.find(l=>l.id===id).title)}</button>`).join(' / '):'없음 · 여기서 시작하세요'}</div>${item.paragraphs.map(p=>`<p>${escapeHTML(p)}</p>`).join('')}
        <div class="guide-scope"><p><strong>개념 실험의 범위</strong><br>${escapeHTML(item.scope)}</p></div><h4>개념 실험 · 한 번에 하나씩</h4>${listHTML(item.steps,true)}<div class="guide-actions"><button type="button" class="guide-launch" data-guide="launch">이 조건으로 개념 실험 열기 →</button></div><div class="guide-columns"><section><h4>관찰할 징후</h4>${listHTML(item.signs)}</section><section><h4>흔한 오류</h4>${listHTML(item.errors)}</section></div>
        <section class="guide-practice"><h4>실제 심 실습 · 다음 세션에서</h4><p>${escapeHTML(item.practice)}</p><p><strong>진행 기준</strong><br>${escapeHTML(item.pass)}</p><p class="guide-subtle">이 기준은 가이드의 연습 제안이며 게임 공식 합격 기준이나 레이팅 예측이 아닙니다.</p></section><div class="guide-actions"><button type="button" data-guide="read" aria-pressed="${state.read.includes(item.id)}">${state.read.includes(item.id)?'✓ 읽음 기록됨':'읽음 표시'}</button><button type="button" data-guide="practice" aria-pressed="${state.practiced.includes(item.id)}">${state.practiced.includes(item.id)?'✓ 실제 심 실습 기록됨':'실제 심에서 실습했음'}</button></div><p class="guide-subtle">버튼을 다시 누르면 체크가 해제됩니다. 실험실을 열기만 해서는 실습 완료로 기록하지 않습니다.</p><p class="guide-status" role="status">${escapeHTML(notice)}</p>
        <details class="guide-sources" data-guide-sources ${sourcesOpen?'open':''}><summary>원리의 출처 · 실습과 진행 기준은 가이드에서 구성</summary><ul>${item.sourceIds.map(id=>{const s=sources.find(s=>s.id===id);return `<li><a href="${escapeHTML(s.url)}" target="_blank" rel="noopener noreferrer">${escapeHTML(s.title)}</a> — ${escapeHTML(s.note)}</li>`;}).join('')}</ul><p class="guide-subtle">자료 확인: ${data.verifiedOn}. 차량·게임별 세부 설정은 선택한 버전의 공식 안내를 다시 확인하세요.</p></details><div class="guide-navigation"><button type="button" data-guide="previous" ${index===0?'disabled':''}>← 이전 수업</button><button type="button" data-guide="next" ${index===lessons.length-1?'disabled':''}>다음 수업 →</button></div></article></div>
        <footer class="guide-footer"><p>교육용 단순 평면 모델입니다. 실제 게임의 랩타임, 휠 복원력, 바퀴 잠김, 기어·RPM·하이브리드, 타이어 열·마모, 요철과 점프를 재현하지 않습니다. Monza·Nürburgring GP는 경로를 읽는 평면 근사입니다.</p><p class="${storageOK?'':'guide-storage'}">${storageOK?'진도는 이 브라우저에 저장됩니다. 다른 기기와 자동 동기화되지 않습니다.':'브라우저 저장소를 사용할 수 없어 현재 화면을 사용하는 동안만 진도를 유지합니다.'}</p><button type="button" class="guide-reset" data-guide="reset">${resetArmed?'한 번 더 눌러 가이드 진도 삭제':'가이드 진도 초기화'}</button>${resetArmed?' <button type="button" class="guide-reset" data-guide="cancel-reset">취소</button>':''}</footer>`;
    }
    function onClick(event) {
      const target=event.target.closest('[data-guide]'); if(!target || !root.contains(target) || target.disabled) return;
      const action=target.dataset.guide; let focus='';
      if(action==='select') {if(!ids.has(target.dataset.id)) return; state.selected=target.dataset.id;notice='';sourcesOpen=false;save();focus='#guide-lesson-title';}
      else if(action==='launch') {const item=lessons.find(item=>item.id===state.selected);if(typeof options.onLaunch==='function') options.onLaunch(item.preset,item.vehicle);return;}
      else if(action==='read'||action==='practice') {const key=action==='read'?'read':'practiced',had=state[key].includes(state.selected);state[key]=had?state[key].filter(id=>id!==state.selected):[...state[key],state.selected];notice=had?'기록을 해제했습니다.':action==='read'?'읽음 기록을 저장했습니다.':'실제 심 실습을 직접 기록했습니다. 실력 평가는 아닙니다.';save();focus=`[data-guide="${action}"]`;}
      else if(action==='previous'||action==='next') {const index=lessons.findIndex(item=>item.id===state.selected),next=lessons[index+(action==='next'?1:-1)];if(!next)return;state.selected=next.id;notice='';sourcesOpen=false;save();focus='#guide-lesson-title';}
      else if(action==='clear') {query='';category='all';focus='[data-guide-search]';}
      else if(action==='cancel-reset') {resetArmed=false;focus='[data-guide="reset"]';}
      else if(action==='reset') {if(resetArmed){state=emptyState();resetArmed=false;notice='가이드 진도를 초기화했습니다.';save();}else resetArmed=true;focus='[data-guide="reset"]';}
      else return;
      render();if(focus){const node=root.querySelector(focus);if(node)node.focus({preventScroll:true});}
    }
    function onInput(event) {if(event.target && event.target.hasAttribute('data-guide-search')) {query=String(event.target.value).slice(0,160);renderList();}}
    function onChange(event) {if(event.target && event.target.hasAttribute('data-guide-category')) {category=categoryIds.has(event.target.value)?event.target.value:'all';renderList();}}
    function onToggle(event) {if(!event.target || !root.contains(event.target))return;if(event.target.hasAttribute('data-guide-plan'))planOpen=event.target.open;if(event.target.hasAttribute('data-guide-sources'))sourcesOpen=event.target.open;}
    root.addEventListener('click',onClick);root.addEventListener('input',onInput);root.addEventListener('change',onChange);root.addEventListener('toggle',onToggle,true);render();
    function dispose() {root.removeEventListener('click',onClick);root.removeEventListener('input',onInput);root.removeEventListener('change',onChange);root.removeEventListener('toggle',onToggle,true);delete root.__gripGuideDispose;}
    root.__gripGuideDispose=dispose;
    return {dispose,getProgress:progress,getState:()=>validateState(state)};
  }
  globalThis.GripGuide=Object.freeze({mount,data,validateState});
})();
