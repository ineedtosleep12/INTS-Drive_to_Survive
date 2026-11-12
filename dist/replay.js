/* Deterministic educational demonstration and distance-aligned trace review.
 * Trace speeds: km/h; heading/wheelSteer/alphaF/alphaR: rad; beta: degrees.
 * A reference is an illustrative controller run, never an optimal lap benchmark.
 */
(function(root){
  'use strict';
  const Physics=root.GripPhysics||(typeof require==='function'?require('./physics.js'):null);
  // Keep the actual model functions: generating a reference must not replace or
  // operate on the live car, even when the app wraps its physics calls for UI.
  const createPhysics=Physics?.create,stepPhysics=Physics?.step;
  const clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
  const finite=Number.isFinite;
  const wrap=x=>Math.atan2(Math.sin(x),Math.cos(x));
  const RAD=180/Math.PI,DT=1/60;
  const FREE=['drift','figure8','playground'];
  const CAVEAT='같은 코스 거리에서 관측한 입력을 비교합니다. 예시는 이 교육용 모델의 조작 방법 하나이며 최적 주행이나 실패 원인을 확정하지 않습니다.';
  const zero=()=>({steer:0,throttle:0,brake:0,handbrake:0,clutch:0});
  function pointOnScene(scene,distance){
    const points=scene.points;
    let lo=1,hi=points.length-1;
    while(lo<hi){const mid=(lo+hi)>>1;if(points[mid].s<distance)lo=mid+1;else hi=mid;}
    const a=points[lo-1],b=points[lo],f=clamp((distance-a.s)/(b.s-a.s),0,1);
    return {x:a.x+(b.x-a.x)*f,y:a.y+(b.y-a.y)*f,heading:Math.atan2(b.y-a.y,b.x-a.x)};
  }
  function controls(state,scene,mission,progress,previousControls,dt){
    if(!state||!scene?.points||scene.points.length<2||!state.params)return zero();
    const previous=previousControls||zero(),seconds=finite(dt)?Math.max(0,dt):DT;
    if(mission?.id==='intro_brake')return {steer:0,throttle:0,brake:state.time>.9?.65:0,handbrake:0,clutch:0};
    const look=clamp(state.speed*.6+4,6,22),target=pointOnScene(scene,progress+look);
    const angle=wrap(Math.atan2(target.y-state.y,target.x-state.x)-state.heading);
    const steer=clamp(Math.atan(2*state.params.wheelbase*Math.sin(angle)/look)/state.params.steeringLimit,-.9,.9);
    const cornerSpeed=(mission?.demoSpeedKph||45)/3.6;
    let speed=cornerSpeed;
    // Guided corners retain approach speed, then decelerate toward the entry
    // target. The 20 m settling distance lets the physical feedback controller
    // release braking before turn-in; it is not a real-world braking marker.
    if(mission?.entrySpeedRange&&finite(scene.gates?.entry)&&progress<scene.gates.entry){
      const approach=(mission.startSpeedKph||mission.demoSpeedKph)/3.6;
      speed=Math.min(approach,Math.sqrt(cornerSpeed*cornerSpeed+2*3.8*Math.max(0,scene.gates.entry-progress-20)));
    }
    // Lesmo's exercise explicitly asks for a faster exit. Allow acceleration in
    // the last third of the rotation interval, still under curvature limits.
    if(mission?.id==='monza_lesmo'&&progress>scene.gates.entry+(scene.gates.exit-scene.gates.entry)*.65){
      speed=Math.max(cornerSpeed,mission.entrySpeedRange[1]/3.6);
    }
    // Curvature anticipation can lower this target earlier when geometry needs
    // it, including an upstream bend in an extended approach segment.
    for(let ahead=0;ahead<=65;ahead+=5){
      const a=pointOnScene(scene,progress+ahead),b=pointOnScene(scene,progress+ahead+8);
      const curvature=Math.abs(wrap(b.heading-a.heading))/8;
      const limit=Math.sqrt((scene.preset==='rally_hairpin'?1.8:3.4)/Math.max(curvature,.001));
      speed=Math.min(speed,Math.sqrt(limit*limit+2*2.5*Math.max(0,ahead-8)));
    }
    const error=speed-state.speed,last=finite(previous.steer)?previous.steer:0;
    return {steer:last+clamp(steer-last,-seconds*2,seconds*2),
      throttle:error>0?clamp(error*.3+.05,0,.6):0,brake:error<-.15?clamp(-error*.16,0,.7):0,handbrake:0,clutch:0};
  }
  function nearest(scene,state,progress){
    let best={distance:Infinity,progress:0};
    for(let i=1;i<scene.points.length;i++){
      const a=scene.points[i-1],b=scene.points[i];
      if(scene.total>700&&!FREE.includes(scene.preset)&&(b.s<progress-45||a.s>progress+100))continue;
      const dx=b.x-a.x,dy=b.y-a.y,l2=dx*dx+dy*dy;
      if(l2<=0)continue;
      const t=clamp(((state.x-a.x)*dx+(state.y-a.y)*dy)/l2,0,1);
      const distance=Math.hypot(state.x-(a.x+t*dx),state.y-(a.y+t*dy));
      if(distance<best.distance)best={distance,progress:a.s+t*Math.sqrt(l2)};
    }
    return best;
  }
  function sample(state,input,progress,off){
    return {t:state.time,s:progress,x:state.x,y:state.y,heading:state.heading,speed:state.speed*3.6,
      steer:input.steer,brake:input.brake,throttle:input.throttle,handbrake:input.handbrake,clutch:input.clutch,
      utilF:state.utilF,utilR:state.utilR,beta:state.beta*RAD,wheelSteer:state.steer,ax:state.ax,ay:state.ay,
      alphaF:state.alphaF,alphaR:state.alphaR,loadFL:state.loadFL,loadFR:state.loadFR,loadRL:state.loadRL,loadRR:state.loadRR,
      downforce:state.downforce,tcActive:state.tcActive,fxF:state.fxF,fyF:state.fyF,fxR:state.fxR,fyR:state.fyR,
      capF:state.capF,capR:state.capR,yawRate:state.yawRate,off};
  }
  function validScene(scene){
    if(!scene||!Array.isArray(scene.points)||scene.points.length<2||!finite(scene.total)||scene.total<=0||!finite(scene.width)||scene.width<=0)return false;
    return scene.points.every((p,i)=>p&&finite(p.x)&&finite(p.y)&&finite(p.s)&&p.s>=0&&(!i||p.s>scene.points[i-1].s));
  }
  function generate(scene,mission,config={}){
    if(!createPhysics||!stepPhysics)return {trace:[],complete:false,reason:'physics_unavailable',events:[],config:null};
    if(!validScene(scene))return {trace:[],complete:false,reason:'invalid_scene',events:[],config:null};
    const settings=config&&typeof config==='object'?config:{};
    const vehicle=Physics.vehicles[settings.vehicle]?settings.vehicle:Physics.vehicles[mission?.vehicle]?mission.vehicle:'gt3';
    const profile=Physics.vehicles[vehicle];
    const surface=Physics.surfaces[settings.surface]?settings.surface:Physics.surfaces[mission?.surface]?mission.surface:profile.defaultSurface;
    const requestedSpeed=finite(settings.startSpeed)?settings.startSpeed:finite(settings.startSpeedKph)?settings.startSpeedKph:finite(mission?.startSpeedKph)?mission.startSpeedKph:profile.startingSpeedKph;
    const startSpeed=clamp(requestedSpeed,0,270),drive=['rwd','fwd','awd'].includes(settings.drive)?settings.drive:profile.drive;
    const aids=['default','off','on',true,false].includes(settings.aids)?settings.aids:mission?.aids??'default';
    const resolved={vehicle,surface,drive,aids,startSpeed,modelVersion:'v2',missionId:mission?.id||scene.trainingId||null};
    const state=createPhysics({vehicle,surface,aids,speed:startSpeed/3.6});
    state.x=scene.points[0].x;state.y=scene.points[0].y;
    state.heading=finite(scene.startHeading)?scene.startHeading:Math.atan2(scene.points[1].y-scene.points[0].y,scene.points[1].x-scene.points[0].x);
    state.drive=drive;
    let input=zero(),progress=0,offTime=0,spinTime=0,reason='timeout',lastOff=false;
    const limit=clamp(finite(scene.timeLimit)&&scene.timeLimit>0?scene.timeLimit:60,DT,900);
    const steps=Math.max(1,Math.floor((limit+1e-9)/DT)),trace=[sample(state,input,0,false)];
    for(let tick=1;tick<=steps;tick++){
      input=controls(state,scene,mission,progress,input,DT);
      stepPhysics(state,input,DT,{surface,drive,aids});
      if(![state.x,state.y,state.time,state.speed,state.heading].every(finite)){reason='invalid_state';break;}
      const near=nearest(scene,state,progress);progress=Math.max(progress,near.progress);
      const off=near.distance>scene.width/2-1;
      if(off)offTime+=DT;
      spinTime=Math.abs(state.beta)*RAD>65&&state.speed>3?spinTime+DT:0;
      if((scene.preset==='brake'||scene.trainingId==='intro_brake'||mission?.id==='intro_brake')&&state.speed<.1&&state.time>1)reason='stop';
      else if(!FREE.includes(scene.preset)&&progress>=scene.total-2&&near.distance<scene.width)reason='finish';
      else if((mission||scene.trainingId)&&offTime>2)reason='offtrack';
      else if(spinTime>1)reason='spin';
      else if(near.distance>70)reason='far';
      else reason='timeout';
      const finished=reason!=='timeout'||tick===steps;
      if(tick%3===0||off&&!lastOff||finished)trace.push(sample(state,input,progress,off));
      lastOff=off;
      if(finished)break;
    }
    // The stopping condition is completeness only, not mission success or pace.
    return {trace,complete:reason==='finish'||reason==='stop',reason,events:events(trace),config:resolved};
  }
  function usable(trace,key){
    if(!Array.isArray(trace)||!trace.length)return false;
    let last=-Infinity;
    for(const p of trace){if(!p||!finite(p[key])||p[key]<last)return false;last=p[key];}
    return true;
  }
  function interpolate(a,b,f){
    if(f<=0)return {...a};if(f>=1)return {...b};
    const out={},keys=new Set([...Object.keys(a),...Object.keys(b)]);
    for(const key of keys){
      const av=a[key],bv=b[key];
      if(typeof av==='number'||typeof bv==='number'){
        // Missing telemetry is kept missing, never silently filled with zero.
        if(!finite(av)||!finite(bv))continue;
        if(['heading','alphaF','alphaR','wheelSteer'].includes(key))out[key]=wrap(av+wrap(bv-av)*f);
        else if(key==='beta')out[key]=wrap((av+wrap((bv-av)/RAD)*RAD*f)/RAD)*RAD;
        else out[key]=av+(bv-av)*f;
      }else if(av!==undefined&&bv!==undefined)out[key]=f<.5?av:bv;
    }
    return out;
  }
  function atTime(trace,time){
    if(!finite(time)||!usable(trace,'t')||time<trace[0].t||time>trace.at(-1).t)return null;
    let lo=0,hi=trace.length-1;
    while(lo<hi){const mid=(lo+hi)>>1;if(trace[mid].t<time)lo=mid+1;else hi=mid;}
    if(trace[lo].t===time||lo===0)return {...trace[lo]};
    const a=trace[lo-1],b=trace[lo];return interpolate(a,b,(time-a.t)/(b.t-a.t));
  }
  function atDistance(trace,distance){
    if(!finite(distance)||!usable(trace,'s')||distance<trace[0].s||distance>trace.at(-1).s)return null;
    // Use the last sample at a repeated distance. A stopped prefix then maps
    // to departure, not an interpolation through several seconds of no motion.
    let lo=0,hi=trace.length;
    while(lo<hi){const mid=(lo+hi)>>1;if(trace[mid].s<=distance)lo=mid+1;else hi=mid;}
    const left=Math.max(0,lo-1),a=trace[left];
    if(a.s===distance||lo===trace.length)return {...a};
    const b=trace[lo];return interpolate(a,b,(distance-a.s)/(b.s-a.s));
  }
  function failureWindow(trace,focusTime,before=4,after=2){
    if(!usable(trace,'t'))return null;
    const first=trace[0].t,last=trace.at(-1).t,focus=clamp(finite(focusTime)?focusTime:last,first,last);
    return {from:Math.max(first,focus-(finite(before)&&before>=0?before:4)),to:Math.min(last,focus+(finite(after)&&after>=0?after:2)),focus};
  }
  function events(trace){
    if(!usable(trace,'t'))return [];
    const result=[];
    let braking=false,brakeCandidate=null,direction=0,turnCandidate=null,previousTime=null;
    function emit(type,p,dir){
      if(!finite(p.s))return;
      const name=type==='brake_start'?'브레이크 시작':type==='brake_release'?'브레이크 놓기':dir>0?'왼쪽 조향 시작':dir<0?'오른쪽 조향 시작':'조향을 가운데로';
      const detail=type==='brake_start'?'브레이크 18% 이상이 0.15초 유지됨':type==='brake_release'?'브레이크 8% 이하가 0.15초 유지됨':dir?'같은 조향 방향이 0.15초 유지됨':'조향이 중립 범위에서 0.15초 유지됨';
      result.push({type,t:p.t,s:p.s,label:name,detail,brake:p.brake,steer:p.steer,...(type==='turn_direction'?{direction:dir}:{})});
    }
    for(const p of trace){
      // Two separated samples do not prove a sustained input across a gap.
      if(previousTime!==null&&p.t-previousTime>.2){brakeCandidate=null;turnCandidate=null;}
      previousTime=p.t;
      if(finite(p.brake)){
        const next=braking?p.brake>.08:p.brake>=.18;
        if(next!==braking){
          if(!brakeCandidate||brakeCandidate.next!==next)brakeCandidate={next,p};
          if(p.t-brakeCandidate.p.t>=.15-1e-8){braking=next;emit(next?'brake_start':'brake_release',brakeCandidate.p);brakeCandidate=null;}
        }else brakeCandidate=null;
      }else brakeCandidate=null;
      if(finite(p.steer)){
        const magnitude=Math.abs(p.steer),sign=Math.sign(p.steer);
        const next=magnitude>=.08?sign:magnitude<=.04?0:direction;
        if(next!==direction){
          if(!turnCandidate||turnCandidate.next!==next)turnCandidate={next,p};
          if(p.t-turnCandidate.p.t>=.15-1e-8){direction=next;emit('turn_direction',turnCandidate.p,direction);turnCandidate=null;}
        }else turnCandidate=null;
      }else turnCandidate=null;
    }
    return result.sort((a,b)=>a.t-b.t||a.type.localeCompare(b.type));
  }
  function steerText(value){
    if(!finite(value))return '미측정';
    if(Math.abs(value)<.04)return '중앙 '+Math.round(Math.abs(value)*100)+'%';
    return (value>0?'왼쪽 ':'오른쪽 ')+Math.round(Math.abs(value)*100)+'%';
  }
  function compare(player,reference,referenceEvents=[]){
    const unavailable={available:false,headline:'같은 거리의 예시 기록이 없습니다.',explanation:'예시가 도달하지 않은 거리이거나 비교에 필요한 기록이 없습니다.',differences:[],events:[],nextAction:'두 기록이 함께 존재하는 코스 거리로 이동해 비교하세요.',caveat:CAVEAT};
    if(!player||!reference||!finite(player.s)||!finite(reference.s)||Math.abs(player.s-reference.s)>.5)return unavailable;
    const differences=[];
    for(const [key,label,unit] of [['brake','브레이크','입력'],['steer','조향','입력'],['throttle','가속','입력'],['speed','속도','km/h']]){
      if(finite(player[key])&&finite(reference[key]))differences.push({key,label,player:player[key],reference:reference[key],delta:player[key]-reference[key],unit});
    }
    if(!differences.length)return unavailable;
    const list=Array.isArray(referenceEvents)?referenceEvents:[];
    const nearby=list.filter(e=>e&&finite(e.s)&&finite(e.t)&&Math.abs(e.s-player.s)<=35).map(e=>({...e}));
    const brake=differences.find(d=>d.key==='brake'),steer=differences.find(d=>d.key==='steer'),speed=differences.find(d=>d.key==='speed');
    const text=[];let headline='같은 거리에서 입력을 나란히 확인하세요.',nextAction='브레이크와 조향 중 한 가지 입력만 바꿔 같은 구간을 다시 달려보세요.';
    if(brake){
      text.push('브레이크는 내 주행 '+Math.round(brake.player*100)+'%, 예시 '+Math.round(brake.reference*100)+'%입니다.');
      if(brake.player>.15&&brake.reference<=.08){headline='이 거리에서 예시는 브레이크를 놓았습니다.';nextAction='예시의 브레이크 해제 위치와 내 입력을 확인한 뒤, 다음에는 해제 시점 하나만 바꿔 보세요.';}
      else if(brake.reference>.18&&brake.player<=.08){headline='이 거리에서 예시는 제동하고 있습니다.';nextAction='예시의 제동 시작 위치를 살펴보고 다음에는 감속 시작 시점 하나만 바꿔 보세요.';}
    }
    if(steer){
      text.push('조향은 내 주행 '+steerText(steer.player)+', 예시 '+steerText(steer.reference)+'입니다.');
      if(Math.abs(steer.player)>.08&&Math.abs(steer.reference)>.08&&Math.sign(steer.player)!==Math.sign(steer.reference)){
        headline='같은 거리에서 조향 방향이 다릅니다.';nextAction='코스의 다음 회전 방향과 예시의 조향 전환 위치를 함께 확인하세요.';
      }else if(Math.abs(steer.player)<.04&&Math.abs(steer.reference)>=.08&&headline==='같은 거리에서 입력을 나란히 확인하세요.'){
        headline='이 거리에서 예시는 이미 조향하고 있습니다.';nextAction='예시의 조향 시작 위치를 확인하고 다음에는 조향 시작 시점만 바꿔 보세요.';
      }
    }
    if(speed)text.push('속도는 내 주행 '+speed.player.toFixed(1)+', 예시 '+speed.reference.toFixed(1)+' km/h입니다.');
    const lastRelease=list.filter(e=>e?.type==='brake_release'&&finite(e.s)&&e.s<=player.s&&player.s-e.s<=45).at(-1);
    if(lastRelease)text.push('예시의 제동 해제 표시는 '+lastRelease.s.toFixed(1)+' m 지점입니다.');
    return {available:true,headline,explanation:text.join(' '),differences,events:nearby,nextAction,caveat:CAVEAT};
  }
  const api=Object.freeze({controls,generate,atTime,atDistance,failureWindow,compare,events});
  root.GripReplay=api;
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
})(typeof globalThis!=='undefined'?globalThis:this);
