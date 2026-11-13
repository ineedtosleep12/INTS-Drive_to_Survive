/* Corner repeats use recorded physical checkpoints, never reconstructed poses.
 * capture(state, controls, progress, off) -> detached snapshot; sample(snapshot)
 * plan({trace,snapshots,scene,focusTime,config}) -> fixed checkpoint/finish or null
 * restore(plan) -> detached {state,controls,progress}; only simulation clock resets
 * summarize(plan, trace, reason, {paused}) -> {conditionKey,trace,metrics,...}
 * compare(plan, previousSummary, currentSummary) -> {available,headline,items,note}
 * Partial repetitions must be kept outside full-mission records by the caller.
 */
(function(root){
  'use strict';
  const finite=Number.isFinite,clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
  const clone=value=>value===undefined?undefined:JSON.parse(JSON.stringify(value));
  const numeric=(v,d=1)=>finite(v)?v.toFixed(d):'미측정';
  const configFields=['preset','missionId','vehicle','surface','drive','aids','modelVersion','startSpeed','timeScale','input'];
  function capture(state,controls,progress,off=false){
    if(!state||![state.time,state.x,state.y,state.heading,state.vx,state.vy,state.speed,progress].every(finite))return null;
    // params is immutable profile metadata. Every dynamic/internal state is copied.
    const copied={};for(const key of Object.keys(state))if(key!=='params')copied[key]=clone(state[key]);
    if(state.params)copied.params=state.params;
    return {t:state.time,s:progress,state:copied,controls:clone(controls),off:!!off};
  }
  function sample(snapshot){
    if(!snapshot)return null;
    const st=snapshot.state,c=snapshot.controls||{},out={t:snapshot.t,s:snapshot.s,x:st.x,y:st.y,heading:st.heading,speed:st.speed*3.6,beta:st.beta*180/Math.PI,wheelSteer:st.steer,off:snapshot.off};
    for(const k of ['steer','brake','throttle','handbrake','clutch'])out[k]=c[k];
    for(const k of ['utilF','utilR','ax','ay','alphaF','alphaR','loadFL','loadFR','loadRL','loadRR','downforce','tcActive','fxF','fyF','fxR','fyR','capF','capR','yawRate'])out[k]=st[k];
    return out;
  }
  function clean(trace){
    const out=[];
    for(const p of Array.isArray(trace)?trace:[]){
      if(!p||!finite(p.t)||!finite(p.s)||p.t<0||p.s<0)continue;
      if(out.length&&(p.t<=out.at(-1).t||p.s<out.at(-1).s-1e-6))continue;
      out.push({...p});
    }
    return out;
  }
  function at(trace,s){
    if(!trace.length||s<trace[0].s-1e-6||s>trace.at(-1).s+1e-6)return null;
    const i=trace.findIndex(p=>p.s>=s-1e-6);if(i<0)return null;
    const b=trace[i];if(i===0||Math.abs(b.s-s)<1e-6)return {...b};
    const a=trace[i-1],f=(s-a.s)/(b.s-a.s),out={s};
    for(const k of Object.keys(a)){if(k==='s')continue;if(finite(a[k])&&finite(b[k]))out[k]=a[k]+f*(b[k]-a[k]);else if(typeof a[k]==='boolean'&&typeof b[k]==='boolean')out[k]=f<.5?a[k]:b[k];}
    return out;
  }
  function slice(trace,from,to){
    const a=at(trace,from),b=at(trace,to);if(!a||!b||to<from)return [];
    return [a,...trace.filter(p=>p.s>from+1e-6&&p.s<to-1e-6),...(b.t>a.t?[b]:[])];
  }
  function plan({trace,snapshots,scene,focusTime,config}={}){
    const points=clean(trace);
    if(!scene||!finite(scene.total)||scene.total<=0||points.length<2||!Array.isArray(snapshots)||scene.freePractice||['drift','figure8','playground','intro_brake','brake'].includes(scene.trainingId||scene.preset))return null;
    const focus=points.reduce((best,p)=>Math.abs(p.t-focusTime)<Math.abs(best.t-focusTime)?p:best,points.at(-1));
    const refs=(scene.turnInReferences||[]).filter(r=>finite(r.s)).sort((a,b)=>a.s-b.s);
    let entry,exit,label;
    if(scene.gates&&scene.trainingId!=='whole_monza'&&scene.total<1000){entry=scene.gates.entry;exit=scene.gates.exit;label=scene.name;}
    else if(refs.length){
      let ref=refs.filter(r=>r.s<=focus.s+35).at(-1)||refs[0];
      const upcoming=refs.find(r=>r.s>focus.s+35);
      // An incident in the next corner's braking approach should not rewind to
      // a previous corner whose exit is already well behind the car.
      if(upcoming&&focus.s>ref.s+150&&upcoming.s-focus.s<=300)ref=upcoming;
      const next=refs.find(r=>r.s>ref.s+1);
      entry=ref.s;exit=Math.min(entry+150,next?next.s-25:scene.total-2);label=ref.cornerName;
    }else{entry=Math.max(points[0].s,focus.s-10);exit=Math.min(scene.total-2,focus.s+90);label=scene.name||'선택 코너';}
    if(!finite(entry)||!finite(exit)||exit<=entry)return null;
    const around=points.find(p=>p.s>=entry)||focus,speed=Math.max(5,(around.speed||focus.speed||50)/3.6);
    let desired=Math.min(focus.s-15,entry-Math.max(50,speed*4));
    // Start before the local braking action, with enough distance to change it.
    const events=root.GripReplay?.events(points)||[],brakes=events.filter(e=>e.type==='brake_start'&&e.s<=entry&&e.s>=entry-500);
    if(brakes.length)desired=Math.min(desired,brakes.at(-1).s-Math.max(12,speed));
    const candidates=snapshots.filter(p=>p&&finite(p.t)&&finite(p.s)&&p.t<focus.t-.3&&p.s<entry&&p.s>=points[0].s-1e-6&&!p.off&&p.state&&finite(p.state.beta)&&Math.abs(p.state.beta)<12*Math.PI/180&&p.state.speed>1);
    if(!candidates.length)return null;
    // If the ring buffer has discarded the pre-braking checkpoint, refuse the
    // repeat instead of moving its start into a later, already failing state.
    const eligible=candidates.filter(p=>p.s<=desired),originalStart=candidates.find(p=>Math.abs(p.t-points[0].t)<1e-6&&Math.abs(p.s-points[0].s)<1e-6);
    const selected=eligible.at(-1)||originalStart;if(!selected)return null;
    if(exit-selected.s<20)return null;
    const checkpoint=clone(selected),dynamicState={...checkpoint.state};delete dynamicState.params;
    const conditionKey=JSON.stringify({config:configFields.map(k=>[k,config?.[k]??null]),scene:scene.id||scene.trainingId||scene.preset,total:scene.total,startS:checkpoint.s,state:dynamicState,controls:checkpoint.controls,end:exit});
    return {id:conditionKey,conditionKey,checkpoint,startS:selected.s,endS:exit,entryS:entry,exitS:exit,label:label||'선택 코너',note:'같은 위치·속도·차량 상태에서 재시작합니다. 이전 시도와 공통으로 주행한 구간을 비교합니다.'};
  }
  function restore(plan){
    if(!plan?.checkpoint)return null;
    const cp=clone(plan.checkpoint);cp.state.time=0;
    return {state:cp.state,controls:cp.controls,progress:cp.s};
  }
  const meanByTime=(trace,fn)=>{
    let sum=0,time=0;
    for(let i=1;i<trace.length;i++){const a=fn(trace[i-1]),b=fn(trace[i]),dt=trace[i].t-trace[i-1].t;if(!finite(a)||!finite(b)||dt<=0||dt>.3)return null;sum+=(a+b)*.5*dt;time+=dt;}
    return time?sum/time:null;
  };
  function metrics(plan,trace){
    const duration=trace.length>1?trace.at(-1).t-trace[0].t:0,entry=at(trace,plan.entryS),exit=at(trace,plan.endS),events=root.GripReplay?.events(trace)||[];
    const brake=events.find(e=>e.type==='brake_start'),release=events.find(e=>e.type==='brake_release'),turn=events.find(e=>e.type==='turn_direction'&&e.direction);
    const off=meanByTime(trace,p=>typeof p.off==='boolean'?Number(p.off):null),front=meanByTime(trace,p=>finite(p.utilF)?Number(p.utilF>1):null),rear=meanByTime(trace,p=>finite(p.utilR)?Number(p.utilR>1):null);
    const slips=trace.map(p=>finite(p.beta)?Math.abs(p.beta):null).filter(finite);
    return {elapsed:duration,offTime:off===null?null:off*duration,frontPct:front===null?null:front*100,rearPct:rear===null?null:rear*100,peakSlip:slips.length?Math.max(...slips):null,entrySpeed:entry?.speed??null,exitSpeed:exit?.speed??null,brakeAtEntry:finite(entry?.brake)?entry.brake*100:null,steerAtEntry:finite(entry?.steer)?entry.steer*100:null,brakeStartS:brake?.s??null,brakeReleaseS:release?.s??null,turnStartS:turn?.s??null};
  }
  function summarize(plan,trace,reason,options={}){
    if(!plan)return null;
    const points=clean(trace),end=Math.min(plan.endS,points.at(-1)?.s??plan.startS),window=slice(points,plan.startS,end),values=metrics(plan,window),completed=end>=plan.endS-1e-6&&window.length>1;
    const cleanFinish=completed&&finite(values.offTime)&&values.offTime<.2&&['finish','segment'].includes(reason);
    return {conditionKey:plan.conditionKey,startS:plan.startS,endS:plan.endS,coverageEndS:window.length?end:plan.startS,coveredM:window.length?end-plan.startS:0,completed,clean:cleanFinish,reason,paused:!!options.paused,trace:window,metrics:values};
  }
  function compare(plan,previous,current){
    if(!plan||!previous||!current||previous.conditionKey!==plan.conditionKey||current.conditionKey!==plan.conditionKey)return {available:false,headline:'같은 시작 조건의 이전 시도가 필요합니다.',items:[],note:'다른 코너·차량·시작 조건의 기록은 비교하지 않습니다.'};
    const end=Math.min(previous.coverageEndS,current.coverageEndS,plan.endS),oldTrace=slice(previous.trace,plan.startS,end),newTrace=slice(current.trace,plan.startS,end);
    if(end-plan.startS<5||oldTrace.length<2||newTrace.length<2)return {available:false,headline:'비교할 공통 주행 구간이 아직 짧습니다.',items:[],note:'같은 시작점에서 5 m 이상 주행한 뒤 비교합니다.'};
    const before=metrics(plan,oldTrace),after=metrics(plan,newTrace),items=[];
    function add(key,label,unit,good,threshold=.05){
      const a=before[key],b=after[key];if(!finite(a)||!finite(b))return;
      const delta=b-a,assessment=Math.abs(delta)<=threshold?'same':good===0?'changed':delta*good>0?'better':'worse';
      let text=label+' '+numeric(a)+' → '+numeric(b)+' '+unit;
      if(key.endsWith('S'))text=label+' '+(Math.abs(delta)<=threshold?'비슷한 위치':numeric(Math.abs(delta))+' m '+(delta<0?'앞당김':'늦춤'));
      items.push({key,label,previous:a,current:b,delta,unit,assessment,text});
    }
    add('offTime','코스 밖에 있던 시간','초',-1,.05);
    // Less slip/demand can follow slower driving or suppress useful rally
    // rotation. Report the observation without declaring universal improvement.
    add('peakSlip','최대 차체 슬립각','°',0,.5);add('frontPct','앞축 접지 요구 초과','%',0,1);add('rearPct','뒤축 접지 요구 초과','%',0,1);
    add('entrySpeed','회전 기준선 속도','km/h',0,.5);add('brakeAtEntry','회전 기준선 브레이크','%',0,2);add('steerAtEntry','회전 기준선 조향 (왼쪽 +)','%',0,2);
    add('brakeStartS','제동 시작','m',0,1);add('brakeReleaseS','브레이크 놓기','m',0,1);add('turnStartS','조향 시작','m',0,1);
    if(previous.clean&&current.clean&&!previous.paused&&!current.paused){add('elapsed','구간 통과 시간','초',-1,.03);add('exitSpeed','탈출 속도','km/h',0,.5);}
    const good=items.filter(i=>i.assessment==='better'),bad=items.filter(i=>i.assessment==='worse');
    let headline=current.clean&&!previous.clean?'이번에는 정해진 구간을 코스 안에서 통과했습니다.':previous.clean&&!current.clean?'이전에는 통과한 구간을 이번에는 마치지 못했습니다.':good.length&&!bad.length?'이탈 시간 또는 완주한 구간의 기록이 개선됐습니다.':bad.length&&!good.length?'이탈 시간과 구간 기록을 다시 확인하세요.':good.length&&bad.length?'좋아진 지표와 다시 확인할 지표가 함께 있습니다.':'입력 변화와 차량 반응을 같은 위치에서 비교하세요.';
    if(!current.completed&&!previous.completed&&current.coveredM>previous.coveredM+5)headline='이번에는 이전보다 '+numeric(current.coveredM-previous.coveredM)+' m 더 진행했습니다.';
    return {available:true,headline,items,commonFromS:plan.startS,commonToS:end,previousCoveredM:previous.coveredM,currentCoveredM:current.coveredM,note:'공통 '+numeric(end-plan.startS,0)+' m 비교 · 입력 변화와 결과의 관찰값이며 원인을 확정하지 않습니다. '+(previous.clean&&current.clean&&!previous.paused&&!current.paused?'두 시도 모두 구간을 통과해 시간을 비교합니다.':'시간 순위는 두 시도 모두 코스 안에서, 일시정지 없이 통과한 뒤 표시합니다.')};
  }
  const api=Object.freeze({capture,sample,plan,restore,summarize,compare});root.GripRetry=api;
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
})(typeof globalThis!=='undefined'?globalThis:this);
