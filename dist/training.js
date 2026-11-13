/* Small, observable driving exercises for the educational Grip Lab model.
 * Targets are practice bands, not measured game/real-world braking points or pace.
 * All speeds in this API are km/h, angles degrees, progress metres, times seconds.
 */
(function(root){
  'use strict';
  const NOTE='이 앱의 교육용 모델에서 연습하는 기준입니다. 실제 게임의 제동 지점·권장 속도·최적 기록이 아닙니다.';
  const targets={corner_release:[32,45],monza_t1:[65,85],monza_lesmo:[95,125],nurb_t1:[65,85],rally_hairpin:[18,28]};
  const releases={corner_release:.35,monza_t1:.3,nurb_t1:.35,rally_hairpin:.3};
  const demoSpeeds={intro_brake:0,corner_release:38,monza_t1:75,monza_lesmo:110,nurb_t1:75,rally_hairpin:23,drift_circle_intro:28,whole_monza:65};
  const finishCriteria={
    intro_brake:'직선에서 브레이크로 정지 · 도로 밖 0.2초 이하',
    corner_release:'회전 시작 32–45 km/h · 브레이크 35% 이하 · 구간 완주',
    monza_t1:'첫 회전 65–85 km/h · 브레이크 30% 이하 · 시케인 완주',
    monza_lesmo:'진입 95–125 km/h보다 높은 탈출 속도 · 가속·제동 겹침 0.4초 이하',
    nurb_t1:'진입 65–85 km/h · 브레이크 35% 이하 · 헤어핀 완주',
    rally_hairpin:'진입 18–28 km/h · 브레이크 30% 이하 · 헤어핀 완주',
    drift_circle_intro:'최고 40 km/h 이하 · 최대 슬립각 15° 이하 · 원 경로 완주',
    whole_monza:'한 바퀴 완주 · 도로 밖 1초 이하'
  };
  const freeze=value=>{if(value&&typeof value==='object'){Object.values(value).forEach(freeze);Object.freeze(value);}return value;};
  const missions=freeze([
    {id:'intro_brake',title:'첫 주행 · 브레이크로 멈추기',shortGoal:'조향 없이 브레이크를 눌러 차를 멈추세요.',why:'가장 먼저 익힐 것은 가속보다 감속입니다. 속도계와 브레이크 입력이 함께 변하는지 확인합니다.',
      category:'foundation',vehicle:'gt3',preset:'brake',surface:'dry',aids:'default',startSpeedKph:60,durationLabel:'약 10초',
      inputHint:'Space로 시작 → S / ↓를 눌러 멈추기. A·D는 아직 쓰지 마세요.',
      steps:['시작하면 차는 이미 60 km/h로 움직입니다.','S / ↓를 눌러 브레이크 표시와 속도계를 보세요.','완전히 멈추면 결과를 확인하고 다음 연습으로 갑니다.'],
      successText:'직선에서 브레이크로 정지했어요.',focusMetric:'정지 확인',recordLabel:'정지까지 이동 거리',metricKey:'stopDistance',nextId:'corner_release',
      runningHints:['브레이크를 눌러 보세요 · S / ↓','핸들을 가운데 두고 속도가 줄어드는지 보세요.','완전히 멈추면 결과가 나옵니다.']},
    {id:'corner_release',title:'90° 코너 · 브레이크 놓기',shortGoal:'회전 전에 32–45 km/h로 줄이고 브레이크를 놓으세요.',why:'감속과 조향을 한꺼번에 크게 주면 경로를 따라가기 어려워집니다. 이번에는 회전 직전 브레이크를 줄이는 순서만 익힙니다.',
      category:'foundation',vehicle:'gt3',preset:'corner',surface:'dry',aids:'default',startSpeedKph:60,durationLabel:'약 25초',
      inputHint:'S / ↓로 감속 → 회전 표지 전에 놓기 → A / ←로 좌회전.',
      steps:['주황 제동 표지에서 감속을 시작합니다.','회전 표지에서 32–45 km/h, 브레이크 35% 이하를 목표로 합니다.','코너 끝이 보이면 조향을 줄이고 W / ↑로 가속합니다.'],
      successText:'감속한 뒤 브레이크를 놓고 회전했어요.',focusMetric:'회전 시작의 브레이크',recordLabel:'회전 시작 속도',metricKey:'entrySpeed',nextId:'monza_t1',
      runningHints:['주황 표지에서 감속 · 회전 전 32–45 km/h','브레이크를 놓고 A / ← · 조향을 조금씩 조절하세요.','조향을 풀며 W / ↑ · 완주보다 입력 순서를 기억하세요.']},
    {id:'monza_t1',title:'몬자 T1 · 첫 시케인',shortGoal:'250 km/h 고속 접근에서 65–85 km/h로 줄이고 시케인을 연결하세요.',why:'첫 시케인에 GT3 고속 접근을 가정해 연습합니다. 250 km/h는 연습 조건이며 실제 차량·게임별 측정값은 아닙니다. 진입 속도를 낮춘 뒤 오른쪽, 왼쪽 방향 전환을 차례로 처리합니다.',
      category:'circuit',vehicle:'gt3',preset:'monza',surface:'dry',aids:'default',startSpeedKph:250,durationLabel:'약 20초',
      inputHint:'S로 감속 → D / → 첫 회전 → A / ← 두 번째 회전.',
      steps:['250 km/h로 접근합니다. 200·150·100 m 거리판을 보며 강하게 감속합니다.','회전 표지의 65–85 km/h를 확인하고 브레이크를 놓습니다.','오른쪽·왼쪽을 연결해 초록 탈출 표지까지 도로 안에 머뭅니다.'],
      successText:'첫 시케인을 낮은 진입 속도로 연결했어요.',focusMetric:'시케인 진입 속도',recordLabel:'첫 회전 진입 속도',metricKey:'entrySpeed',nextId:'monza_lesmo',
      runningHints:['제동 표지를 보고 감속 · 첫 회전 65–85 km/h','D / → 다음 A / ← · 시케인 사이에서 가속을 서두르지 마세요.','차가 직선을 향하면 조향을 풀고 가속하세요.']},
    {id:'monza_lesmo',title:'몬자 레스모 1 · 탈출 가속',shortGoal:'앞선 로지아 구간을 지나 레스모에 95–125 km/h로 진입하고 탈출 가속하세요.',why:'로지아 일부를 포함한 연결 진입 구간에서 시작합니다. 레스모 우회전의 감속과 가속 입력을 분리하고 진입·탈출 속도를 비교합니다.',
      category:'circuit',vehicle:'gt3',preset:'monza',surface:'dry',aids:'default',startSpeedKph:70,approachSpeedKph:160,durationLabel:'약 20초',
      inputHint:'S로 감속 → D / →로 회전 → 브레이크를 놓고 W / ↑.',
      steps:['앞선 로지아 구간을 지나 레스모 회전 표지에서 95–125 km/h가 되도록 감속합니다.','D / →로 우회전하며 다음 탈출 구간을 봅니다.','브레이크와 가속을 겹치지 않고 탈출 속도를 진입보다 높입니다.'],
      successText:'브레이크와 가속을 분리해 탈출했어요.',focusMetric:'진입 대비 탈출 속도',recordLabel:'탈출 속도',metricKey:'exitSpeed',nextId:'nurb_t1',
      runningHints:['회전 전 95–125 km/h · 주황 표지부터 준비하세요.','D / →로 우회전 · 브레이크를 놓고 가속을 준비하세요.','W / ↑로 가속 · 탈출 속도를 진입보다 높여 보세요.']},
    {id:'nurb_t1',title:'뉘르부르크링 GP · T1 헤어핀',shortGoal:'65–85 km/h로 진입해 긴 우회전을 도로 안에서 마치세요.',why:'헤어핀에서는 첫 회전 뒤에도 길이 계속 돌아갑니다. 낮은 진입 속도를 만든 뒤 출구가 열릴 때까지 조향을 유지하는 연습입니다.',
      category:'circuit',vehicle:'gt3',preset:'nurbgp',surface:'dry',aids:'default',startSpeedKph:230,durationLabel:'약 20초',
      inputHint:'S로 충분히 감속 → D / →를 조절하며 긴 우회전.',
      steps:['230 km/h 접근에서 강하게 감속해 회전 표지에 65–85 km/h로 도착합니다.','브레이크를 줄이고 D / →로 긴 회전을 따라갑니다.','출구가 보이면 조향을 풀며 가속해 끝까지 이어갑니다.'],
      successText:'낮은 진입 속도로 T1 헤어핀을 마쳤어요.',focusMetric:'헤어핀 진입 속도',recordLabel:'T1 진입 속도',metricKey:'entrySpeed',nextId:'rally_hairpin',
      runningHints:['회전 전 65–85 km/h · 직선에서 속도를 먼저 줄이세요.','긴 우회전입니다 · D / →를 조금씩 조절하세요.','출구 방향이 열리면 조향을 풀며 가속하세요.']},
    {id:'rally_hairpin',title:'랠리 · 저속 헤어핀',shortGoal:'자갈길 회전 전에 18–28 km/h로 감속하세요.',why:'낮은 접지에서는 가속이나 핸드브레이크보다 진입 속도부터 익힙니다. 가상 헤어핀 하나에서 감속 → 회전 → 탈출 순서를 반복합니다.',
      category:'rallydrift',vehicle:'rally1',preset:'hairpin',surface:'gravel',aids:'default',startSpeedKph:40,durationLabel:'약 35초',
      inputHint:'S로 일찍 감속 → A / ←로 좌회전. 이번에는 E를 쓰지 마세요.',
      steps:['자갈 직선에서 속도를 낮춰 회전 표지에 18–28 km/h로 도착합니다.','브레이크를 놓고 A / ←로 헤어핀을 따라갑니다.','출구를 향해 조향을 풀고 가속을 조금씩 더합니다.'],
      successText:'자갈 헤어핀에 낮은 속도로 진입했어요.',focusMetric:'자갈길 진입 속도',recordLabel:'헤어핀 진입 속도',metricKey:'entrySpeed',nextId:'drift_circle_intro',
      runningHints:['회전 전 18–28 km/h · 자갈에서는 일찍 감속하세요.','A / ← · 가속을 서두르지 말고 출구를 보세요.','조향을 줄이며 W / ↑를 짧게 눌러 가속하세요.']},
    {id:'drift_circle_intro',title:'드리프트 준비 · 천천히 원 돌기',shortGoal:'40 km/h 이하로 원을 따라가며 차가 크게 미끄러지지 않게 하세요.',why:'드리프트를 시도하기 전에 차의 코와 이동 방향이 거의 같은 상태를 경험합니다. 이 연습은 저속 원 선회이며 드리프트 유지 능력을 평가하지 않습니다.',
      category:'rallydrift',vehicle:'ae86',preset:'drift',surface:'dry',aids:'default',startSpeedKph:28,durationLabel:'약 30초',
      inputHint:'A / ←로 원을 따라가기. W / ↑는 속도가 떨어질 때 짧게.',
      steps:['28 km/h로 시작해 A / ←를 조금씩 조절합니다.','40 km/h 이하를 유지하고 도로 바깥으로 나가지 않습니다.','거의 한 바퀴를 돌아 완주선까지 차분히 연결합니다.'],
      successText:'크게 미끄러지지 않고 원 선회를 마쳤어요.',focusMetric:'낮은 속도와 작은 슬립각',recordLabel:'최대 차체 슬립각',metricKey:'peakBeta',nextId:'whole_monza',
      runningHints:['A / ←를 조금씩 조절 · 40 km/h 이하','원을 따라가세요 · W / ↑는 속도가 떨어질 때만 짧게','마지막까지 원을 유지하세요 · 드리프트 점수는 매기지 않습니다.']},
    {id:'whole_monza',title:'몬자 GP · 한 바퀴 연결',shortGoal:'연습한 구간을 포함해 몬자 한 바퀴를 도로 안에서 마치세요.',why:'구간 연습을 한 바퀴로 연결합니다. 이번 목표는 코스 순서와 완주이며 빠른 랩타임이나 실제 게임 실력은 판정하지 않습니다.',
      category:'circuit',vehicle:'gt3',preset:'monza',surface:'dry',aids:'default',startSpeedKph:70,durationLabel:'약 2–4분',
      inputHint:'코너 이름과 미니맵을 보며 미리 감속하세요. 서두르지 않아도 됩니다.',
      steps:['T1에서 배운 감속 → 방향 전환 순서를 사용합니다.','다음 코너 이름을 보고 일찍 준비하며 한 바퀴를 연결합니다.','완주 후 이탈 시간과 주행 시간을 다음 시도의 기준으로 남깁니다.'],
      successText:'몬자 한 바퀴를 도로 안에서 연결했어요.',focusMetric:'한 바퀴 완주',recordLabel:'한 바퀴 주행 시간',metricKey:'time',nextId:'monza_t1',
      runningHints:['첫 시케인부터 차분하게 · 미니맵과 다음 코너를 보세요.','긴 직선 다음 회전을 미리 준비하세요 · 속도보다 코스 순서','마지막 코너 뒤 완주선까지 이어가세요.']}
  ].map(m=>({...m,targetNote:NOTE,finishCriterion:finishCriteria[m.id],entrySpeedRange:targets[m.id]||null,releaseBrakeLimit:releases[m.id]??null,demoSpeedKph:demoSpeeds[m.id]})));
  const byId=Object.fromEntries(missions.map(m=>[m.id,m]));
  const get=id=>Object.prototype.hasOwnProperty.call(byId,id)?byId[id]:null;
  function measuredPoints(coordinates){
    const points=[];let total=0;
    for(const p of coordinates){const prev=points.at(-1);if(prev){const ds=Math.hypot(p.x-prev.x,p.y-prev.y);if(ds<1e-9)continue;total+=ds;}points.push({x:p.x,y:p.y,s:total});}
    return points;
  }
  function at(points,s){
    if(s<=0)return {...points[0],s:0};
    if(s>=points.at(-1).s)return {...points.at(-1)};
    const i=points.findIndex(p=>p.s>=s),a=points[i-1],b=points[i],t=(s-a.s)/(b.s-a.s);
    return {x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t,s};
  }
  function segment(trackId,cornerId,before,after){
    const parent=root.GripTracks?.get(trackId);if(!parent)return null;
    const c=parent.corners.find(c=>c.id===cornerId);if(!c)return null;
    const start=Math.max(0,c.s-before),end=Math.min(parent.total,c.s+after);
    const points=measuredPoints([at(parent.points,start),...parent.points.filter(p=>p.s>start&&p.s<end),at(parent.points,end)]);
    return {...parent,points,total:points.at(-1).s,closed:false,freePractice:false,parentTrack:trackId,parentStartS:start,parentEndS:end,
      corners:parent.corners.filter(c=>c.s>=start&&c.s<=end).map(c=>({...c,s:c.s-start})),source:{...parent.source,notes:parent.source.notes+' '+NOTE}};
  }
  function synthetic(kind){
    const p=[],add=(x,y)=>p.push({x,y});
    if(kind==='intro_brake'){for(let x=0;x<=150;x+=3)add(x,0);}
    else if(kind==='drift_circle_intro'){
      for(let i=0;i<=132;i++){const a=-Math.PI/2+i/132*Math.PI*11/6;add(28*Math.cos(a),28+28*Math.sin(a));}
    }else{
      const rally=kind==='rally_hairpin',straight=rally?65:90,radius=rally?22:34,angle=rally?Math.PI:Math.PI/2;
      for(let x=0;x<straight;x+=3)add(x,0);add(straight,0);
      for(let i=1;i<=90;i++){const a=-Math.PI/2+i/90*angle;add(straight+radius*Math.cos(a),radius+radius*Math.sin(a));}
      for(let d=3;d<=63;d+=3)if(rally)add(straight-d,2*radius);else add(straight+radius,radius+d);
    }
    const points=measuredPoints(p);
    return {points,total:points.at(-1).s,width:kind==='rally_hairpin'?11:12,closed:false,freePractice:false,corners:[],parentTrack:null,parentStartS:0,
      source:{approximation:true,label:'가상 단계별 연습장',url:null,officialLengthM:null,notes:'직접 설계한 평면 연습 코스입니다. '+NOTE},
      ...(kind==='drift_circle_intro'?{circleCenter:{x:0,y:28},circleRadius:28}:{})};
  }
  function scene(id){
    const m=get(id);if(!m)return null;
    let s,entry,exit,brake;
    if(id==='monza_t1'){s=segment('monza','rettifilo',500,165);brake=245;entry=420;exit=620;}
    else if(id==='monza_lesmo'){s=segment('monza','lesmo1',320,145);brake=155;entry=210;exit=380;}
    else if(id==='nurb_t1'){s=segment('nurbgp','t1',500,170);brake=275;entry=425;exit=615;}
    else if(id==='whole_monza'){
      const parent=root.GripTracks?.get('monza');if(!parent)return null;s={...parent,parentTrack:'monza',parentStartS:0,parentEndS:parent.total};
      entry=parent.turnInReferences.find(ref=>ref.cornerId==='rettifilo').s;exit=parent.corners.at(-1).s+140;brake=entry-350;
    }else{
      s=synthetic(id);
      if(id==='intro_brake'){brake=18;entry=55;exit=120;}
      if(id==='corner_release'){brake=35;entry=85;exit=153;}
      if(id==='rally_hairpin'){brake=15;entry=60;exit=142;}
      if(id==='drift_circle_intro'){brake=0;entry=15;exit=s.total-15;}
    }
    if(!s)return null;
    s.id=id;s.preset=id==='whole_monza'?'monza':id;s.name=m.title;s.trainingId=id;s.training=true;s.trainingNote=NOTE;s.description=m.shortGoal+' '+NOTE;
    s.startHeading=Math.atan2(s.points[1].y-s.points[0].y,s.points[1].x-s.points[0].x);s.gates={entry,exit};s.timeLimit=id==='whole_monza'?600:90;
    if(['monza_t1','monza_lesmo','nurb_t1'].includes(id)){
      const cornerId={monza_t1:'rettifilo',monza_lesmo:'lesmo1',nurb_t1:'t1'}[id],corner=s.corners.find(c=>c.id===cornerId);
      // Segment s is local route distance. Rebuild both references and boards after slicing.
      s.turnInReferences=[{id:cornerId+'-turn-in',cornerId,cornerName:corner.name,s:entry,label:'회전 기준선',modelOnly:true,offsetBeforeCornerM:corner.s-entry}];
      s.brakingBoards=[200,150,100,50].map(distance=>({s:entry-distance,distance,label:distance+' m',targetS:entry,cornerName:corner.name}));
      s.source={...s.source,turnInReferences:s.turnInReferences.map(ref=>({...ref}))};
    }else if(id!=='whole_monza'){
      s.brakingBoards=[];s.turnInReferences=[];s.brakingBoardNote='';
    }
    s.landmarks=[{s:brake,label:id==='intro_brake'?'브레이크로 정지':id==='drift_circle_intro'?'40 km/h 이하':'감속 준비',type:'brake'},
      {s:entry,label:id==='intro_brake'?'곧게 멈추기':id==='drift_circle_intro'?'원 선회':s.brakingBoards.length?'회전 기준선':'회전 시작',type:'turn'},
      {s:exit,label:id==='intro_brake'?'직선 연습장':id==='drift_circle_intro'?'선회 마무리':'탈출',type:'exit'}];
    if(id==='whole_monza')s.landmarks=[{s:brake,label:'첫 시케인 감속 준비',type:'brake'},...s.turnInReferences.map(ref=>({s:ref.s,label:ref.cornerName+' 회전 기준선',type:'turn'})),{s:s.total-55,label:'완주선까지',type:'exit'}];
    return s;
  }
  const measured=v=>typeof v==='number'&&Number.isFinite(v)&&v>=0;
  const display=(v,unit,digits=1)=>measured(v)?v.toFixed(digits)+' '+unit:'미측정';
  function evaluate(id,summary){
    const m=get(id);if(!m)return null;
    const r=summary&&typeof summary==='object'?summary:{},tm=r.trainingMetrics&&typeof r.trainingMetrics==='object'?r.trainingMetrics:{},checks=[];
    function check(label,pass,value,nextAction){checks.push({label,pass:!!pass,value,nextAction});}
    function numeric(label,value,predicate,unit,action){check(label,measured(value)&&predicate(value),(unit==='입력 (0–1)'&&measured(value)?Math.round(value*100)+'%':display(value,unit)),measured(value)?action:'측정 지점을 통과하는 주행을 다시 완료하세요.');}
    const finished=id==='intro_brake'?r.reason==='stop':r.reason==='finish';
    check(id==='intro_brake'?'브레이크로 정지':'구간 완주',finished&&measured(r.time)&&r.time>0,finished?'완료':({offtrack:'도로 밖 2초 초과로 종료',user:'직접 종료',spin:'스핀으로 종료',far:'코스에서 멀어져 종료',timeout:'시간 종료',stop:'구간 전에 정지'}[r.reason]||'완료 기록 없음'),id==='intro_brake'?'S / ↓를 눌러 완전히 멈춘 뒤 결과를 확인하세요.':'다음 시도에는 속도를 낮추고 완주선까지 연결하세요.');
    const offLimit=id==='whole_monza'?1:id==='intro_brake'?.2:.5;
    numeric('도로 밖 시간 ≤ '+offLimit+'초',r.offTime,v=>v<=offLimit,'초','다음에는 진입 속도를 조금 낮추고 도로 가운데를 따라가세요.');
    function entry(low,high){numeric('회전 시작 '+low+'–'+high+' km/h',r.entrySpeed,v=>v>=low&&v<=high,'km/h',measured(r.entrySpeed)&&r.entrySpeed<low?'다음에는 브레이크를 조금 더 일찍 놓아 진입 속도를 유지하세요.':'다음에는 제동을 더 일찍 시작해 회전 전에 목표 속도를 만드세요.');}
    function brakeUsed(){numeric('브레이크 입력 0.2초 이상',tm.brakeTime,v=>v>=.2,'초','주황 표지에서 브레이크를 눌러 직접 감속해 보세요.');}
    function released(limit){numeric('회전 시작 브레이크 ≤ '+Math.round(limit*100)+'%',tm.brakeAtEntry,v=>v<=limit,'입력 (0–1)','다음에는 회전 표지에 도착하기 조금 전에 브레이크를 놓으세요.');}
    if(id==='intro_brake'){
      brakeUsed();numeric('마지막 속도 ≤ 2 km/h',r.endSpeed,v=>v<=2,'km/h','브레이크를 유지해 완전히 멈춰 보세요.');
    }else if(id==='corner_release'){entry(...m.entrySpeedRange);brakeUsed();released(m.releaseBrakeLimit);}
    else if(id==='monza_t1'){entry(...m.entrySpeedRange);brakeUsed();released(m.releaseBrakeLimit);}
    else if(id==='monza_lesmo'){
      entry(...m.entrySpeedRange);brakeUsed();
      numeric('가속·제동 겹침 ≤ 0.4초',tm.overlapTime??r.throttleBrakeOverlapTime,v=>v<=.4,'초','브레이크를 놓은 것을 확인한 뒤 가속을 시작하세요.');
      check('탈출 속도 > 진입 속도',measured(r.exitSpeed)&&measured(r.entrySpeed)&&r.exitSpeed>r.entrySpeed,display(r.entrySpeed,'km/h')+' → '+display(r.exitSpeed,'km/h'),'출구가 보일 때 조향을 풀면서 가속을 조금씩 더하세요.');
    }else if(id==='nurb_t1'){entry(...m.entrySpeedRange);brakeUsed();released(m.releaseBrakeLimit);}
    else if(id==='rally_hairpin'){entry(...m.entrySpeedRange);brakeUsed();released(m.releaseBrakeLimit);}
    else if(id==='drift_circle_intro'){
      numeric('최고 속도 ≤ 40 km/h',tm.peakSpeed,v=>v<=40,'km/h','다음에는 가속을 짧게 사용해 40 km/h 이하를 유지하세요.');
      numeric('최대 차체 슬립각 ≤ 15°',r.peakBeta??r.maxBeta,v=>v<=15,'°','다음에는 가속과 조향을 줄여 차의 코와 이동 방향이 비슷하게 유지되도록 해 보세요.');
      numeric('선회 각도 240° 이상',tm.headingTurnDeg,v=>v>=240,'°','왼쪽 원 경로를 따라 거의 한 바퀴를 끝까지 돌아 보세요.');
      numeric('이동 거리 125 m 이상',tm.travelDistance,v=>v>=125,'m','원을 지름길로 건너가지 않고 도로를 따라 완주해 보세요.');
    }
    const failed=checks.find(c=>!c.pass),passed=!failed;
    const observable=checks.slice(1).find(c=>!c.pass)||checks.at(-1);
    return {passed,headline:passed?m.successText:'이번 목표를 다시 연습해 보세요.',
      observation:observable?observable.label+' · '+observable.value:'측정 기록을 확인하세요.',
      nextAction:passed?(get(m.nextId)?.title+'에서 다음 동작을 익혀 보세요.'):failed.nextAction,
      checks:checks.map(({label,pass,value})=>({label,pass,value})),nextId:m.nextId};
  }
  root.GripTraining=Object.freeze({missions,get,scene,evaluate});
})(typeof globalThis!=='undefined'?globalThis:this);
