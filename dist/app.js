(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const rad = 180 / Math.PI;
  const storageKey = 'gripLabRunsV1';
  const vehicleIds=['gt3','rally1','ae86','hypercar','formula','touring_fwd','midship','rearengine'];
  const presetIds=['corner','brake','slalom','hairpin','drift','figure8','monza','nurbgp','rally_stage','playground','touge'];
  const freeScenes=['drift','figure8','playground'];
  const esc = value => String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  let page = 'lab', simPage = 'lab', inputMode = 'keyboard', running = false, done = false, follow = false;
  let state, scene, samples = [], trail = [], attempts = [], history = [], run;
  let controls = {steer: 0, throttle: 0, brake: 0, handbrake:0, clutch:0}, keys = new Set(), pointer = null;
  let lastTime = 0, sampleTime = 0, uiTime = 0, toastTimer, storageFailed = false;
  let initialLoads;
  try { const saved = JSON.parse(localStorage.getItem(storageKey) || '[]'); if (Array.isArray(saved)) history = saved.filter(validRecord).slice(-150); } catch (_) { storageFailed = true; }

  function validRecord(r) {
    const nonnegative = value => Number.isFinite(value) && value >= 0;
    const optionalNumber = value => value === null || nonnegative(value);
    if (!r || typeof r.id !== 'string' || typeof r.at !== 'string' || !Number.isFinite(Date.parse(r.at)) || !nonnegative(r.time) || r.time > 901 || !r.config || typeof r.config !== 'object') return false;
    const c = r.config;
    if (!presetIds.includes(c.preset) || !['dry','wet','gravel'].includes(c.surface) || !['rwd','fwd','awd'].includes(c.drive) || !['keyboard','mouse','gamepad'].includes(c.input) || ![.5,1].includes(c.timeScale) || !Number.isFinite(c.startSpeed) || c.startSpeed < 20 || c.startSpeed > 200) return false;
    c.vehicle??='legacy';c.aids??='default';c.modelVersion??='v1';
    if(![...vehicleIds,'legacy'].includes(c.vehicle)||!['default','off','on'].includes(c.aids)||!['v1','v2'].includes(c.modelVersion))return false;
    if (!Array.isArray(r.sectors) || r.sectors.length !== 3 || !r.sectors.every(optionalNumber) || typeof r.valid !== 'boolean' || typeof r.paused !== 'boolean' || typeof r.practice !== 'boolean' || !['finish','stop','spin','timeout','far'].includes(r.reason)) return false;
    if (!['offTime','frontPct','rearPct','maxBeta'].every(key => nonnegative(r[key])) || r.frontPct > 100.01 || r.rearPct > 100.01 || !['entrySpeed','minSpeed','exitSpeed','stopDistance'].every(key => optionalNumber(r[key]))) return false;
    if (r.valid && (r.reason !== 'finish' || r.offTime >= .2 || (c.preset === 'corner' && (!r.sectors.every(nonnegative) || Math.abs(r.sectors.reduce((sum,value)=>sum+value,0)-r.time) > .1)))) return false;
    return true;
  }
  const config = () => ({preset:$('preset').value,vehicle:$('vehicle').value||'gt3',aids:$('aids').value||'default',modelVersion:'v2',surface:$('surface').value,drive:$('drive').value,startSpeed:Number($('start-speed').value),timeScale:Number($('time-scale').value),input:inputMode});
  function makeScene(preset) {
    const track=globalThis.GripTracks?.get(preset);if(track)return track;
    const points = [];
    const add = (x,y) => {const p=points.at(-1);points.push({x,y,s:p?p.s+Math.hypot(x-p.x,y-p.y):0});};
    if (preset==='corner') {
      for(let x=0;x<=60;x+=3)add(x,0);
      for(let i=1;i<=50;i++){const a=-Math.PI/2+i/50*Math.PI/2;add(60+32*Math.cos(a),32+32*Math.sin(a));}
      for(let y=35;y<=98;y+=3)add(92,y);
    } else if (preset==='brake') {for(let x=0;x<=500;x+=3)add(x,0);}
    else if (preset==='slalom') {for(let x=0;x<=230;x+=2)add(x,x<=30?0:12*Math.sin((x-30)/28)*Math.min(1,(x-30)/20));}
    else if(preset==='hairpin'){for(let x=0;x<=45;x+=3)add(x,0);for(let i=1;i<=70;i++){const a=-Math.PI/2+i/70*Math.PI;add(45+14*Math.cos(a),14+14*Math.sin(a));}for(let x=42;x>=0;x-=3)add(x,28);}
    else if(preset==='figure8'){for(let i=0;i<=180;i++){const a=i/180*Math.PI*2;add(40*Math.sin(a),20*Math.sin(2*a));}}
    else {for(let i=0;i<=120;i++){const a=-Math.PI/2+i/120*Math.PI*2;add(28*Math.cos(a),28+28*Math.sin(a));}}
    return {points,total:points.at(-1).s,width:freeScenes.includes(preset)?18:12,preset,name:{corner:'90° LEFT',brake:'STRAIGHT BRAKING',slalom:'LOW-GRIP SLALOM',hairpin:'HAIRPIN / R14',drift:'SKIDPAD / R28',figure8:'FIGURE 8 / TRANSITION'}[preset],startHeading:preset==='figure8'?Math.PI/4:0,timeLimit:freeScenes.includes(preset)?90:60};
  }
  function nearest(s) {
    let best={distance:Infinity,progress:0};
    for(let i=1;i<scene.points.length;i++){
      const a=scene.points[i-1],b=scene.points[i],dx=b.x-a.x,dy=b.y-a.y,l2=dx*dx+dy*dy;
      if(scene.total>700&&!freeScenes.includes(scene.preset)&&(b.s<run.progress-45||a.s>run.progress+100))continue;
      const t=clamp(((s.x-a.x)*dx+(s.y-a.y)*dy)/l2,0,1),x=a.x+t*dx,y=a.y+t*dy,d=Math.hypot(s.x-x,s.y-y);
      if(d<best.distance)best={distance:d,progress:a.s+t*Math.sqrt(l2)};
    }
    return best;
  }
  function clearInputs(){keys.clear();pointer=null;controls={steer:0,throttle:0,brake:0,handbrake:0,clutch:0};$('pad-dot').style.left='50%';$('pad-dot').style.top='50%';}
  function reset(keepResults=false){
    running=false;done=false;clearInputs();scene=makeScene($('preset').value);
    state=GripPhysics.create({vehicle:$('vehicle').value||'gt3',speed:Number($('start-speed').value)/3.6,surface:$('surface').value,aids:$('aids').value||'default'});
    state.x=scene.points[0].x;state.y=scene.points[0].y;state.heading=scene.startHeading||0;
    follow=scene.total>700;
    initialLoads={fl:state.staticNf?state.staticNf/2:state.loadFL,fr:state.staticNf?state.staticNf/2:state.loadFR,rl:state.staticNr?state.staticNr/2:state.loadRL,rr:state.staticNr?state.staticNr/2:state.loadRR};
    samples=[];trail=[];sampleTime=0;
    run={config:config(),progress:0,travelDistance:0,lastPosition:{x:state.x,y:state.y},offTime:0,maxOff:0,frontTime:0,rearTime:0,spinTime:0,maxBeta:0,entrySpeed:null,minSpeed:Infinity,exitSpeed:null,gates:[null,null],stopDistance:null,paused:false};
    $('start-hint').hidden=false;$('play').textContent='▶ 시작';$('run-status').textContent='준비됨';$('scene-tag').textContent=scene.name;
    $('run-dot').style.background='var(--muted)';$('track-status').textContent='코스 안';$('track-status').style.color='var(--muted)';
    const prompts={corner:['브레이크를 놓으며<br>차가 도는 순간을 보세요.','민트색 선은 비교용 중심선입니다. 최적 레이싱 라인이 아닙니다.'],brake:['곧게 제동한 뒤,<br>조향을 조금 더해 보세요.','완전히 멈추면 정지 거리와 접지 요구량을 확인합니다.'],slalom:['속도를 낮추고,<br>방향 전환을 미리 준비하세요.','자갈 노면은 낮은 접지의 근사입니다. 요철·점프는 포함하지 않습니다.'],drift:['차의 코와 이동 방향,<br>두 방향을 구분하세요.','처음에는 원을 천천히 도세요. 뒤가 흐를 때 시선과 카운터의 관계를 관찰합니다.']};
    const prompt=prompts[scene.preset]||[scene.name+'<br>입력을 이어서 확인하세요.',scene.source?.approximation?'공식 레이아웃을 참고한 평면 근사입니다. 실제 고저차·연석·브레이크 포인트를 재현하지 않습니다.':'가상 연습 경로입니다. 속도보다 경로와 방향 전환을 먼저 익히세요.'];
    $('start-hint').querySelector('strong').innerHTML=prompt[0];$('scene-description').textContent=prompt[1];
    if(!keepResults)$('results').hidden=true;
    updateVehicleInfo();updateControlsUI();updateTelemetry();lockSettings(false);
  }
  function lockSettings(lock){['vehicle','aids','preset','surface','drive','start-speed','time-scale'].forEach(id=>$(id).disabled=lock);document.querySelectorAll('[data-input]').forEach(el=>el.disabled=lock);}
  function updateVehicleInfo(){
    const v=GripPhysics.vehicles?.[$('vehicle').value];if(!v)return;
    $('vehicle-title').textContent=v.label;
    $('vehicle-spec').textContent=Math.round(v.mass)+' kg · '+Math.round(v.power/1000)+' kW · '+v.wheelbase.toFixed(2)+' m · '+v.drive.toUpperCase();
    const sources=(v.sourceFacts||[]).map(f=>'<li>'+esc(f.fact)+(f.url&&/^https:\/\//.test(f.url)?' <a href="'+esc(f.url)+'" target="_blank" rel="noopener">공식 자료 ↗</a>':'')+'</li>').join('');
    $('vehicle-details').innerHTML='<strong>'+esc(v.label)+' · 모델과 확인된 제원</strong><p>차량별 차이를 보기 위한 교육용 근사입니다. 타이어·공력·관성 계수는 실제 게임에서 검증한 값이 아닙니다. 구동 또는 보조장치를 바꾸면 원차 재현이 아닌 비교 실험이 됩니다.</p><p>배치: '+esc(v.layout)+' / 모델 질량 '+Math.round(v.mass)+' kg / 모델 출력 '+Math.round(v.power/1000)+' kW / 축간거리 '+v.wheelbase.toFixed(3)+' m</p><ul>'+(v.assumptions||[]).map(s=>'<li>'+esc(s)+'</li>').join('')+'</ul>'+(sources?'<strong>공개 자료로 확인한 사실</strong><ul>'+sources+'</ul>':'');
  }
  function selectVehicle(id){
    if(!vehicleIds.includes(id))return;
    $('vehicle').value=id;const v=GripPhysics.vehicles?.[id];
    if(v){$('drive').value=v.drive;$('surface').value=v.defaultSurface||'dry';$('start-speed').value=clamp(v.startingSpeedKph||70,20,200);$('start-speed-out').textContent=$('start-speed').value;}
    $('aids').value='default';
  }
  function play(){
    if(page==='practice'&&attempts.length>=5&&done){toast('세트가 끝났습니다. ‘새 세트’로 다시 시작하세요.');return;}
    if(done)reset(true);
    running=!running;
    if(running){$('start-hint').hidden=true;run.config=config();lockSettings(true);}
    else {run.paused=true;clearInputs();}
    $('play').textContent=running?'Ⅱ 일시정지':'▶ 계속';$('run-status').textContent=running?'관찰 중':'일시정지';$('run-dot').style.background=running?'var(--lime)':'var(--muted)';
  }
  function setInput(mode){
    if(!['keyboard','mouse','gamepad'].includes(mode))return;
    inputMode=mode;clearInputs();
    document.querySelectorAll('[data-input]').forEach(el=>{const on=el.dataset.input===mode;el.classList.toggle('active',on);el.setAttribute('aria-pressed',String(on));});
    $('mouse-pad').hidden=mode!=='mouse';
    const guides={keyboard:'<kbd>W</kbd> 가속 <kbd>S</kbd> 브레이크 <kbd>A</kbd><kbd>D</kbd> 조향 · 방향키도 사용 · <kbd>Space</kbd> 일시정지 <kbd>R</kbd> 재시작',mouse:'패드를 누른 채 좌우로 조향, 위로 가속, 아래로 제동. 손을 떼면 중립. 아래 슬라이더는 입력을 유지합니다.',gamepad:'표준 패드: 왼쪽 스틱 조향 · RT 가속 · LT 브레이크. 패드 버튼을 한 번 눌러 연결하세요.'};
    $('input-guide').innerHTML=guides[mode]+(mode==='keyboard'?' · <kbd>E</kbd> 핸드브레이크 <kbd>Q</kbd> 클러치':mode==='gamepad'?' · B 핸드브레이크 · LB 클러치':'');
    ['steer-range','throttle-range','brake-range','handbrake-range','clutch-range'].forEach(id=>$(id).disabled=mode!=='mouse');
    updateControlsUI();
  }
  function inputStep(dt){
    if(inputMode==='keyboard'){
      const left=keys.has('KeyA')||keys.has('ArrowLeft'),right=keys.has('KeyD')||keys.has('ArrowRight');
      const limit=clamp(.26+4/(state.speed+7),.32,.8),target=((left?1:0)-(right?1:0))*limit;
      controls.steer+=clamp(target-controls.steer,-dt*1.35,dt*1.35);
      controls.throttle+=clamp((keys.has('KeyW')||keys.has('ArrowUp')?1:0)-controls.throttle,-dt*4,dt*2.8);
      controls.brake+=clamp((keys.has('KeyS')||keys.has('ArrowDown')?1:0)-controls.brake,-dt*3.5,dt*4);
      controls.handbrake=keys.has('KeyE')?1:0;controls.clutch=keys.has('KeyQ')?1:0;
    } else if(inputMode==='gamepad'){
      let pads=[];try{pads=navigator.getGamepads?.()||[];}catch(_){}
      const pad=Array.from(pads).find(p=>p&&p.connected&&p.mapping==='standard');
      if(pad){const x=pad.axes[0]||0;controls.steer=Math.abs(x)<.08?0:-Math.sign(x)*(Math.abs(x)-.08)/.92;controls.throttle=pad.buttons[7]?.value||0;controls.brake=pad.buttons[6]?.value||0;controls.handbrake=pad.buttons[1]?.value||0;controls.clutch=pad.buttons[4]?.value||0;$('input-guide').textContent='패드 연결됨 · 왼쪽 스틱 / RT 가속 / LT 제동 / B 핸드브레이크 / LB 클러치';}
      else{controls={steer:0,throttle:0,brake:0,handbrake:0,clutch:0};$('input-guide').textContent='표준 패드를 기다리는 중 · 버튼을 눌러 활성화하세요. 연결이 안 되면 키보드 또는 마우스를 선택하세요.';}
    }
  }
  function measure(dt){
    run.travelDistance+=Math.hypot(state.x-run.lastPosition.x,state.y-run.lastPosition.y);run.lastPosition={x:state.x,y:state.y};
    const near=nearest(state),previous=run.progress;run.progress=Math.max(run.progress,near.progress);
    if(near.distance>scene.width/2-1){run.offTime+=dt;run.maxOff=Math.max(run.maxOff,near.distance);}
    if(state.utilF>1)run.frontTime+=dt;if(state.utilR>1)run.rearTime+=dt;
    run.maxBeta=Math.max(run.maxBeta,Math.abs(state.beta)*rad);
    run.spinTime=Math.abs(state.beta)*rad>65&&state.speed>3?run.spinTime+dt:0;
    if(scene.preset==='corner'){
      const gates=[45,110];
      gates.forEach((g,i)=>{if(run.gates[i]===null&&previous<g&&run.progress>=g){const frac=(g-previous)/(run.progress-previous);run.gates[i]=Math.max(0,state.time-dt+frac*dt);if(i===0)run.entrySpeed=state.speed*3.6;else run.exitSpeed=state.speed*3.6;}});
      if(run.progress>=45&&run.progress<=110)run.minSpeed=Math.min(run.minSpeed,state.speed);
    }
    if(scene.preset==='brake'&&state.speed<.1&&state.time>1){run.stopDistance=run.travelDistance;finish('stop');return;}
    if(!freeScenes.includes(scene.preset)&&run.progress>=scene.total-2&&near.distance<scene.width){finish('finish');return;}
    if(run.spinTime>1.0){finish('spin');return;}
    if(state.time>=(scene.timeLimit||60)){finish('timeout');return;}
    if(near.distance>70){finish('far');return;}
    $('track-status').textContent=near.distance>scene.width/2-1?'코스 이탈':'코스 안';$('track-status').style.color=near.distance>scene.width/2-1?'var(--coral)':'var(--muted)';
  }
  function finish(reason){
    running=false;done=true;clearInputs();lockSettings(false);
    const valid=reason==='finish'&&run.offTime<.2;
    const sectors=run.gates[0]!==null&&run.gates[1]!==null?[run.gates[0],run.gates[1]-run.gates[0],state.time-run.gates[1]]:[null,null,null];
    const record={id:Date.now().toString(36)+'-'+Math.random().toString(36).slice(2,7),at:new Date().toISOString(),config:run.config,time:state.time,sectors,valid,reason,offTime:run.offTime,frontPct:100*run.frontTime/Math.max(state.time,.1),rearPct:100*run.rearTime/Math.max(state.time,.1),maxBeta:run.maxBeta,entrySpeed:run.entrySpeed,minSpeed:Number.isFinite(run.minSpeed)?run.minSpeed*3.6:null,exitSpeed:run.exitSpeed,stopDistance:run.stopDistance,paused:run.paused,practice:page==='practice'};
    history.push(record);history=history.slice(-150);saveHistory();
    if(page==='practice'){attempts.push(record);$('attempt-count').textContent=attempts.length;}
    $('play').textContent=page==='practice'&&attempts.length>=5?'세트 완료':'▶ 한 번 더';$('run-status').textContent='연습 완료';$('run-dot').style.background='var(--cyan)';
    showResults(record);updateTelemetry();
  }
  function sameConfig(a,b){return ['preset','vehicle','aids','modelVersion','surface','drive','startSpeed','timeScale','input'].every(k=>a[k]===b[k]);}
  function comparison(r){return history.filter(h=>h.id!==r.id&&h.valid&&h.paused===r.paused&&sameConfig(h.config,r.config)).sort((a,b)=>a.time-b.time)[0];}
  const fmt = v => Number.isFinite(v)?v.toFixed(2):'—';
  const delta = v => Number.isFinite(v)?(v>=0?'+':'')+v.toFixed(2):'—';
  function diagnosis(r){
    if(r.reason==='spin')return ['뒤가 크게 흐르는 상태에 도달했습니다.','이 모델은 큰 스핀을 정확히 재현하지 않습니다. 시작 속도를 낮추고 차체 방향과 이동 방향이 벌어지는 시점을 먼저 찾으세요.'];
    if(r.reason==='stop')return ['정지까지 '+r.stopDistance.toFixed(1)+' m','같은 시작 속도에서 노면 또는 조향량 하나만 바꿔 정지 거리를 비교하세요. 조향하며 제동할 때는 타이어가 두 요구를 함께 처리합니다.'];
    if(r.reason==='timeout'&&freeScenes.includes(r.config.preset))return ['관찰 세션을 마쳤습니다.','드리프트 유지 여부를 점수로 단정하지 않습니다. β가 커질 때 가속·카운터·이동 경로가 어떻게 연결됐는지 확인하세요.'];
    if(r.reason==='timeout')return ['제한 시간 안에 구간을 끝내지 못했습니다.','속도부터 올리기보다 코스와 입력을 익히세요. 마우스의 일정 조향으로 회전 반경을 먼저 확인해도 좋습니다.'];
    if(r.offTime>=.2)return ['코스 이탈이 있었습니다.','진입 속도, 제동 해제, 조향을 함께 보세요. 앞축 요구가 높았는지 뒤 슬립각이 먼저 커졌는지는 오른쪽 표시와 입력 그래프에서 확인할 수 있습니다.'];
    const base=comparison(r);
    if(base&&r.config.preset==='corner'){
      const d=r.sectors.map((v,i)=>v-base.sectors[i]),i=d.indexOf(Math.max(...d));
      if(d[i]>.05)return [['진입','회전','탈출'][i]+' 구간에서 '+d[i].toFixed(2)+'초 늦었습니다.','같은 조건의 이전 최고 주행과 비교했습니다. 구간 시간은 손실 위치를 보여 주며 원인을 확정하지 않습니다. 해당 구간의 입력 하나만 바꿔 다시 달려보세요.'];
      return ['이전 최고 주행과 비슷하거나 빨랐습니다.','이 결과를 다시 만들 수 있는지 확인하세요. 다음 목표는 같은 진입 속도와 브레이크 해제 지점을 반복하는 것입니다.'];
    }
    return ['첫 비교 기준을 만들었습니다.','조건을 고정하고 다시 달려보세요. 다음 기록부터 이전 최고 주행과 구간 차이를 비교합니다.'];
  }
  function showResults(r){
    const [title,body]=diagnosis(r),base=comparison(r),list=page==='practice'?attempts:[r],valids=attempts.filter(x=>x.valid),mean=valids.length?valids.reduce((sum,x)=>sum+x.time,0)/valids.length:0,sd=valids.length>1?Math.sqrt(valids.reduce((sum,x)=>sum+(x.time-mean)**2,0)/valids.length):null;
    $('results').hidden=false;
    $('results').innerHTML='<div class="results-head"><div><p class="eyebrow">DEBRIEF / 원인 하나, 다음 실험 하나</p><h2>'+title+'</h2><p>'+body+'</p></div><span class="tag">'+(r.valid?'유효 주행':r.reason==='stop'?'제동 실험':r.config.preset==='drift'?'관찰 세션':'유효 기록 제외')+'</span></div>'+ '<div class="stat-grid"><div class="stat"><span>시뮬레이션 시간</span><strong>'+fmt(r.time)+'<small>s</small></strong></div><div class="stat"><span>앞축 요구가 한계를 넘은 시간 비율</span><strong>'+Math.round(r.frontPct)+'<small>%</small></strong></div><div class="stat"><span>최대 차체 슬립각 |β|</span><strong>'+r.maxBeta.toFixed(1)+'<small>°</small></strong></div><div class="stat"><span>'+(page==='practice'?'유효 주행 시간의 표준편차':'회전 구간 최소 속도')+'</span><strong>'+(page==='practice'?(sd===null?'—':sd.toFixed(2)):(r.config.preset==='corner'&&Number.isFinite(r.minSpeed)?r.minSpeed.toFixed(1):'—'))+'<small>'+(page==='practice'?'s':'km/h')+'</small></strong></div></div>'+
      (r.config.preset==='corner'?'<div class="table-scroll"><table><thead><tr><th>주행</th><th>진입 0–45m</th><th>회전 45–110m</th><th>탈출 110m–끝</th><th>전체</th><th>상태</th></tr></thead><tbody>'+list.map((a,i)=>'<tr><td>'+(page==='practice'?String(i+1).padStart(2,'0'):'이번')+'</td>'+a.sectors.map(v=>'<td>'+fmt(v)+' s</td>').join('')+'<td>'+fmt(a.time)+' s</td><td class="text '+(a.valid?'good':'bad')+'">'+(a.valid?'유효':'제외')+'</td></tr>').join('')+(base&&r.valid?'<tr><td class="text">이전 최고 대비</td>'+r.sectors.map((v,i)=>'<td class="'+(v-base.sectors[i]>0?'bad':'good')+'">'+delta(v-base.sectors[i])+' s</td>').join('')+'<td>'+delta(r.time-base.time)+' s</td><td class="text">+ 느림 / − 빠름</td></tr>':'')+'</tbody></table></div>':'')+
      '<p class="fineprint">앞축 요구량은 모델 지표이며 곧바로 언더스티어의 원인을 확정하지 않습니다. '+(page==='practice'?'유효 '+valids.length+'/'+attempts.length+'회. 편차는 유효 기록이 2개 이상일 때 계산합니다. ':'')+(r.config.timeScale===.5?'0.5× 관찰 기록입니다. ':'')+(r.paused?'일시정지를 사용한 주행입니다. ':'')+'조건·입력 장치·배속·일시정지 사용 여부가 같은 기록만 비교합니다.</p>'+(storageFailed?'<p class="fineprint coral-text">브라우저 저장을 사용할 수 없어 기록은 현재 탭에서만 유지됩니다. CSV로 보관하세요.</p>':'');
  }
  function updateControlsUI(){
    $('steer-range').value=-controls.steer;$('throttle-range').value=controls.throttle;$('brake-range').value=controls.brake;
    $('handbrake-range').value=controls.handbrake||0;$('clutch-range').value=controls.clutch||0;$('handbrake-out').textContent=Math.round((controls.handbrake||0)*100)+'%';$('clutch-out').textContent=Math.round((controls.clutch||0)*100)+'%';
    $('steer-out').textContent=(state.steer>0?'← ':state.steer<0?'→ ':'')+Math.abs(state.steer*rad).toFixed(1)+'°';$('throttle-out').textContent=Math.round(controls.throttle*100)+'%';$('brake-out').textContent=Math.round(controls.brake*100)+'%';
  }
  function updateTelemetry(){
    $('speed').textContent=Math.round(state.speed*3.6);$('elapsed').textContent=state.time.toFixed(2).padStart(5,'0');
    $('phase').textContent=scene.preset==='corner'?(run.progress<45?'진입':run.progress<110?'회전':'탈출'):scene.preset==='brake'?'제동 실험':scene.total>700?Math.round(run.progress)+' / '+Math.round(scene.total)+' m':scene.preset==='slalom'?'방향 전환':'거동 관찰';
    $('downforce').textContent=Math.round(state.downforce||0)+' N';$('g-force').textContent=(Math.abs(state.ay||0)/9.81).toFixed(2)+' g / '+(state.tcActive?'개입 중':'—');
    for(const [id,val] of [['front-use',state.utilF],['rear-use',state.utilR]]){const el=$(id);el.textContent=Math.round(Math.min(val,9.99)*100)+'%';el.style.color=val>1?'var(--coral)':'var(--text)';}
    for(const part of ['fl','fr','rl','rr']){const val=state['load'+part.toUpperCase()]/initialLoads[part],el=$('load-'+part);el.querySelector('b').textContent=Math.round(val*100)+'%';el.querySelector('i').style.width=clamp(val*50,0,100)+'%';el.querySelector('i').style.background=val>1.1?'var(--lime)':'var(--cyan)';}
    $('beta').innerHTML=(state.beta*rad).toFixed(1)+'<small>°</small>';$('yaw').innerHTML=(state.yawRate*rad).toFixed(1)+'<small>°/s</small>';$('alpha-f').textContent=(state.alphaF*rad).toFixed(1)+'°';$('alpha-r').textContent=(state.alphaR*rad).toFixed(1)+'°';
    let title='입력 하나만 바꿔서 비교하세요.',body='조향을 일정하게 두고 브레이크나 가속을 바꿔 보면, 속도와 앞뒤 타이어 요구량의 관계를 읽기 쉽습니다.',action='실험 → 같은 조건에서 한 변수만 바꾸기';
    if(state.time<.2){title='먼저, 직진하며 제동해 보세요.';body='앞쪽 하중이 늘어도 제동에 접지를 쓰고 있다면, 동시에 요구할 수 있는 회전력에는 한계가 있습니다.';action='실험 → 같은 속도에서 제동량만 바꿔 보기';}
    else if(state.speed<.8){title='멈춘 상태에서는 코너링을 읽기 어려워요.';body='낮은 속도로 다시 출발해 같은 조향을 유지해 보세요. 정지한 바퀴의 방향과 움직이는 차의 반응을 구분합니다.';action='실험 → 조금 가속한 뒤 원을 그리기';}
    else if(Math.abs(state.beta)*rad>12&&state.utilR>1){title='뒤 접지 요구와 차체 슬립각이 큽니다.';body='차의 코와 실제 이동 방향이 벌어졌습니다. 큰 가속을 더하기 전에 뒤 타이어의 요구량과 카운터 조향 방향을 확인하세요.';action='실험 → 가속을 부드럽게 조절하고 두 화살표 관찰';}
    else if(state.utilF>1.1&&Math.abs(state.steer)>.035){title='앞 타이어에 요구가 몰렸습니다.';body='조향을 더해도 회전력이 같은 비율로 늘지 않을 수 있습니다. 진입 속도·제동·조향량 중 무엇이 겹쳤는지 보세요.';action='실험 → 조향을 조금 줄인 상태와 비교';}
    else if(controls.brake>.2&&Math.abs(state.steer)>.03){title='제동과 회전이 접지를 나눠 쓰고 있습니다.';body='브레이크를 놓으면 종방향 요구가 줄지만 하중도 변합니다. 어느 쪽 효과가 더 큰지 차의 회전 반응으로 확인하세요.';action='실험 → 제동 해제를 급하게 / 부드럽게 비교';}
    else if(Math.abs(state.ay)>2){title='바깥쪽 바퀴의 하중을 보세요.';body='선회 중 바깥쪽 하중은 늘고 안쪽은 줄어듭니다. 하중이 증가해도 타이어 접지력이 같은 비율로 증가하는 것은 아닙니다.';action='실험 → 속도를 조금 낮춰 하중과 반경 비교';}
    else if(controls.brake>.2){title='제동으로 앞축 하중이 늘고 있습니다.';body='하지만 제동 자체도 접지 예산을 사용합니다. 앞 하중이 늘었다는 사실만으로 조향 여유가 커졌다고 단정하지 마세요.';action='실험 → 제동 유지 중 조향을 조금 더하기';}
    $('coach-title').textContent=title;$('coach-text').textContent=body;$('coach-action').textContent=action;
    updateControlsUI();
  }
  function saveHistory(){try{localStorage.setItem(storageKey,JSON.stringify(history));storageFailed=false;}catch(_){storageFailed=true;toast('저장 공간을 사용할 수 없습니다. CSV로 기록을 보관할 수 있어요.');}}
  function renderHistory(){
    if(!history.length){$('history-content').innerHTML='<div class="empty-state card"><p class="eyebrow">YOUR FIRST BASELINE</p><h2>아직 완료한 주행이 없습니다.</h2><p>반복 훈련에서 같은 코너를 5번 달려 첫 비교 기준을 만들어 보세요.</p><button class="btn primary" id="history-start">반복 훈련 시작</button></div>';$('history-start').onclick=()=>navigate('practice');return;}
    const names={corner:'90° 코너',brake:'직선 제동',slalom:'슬라럼',hairpin:'헤어핀',drift:'스키드패드',figure8:'8자',monza:'Monza 근사',nurbgp:'Nürburgring GP 근사',rally_stage:'랠리 스테이지',playground:'플레이그라운드',touge:'토우게'},inputs={keyboard:'키보드',mouse:'마우스',gamepad:'패드'};
    $('history-content').innerHTML='<div class="card results"><div class="section-heading"><h2>최근 '+history.length+'회 <small>이 브라우저에 저장 · 최대 150회</small></h2><button class="btn subtle" id="clear-history">기록 지우기</button></div><div class="table-scroll"><table><thead><tr><th>일시</th><th>연습 / 조건</th><th>조작</th><th>진입</th><th>회전</th><th>탈출</th><th>시간</th><th>상태</th></tr></thead><tbody>'+history.slice().reverse().map(r=>'<tr><td>'+new Date(r.at).toLocaleString('ko-KR',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'})+'</td><td class="text">'+names[r.config.preset]+' · '+GripPhysics.surfaces[r.config.surface].label+'<br><small>'+esc(GripPhysics.vehicles?.[r.config.vehicle]?.label||'이전 모델')+' / '+esc(r.config.aids)+' / '+r.config.drive.toUpperCase()+' / '+r.config.startSpeed+' km/h</small></td><td class="text">'+(inputs[r.config.input]||'—')+'<br><small>'+r.config.timeScale+'×'+(r.paused?' / 멈춤 사용':'')+'</small></td>'+r.sectors.map(v=>'<td>'+fmt(v)+'</td>').join('')+'<td>'+fmt(r.time)+' s</td><td class="text '+(r.valid?'good':'bad')+'">'+(r.valid?'유효':r.reason==='stop'?'제동':r.config.preset==='drift'?'관찰':'제외')+'</td></tr>').join('')+'</tbody></table></div><p class="fineprint">조건이 다른 주행의 시간은 직접 비교하지 마세요. 이 기록은 실제 게임 텔레메트리를 분석한 결과가 아닙니다.</p></div>';
    let armed=false;$('clear-history').onclick=()=>{if(!armed){armed=true;$('clear-history').textContent='한 번 더 눌러 전체 삭제';return;}history=[];attempts=[];$('attempt-count').textContent='0';saveHistory();renderHistory();};
  }
  function navigate(next){
    if(!['lab','practice','learn','guide','transfer','history'].includes(next))next='lab';
    const switchingSimulator=['lab','practice'].includes(next)&&simPage!==next;
    if(running){running=false;run.paused=true;clearInputs();$('play').textContent='▶ 계속';$('run-status').textContent='일시정지';$('run-dot').style.background='var(--muted)';}
    page=next;document.querySelectorAll('[data-page]').forEach(el=>{el.classList.toggle('active',el.dataset.page===page);if(el.dataset.page===page)el.setAttribute('aria-current','page');else el.removeAttribute('aria-current');});
    $('sim-page').hidden=!['lab','practice'].includes(page);$('learn-page').hidden=page!=='learn';$('guide-page').hidden=page!=='guide';$('transfer-page').hidden=page!=='transfer';$('history-page').hidden=page!=='history';$('practice-intro').hidden=page!=='practice';
    if(page==='lab'||page==='practice'){
      $('page-kicker').textContent=page==='practice'?'SAME CORNER. FIVE ATTEMPTS.':'OBSERVE → PREDICT → ADJUST';$('page-title').textContent=page==='practice'?'빠른 한 번보다, 같은 다섯 번.':'차의 반응을 읽는 연습.';$('page-intro').textContent=page==='practice'?'조건을 고정하고 진입·회전·탈출의 편차를 줄여보세요.':'한 번에 입력 하나만 바꾸세요. 어디에 접지가 남아 있는지부터 봅니다.';
      if(switchingSimulator){simPage=page;if(page==='practice'){$('preset').value='corner';attempts=[];$('attempt-count').textContent='0';}reset();}
    }
    if(page==='history')renderHistory();
    if(location.hash!=='#'+next)historyReplace(next);
  }
  function historyReplace(next){try{window.history.replaceState(null,'','#'+next);}catch(_){}}
  function toast(message){$('toast').textContent=message;$('toast').hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>$('toast').hidden=true,4200);}
  $('play').onclick=play;$('reset').onclick=()=>{if(page==='practice'&&attempts.length>=5){toast('새 세트로 시작하세요.');return;}reset(true);};
  document.querySelectorAll('[data-page]').forEach(el=>el.onclick=()=>navigate(el.dataset.page));
  document.querySelectorAll('[data-input]').forEach(el=>el.onclick=()=>{setInput(el.dataset.input);settingsChanged();});
  function settingsChanged(){if(page==='practice'&&attempts.length){attempts=[];$('attempt-count').textContent='0';toast('조건이 바뀌어 새 5회 세트를 시작합니다. 이전 주행은 내 기록에 남아 있습니다.');}reset();}
  for(const id of ['vehicle','aids','preset','surface','drive','start-speed','time-scale'])$(id).addEventListener('change',()=>{
    if(id==='vehicle')selectVehicle($('vehicle').value);
    if(id==='preset'){if(page==='practice'&&$('preset').value!=='corner'){$('preset').value='corner';toast('반복 훈련은 동일한 90° 코너를 사용합니다. 다른 장면은 거동 실험실에서 선택하세요.');return;}if(['slalom','hairpin','rally_stage'].includes($('preset').value)){$('surface').value='gravel';$('start-speed').value='40';}else if(freeScenes.includes($('preset').value)||$('preset').value==='touge'){$('surface').value='dry';$('start-speed').value='30';}else{$('surface').value='dry';$('start-speed').value='70';}}
    $('start-speed-out').textContent=$('start-speed').value;settingsChanged();
  });
  $('start-speed').oninput=()=>$('start-speed-out').textContent=$('start-speed').value;
  $('new-session').onclick=()=>{attempts=[];$('attempt-count').textContent='0';reset();};
  $('help-toggle').onclick=()=>{$('quick-help').hidden=!$('quick-help').hidden;$('help-toggle').setAttribute('aria-expanded',String(!$('quick-help').hidden));};
  $('camera-toggle').onclick=()=>{follow=!follow;toast(follow?'차량 추적 카메라':'전체 코스 보기');};
  $('model-details').onclick=()=>{pauseHidden();$('model-dialog').showModal();};
  $('vehicle-details-toggle').onclick=()=>{$('vehicle-details').hidden=!$('vehicle-details').hidden;$('vehicle-details-toggle').setAttribute('aria-expanded',String(!$('vehicle-details').hidden));};
  $('steer-range').oninput=()=>{controls.steer=-Number($('steer-range').value);};$('throttle-range').oninput=()=>{controls.throttle=Number($('throttle-range').value);};$('brake-range').oninput=()=>{controls.brake=Number($('brake-range').value);};
  $('handbrake-range').oninput=()=>{controls.handbrake=Number($('handbrake-range').value);};$('clutch-range').oninput=()=>{controls.clutch=Number($('clutch-range').value);};
  function pointerMove(e){if(pointer!==e.pointerId)return;const rect=$('mouse-pad').getBoundingClientRect(),x=clamp((e.clientX-rect.left)/rect.width,0,1),y=clamp((e.clientY-rect.top)/rect.height,0,1);controls.steer=1-2*x;controls.throttle=Math.max(0,1-2*y);controls.brake=Math.max(0,2*y-1);$('pad-dot').style.left=x*100+'%';$('pad-dot').style.top=y*100+'%';}
  $('mouse-pad').onpointerdown=e=>{pointer=e.pointerId;$('mouse-pad').setPointerCapture(e.pointerId);pointerMove(e);};$('mouse-pad').onpointermove=pointerMove;
  for(const ev of ['pointerup','pointercancel','lostpointercapture'])$('mouse-pad').addEventListener(ev,()=>{clearInputs();updateControlsUI();});
  const driveKeys=['KeyW','KeyA','KeyS','KeyD','KeyE','KeyQ','ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Space','KeyR'];
  document.addEventListener('keydown',e=>{if(!['lab','practice'].includes(page)||$('model-dialog').open||!driveKeys.includes(e.code)||['SELECT','INPUT','TEXTAREA'].includes(e.target.tagName)||e.target.isContentEditable||((e.code==='Space')&&e.target.tagName==='BUTTON'))return;e.preventDefault();if(e.code==='Space'){if(!e.repeat)play();}else if(e.code==='KeyR'){if(!e.repeat)$('reset').click();}else if(inputMode==='keyboard')keys.add(e.code);});
  document.addEventListener('keyup',e=>keys.delete(e.code));
  const pauseHidden=()=>{clearInputs();if(running){running=false;run.paused=true;$('play').textContent='▶ 계속';$('run-status').textContent='일시정지';$('run-dot').style.background='var(--muted)';}};
  window.addEventListener('blur',pauseHidden);document.addEventListener('visibilitychange',()=>{if(document.hidden)pauseHidden();});
  window.addEventListener('hashchange',()=>navigate(location.hash.slice(1)));
  $('export').onclick=()=>{if(!history.length){toast('완료한 주행이 없습니다.');return;}const cols=['date','preset','vehicle','aids','model_version','surface','drive','start_kmh','input','timescale','paused','valid','reason','total_s','entry_s','rotation_s','exit_s','offtrack_s','front_over_demand_pct','max_body_slip_deg','stop_m'];const rows=history.map(r=>[r.at,r.config.preset,r.config.vehicle,r.config.aids,r.config.modelVersion,r.config.surface,r.config.drive,r.config.startSpeed,r.config.input,r.config.timeScale,r.paused,r.valid,r.reason,r.time,...r.sectors,r.offTime,r.frontPct,r.maxBeta,r.stopDistance]);const csv='\ufeff'+[cols,...rows].map(row=>row.map(v=>'"'+String(v??'').replaceAll('"','""')+'"').join(',')).join('\r\n');const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8;'}));const a=document.createElement('a');a.href=url;a.download='grip-lab-runs.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
  function launchLesson(preset,vehicle){navigate('lab');if(vehicle)selectVehicle(vehicle);$('preset').value=presetIds.includes(preset)?preset:'corner';$('preset').dispatchEvent(new Event('change'));window.scrollTo({top:0,behavior:'smooth'});}
  GripLessons.mount($('lessons-root'),{onLaunch:launchLesson,onProgress:()=>{}});
  globalThis.GripGuide?.mount($('guide-root'),{onLaunch:launchLesson});
  globalThis.GripTransfer?.mount($('transfer-root'),{onLaunch:launchLesson});
  reset();setInput('keyboard');navigate(location.hash.slice(1)||'lab');
  function frame(now){
    const dt=lastTime?Math.min((now-lastTime)/1000,.05):0;lastTime=now;
    if(running){inputStep(dt);const simDt=dt*Number($('time-scale').value);GripPhysics.step(state,controls,simDt,{surface:$('surface').value,drive:$('drive').value,aids:$('aids').value});measure(simDt);if(state.time-sampleTime>=.04){sampleTime=state.time;samples.push({t:state.time,...controls,speed:state.speed});while(samples.length&&samples[0].t<state.time-12)samples.shift();trail.push({x:state.x,y:state.y});if(trail.length>1500)trail.shift();}}
    if(now-uiTime>75){uiTime=now;updateTelemetry();}
    if(['lab','practice'].includes(page)){GripRenderer.track($('track'),state,scene,{follow,path:$('show-path').checked,trail,progress:run.progress});GripRenderer.grip($('grip'),state);GripRenderer.trace($('trace'),samples);}
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
})();
