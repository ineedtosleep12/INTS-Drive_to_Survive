/* Grip Lab educational vehicle model, SI units.
 * Body +x forward/+y left. World +x east/+y north. Positive steering/yaw = left.
 * Class profiles are teaching assumptions, NOT fitted LMU/WRC/AC car physics.
 * Dynamic bicycle motion + four contact loads + nonlinear combined tire forces.
 * No wheel angular states, RPM/gears, real ABS/TC, clutch kick, thermal or wear model.
 */
(function (root) {
  'use strict';
  const G = 9.81;
  const clamp = (x,a,b) => Math.max(a,Math.min(b,x));
  const finite = (x,fallback=0) => Number.isFinite(x) ? x : fallback;
  const wrap = x => Math.atan2(Math.sin(x),Math.cos(x));
  const surfaces = Object.freeze({
    dry: Object.freeze({label:'드라이 아스팔트',mu:1.08,stiffness:1,rolling:.014}),
    wet: Object.freeze({label:'젖은 아스팔트',mu:.65,stiffness:.76,rolling:.014}),
    gravel: Object.freeze({label:'그래블',mu:.49,stiffness:.47,rolling:.026})
  });
  const capabilities = Object.freeze({
    vehicleProfiles:true,combinedTireForces:true,fourContactLoads:true,loadSensitivity:true,
    speedSquaredAero:true,driveLayout:true,engineBrakingApproximation:true,
    handbrakeRearBraking:true,clutchDisconnect:true,gripBudgetTractionAid:true,
    wheelRotation:false,slipRatio:false,realABS:false,realTC:false,
    engineRPM:false,gears:false,clutchKick:false,fullSpin:false,temperature:false,wear:false,
    suspensionTravel:false,jumps:false,gameSpecificCalibration:false
  });
  const limitations = Object.freeze({
    fidelity:'차종별 교육용 근사입니다. 특정 게임·실차·BoP·셋업에 맞춰 검증하지 않았습니다.',
    tires:'앞뒤 축의 결합 마찰력과 네 바퀴 하중을 근사합니다. 바퀴 회전·슬립률·잠김·실제 ABS는 계산하지 않습니다.',
    tractionAid:'보조는 횡력에 필요한 접지를 남기도록 구동력을 줄이는 학습용 제어입니다. 실제 차량 TC 로직이 아닙니다.',
    handbrake:'핸드브레이크는 뒤축 추가 제동과 그에 따른 횡력 여유 감소만 근사합니다. 잠김·재접지·센터 디퍼렌셜은 재현하지 않습니다.',
    clutch:'클러치 100%는 구동력과 근사 엔진브레이크를 분리합니다. 엔진·플라이휠 에너지가 없어 클러치킥은 재현하지 않습니다.',
    motion:'전진 평면 운동 범위입니다. 90°를 넘는 슬립·완전 스핀·후진·점프·서스펜션 스트로크는 학습 범위 밖입니다.',
    operatingState:'타이어 온도·마모·압력·연료 변화·기어·RPM·FFB는 모델에 없습니다.',
    profiles:'질량·출력의 출처 조건과 모델 가정을 구분했습니다. CG·관성·타이어 곡선·에어로·전자제어 계수는 모두 교육용 가정입니다.'
  });
  function profile(id,data){
    const front=data.frontWeight;
    const p={id,gravity:G,steeringRate:2.1,loadResponse:.11,tireShape:1.35,loadExponent:.90,
      aeroFront:.46,frontDriveShare:.5,powerEfficiency:.90,startingSpeed:70/3.6,
      defaultSurface:'dry',defaultAids:false,lateralTransferFront:front,
      ...data,a:data.wheelbase*(1-front),b:data.wheelbase*front,
      startingSpeedKph:Math.round((data.startingSpeed||70/3.6)*3.6),
      aids:Object.freeze({default:!!data.defaultAids,type:'grip-budget torque aid',realVehicleController:false}),
      assumptions:Object.freeze(data.assumptions||[]),sourceFacts:Object.freeze(data.sourceFacts||[])
    };
    return Object.freeze(p);
  }
  const vehicles=Object.freeze({
    gt3:profile('gt3',{
      label:'GT3 · 프런트엔진 RWD',category:'representative',layout:'FR',mass:1350,wheelbase:2.917,frontWeight:.52,
      height:.40,track:1.70,inertia:2250,power:400000,maxDriveForce:12000,steeringLimit:.47,
      muScale:1.28,cornerStiffnessFront:108000,cornerStiffnessRear:119000,loadExponent:.90,
      downforceCoefficient:2.7,drag:.65,brakeBias:.59,brakeForceRatio:1.65,engineBrakeForce:600,
      drive:'rwd',defaultAids:true,startingSpeed:80/3.6,
      assumptions:['BMW M4 GT3 EVO의 휠베이스를 참고한 전방 엔진 GT3 학습형입니다.','1350 kg 운용 질량·400 kW 엔진 출력·90% 전달효율은 예시 가정입니다. BoP 재현값이 아닙니다.','타이어·무게배분·관성·다운포스·드래그는 교육용 가정입니다.'],
      sourceFacts:[{fact:'BMW M4 GT3 EVO: 휠베이스 2917 mm, 제조사 표기 최대 590 hp. 실제 운용 출력은 조건에 따라 다릅니다.',url:'https://www.bmw-m.com/en/fastlane/motorsport/race-cars/bmw-m4-gt3-evo.html'}]
    }),
    rally1:profile('rally1',{
      label:'Rally1 · AWD',category:'representative',layout:'앞 엔진 / AWD',mass:1350,wheelbase:2.600,frontWeight:.56,
      height:.52,track:1.60,inertia:2050,power:265000,powerEfficiency:.86,maxDriveForce:11000,
      steeringLimit:.60,muScale:1.02,cornerStiffnessFront:82000,cornerStiffnessRear:90000,
      downforceCoefficient:.65,drag:.62,brakeBias:.61,brakeForceRatio:1.35,engineBrakeForce:550,
      drive:'awd',frontDriveShare:.5,defaultSurface:'gravel',startingSpeed:60/3.6,loadResponse:.15,
      assumptions:['2025 Hyundai i20 N Rally1 공개 자료의 탑승자 포함 질량과 휠베이스를 참고했습니다.','약 360 hp를 바탕으로 265 kW를 사용하는 근사입니다. 구동 효율·센터 토크 분배·차체·타이어 계수는 가정입니다.','자갈은 낮은 접지 계수 모델입니다. 타이어의 노면 파고듦·러트·점프는 재현하지 않습니다.'],
      sourceFacts:[{fact:'2025 Hyundai: 최소 1180 kg, 드라이버·코드라이버 포함 1350 kg, 약 360 hp, 휠베이스 2600 mm, 4WD.',url:'https://motorsport.hyundai.com/Corporate%20Site/Car%20Spec/i20%20N%20Rally1/2025/Hyundai%20Rally1%20Tech%20Spec.pdf'}]
    }),
    ae86:profile('ae86',{
      label:'AE86 · 경량 FR',category:'representative',layout:'FR',mass:1015,wheelbase:2.400,frontWeight:.535,
      height:.51,track:1.37,inertia:1450,power:95600,powerEfficiency:.78,maxDriveForce:4600,
      steeringLimit:.60,muScale:.90,cornerStiffnessFront:52000,cornerStiffnessRear:50000,
      loadExponent:.88,downforceCoefficient:0,drag:.42,brakeBias:.64,brakeForceRatio:1.10,
      engineBrakeForce:220,drive:'rwd',startingSpeed:50/3.6,loadResponse:.16,
      assumptions:['1983 Levin 3도어 GTV 공차중량 940 kg에 운전자 75 kg를 가정한 1015 kg 모델입니다.','130 PS는 당시 gross 출력입니다. 95.6 kW × 78%는 교육용 전달출력 가정이며 net/wheel 출력 실측치가 아닙니다.','순정급 출력·로드 타이어를 상정했습니다. 튜닝 드리프트 AE86이나 특정 AC 모드의 재현이 아닙니다.'],
      sourceFacts:[{fact:'Toyota 1983 Corolla Levin GTV: 공차중량 940 kg, 130 PS gross, 휠베이스 2400 mm, FR.',url:'https://www.toyota-global.com/company/history_of_toyota/75years/vehicle_lineage/car/id60003763/index.html'}]
    }),
    hypercar:profile('hypercar',{
      label:'하이퍼카 · RWD 학습형',category:'fictional',layout:'MR',mass:1100,wheelbase:3.15,frontWeight:.46,
      height:.30,track:1.68,inertia:1750,power:500000,powerEfficiency:.92,maxDriveForce:15000,
      steeringLimit:.40,muScale:1.43,cornerStiffnessFront:138000,cornerStiffnessRear:155000,
      downforceCoefficient:4.8,drag:.82,brakeBias:.56,brakeForceRatio:2.25,engineBrakeForce:750,
      drive:'rwd',defaultAids:true,startingSpeed:110/3.6,
      assumptions:['고다운포스 후륜구동 프로토타입의 특성을 비교하는 가상 학습형입니다.','1100 kg·500 kW 등 모든 입력값은 교육용 가정이며 LMH/LMDh 규정·BoP·하이브리드 재현값이 아닙니다.','전륜 모터 활성 속도·회생제동·에너지 운용·하이브리드는 모델에 없습니다.']
    }),
    formula:profile('formula',{
      label:'포뮬러 · 고다운포스 RWD',category:'fictional',layout:'MR',mass:850,wheelbase:3.55,frontWeight:.46,
      height:.28,track:1.60,inertia:1400,power:700000,powerEfficiency:.94,maxDriveForce:17000,
      steeringLimit:.35,muScale:1.55,cornerStiffnessFront:145000,cornerStiffnessRear:180000,
      downforceCoefficient:6.5,drag:.90,brakeBias:.55,brakeForceRatio:2.80,engineBrakeForce:700,
      drive:'rwd',startingSpeed:110/3.6,
      assumptions:['공력 의존성을 비교하는 가상 포뮬러입니다. 특정 연식 F1의 제원·타이어·규정 재현이 아닙니다.','850 kg·700 kW·에어로 등 모든 수치는 교육용 가정입니다. DRS·회생·ERS·액티브 에어로는 없습니다.']
    }),
    touring_fwd:profile('touring_fwd',{
      label:'FF · 전륜구동 학습형',category:'fictional',layout:'FF',mass:1200,wheelbase:2.62,frontWeight:.62,
      height:.50,track:1.55,inertia:1850,power:150000,powerEfficiency:.88,maxDriveForce:7500,
      steeringLimit:.53,muScale:1.02,cornerStiffnessFront:80000,cornerStiffnessRear:61000,
      downforceCoefficient:.15,drag:.48,brakeBias:.67,brakeForceRatio:1.25,engineBrakeForce:420,
      drive:'fwd',assumptions:['앞바퀴가 구동·조향을 함께 담당하는 특성을 비교하는 가상 FF입니다. 모든 수치는 교육용 가정입니다.']
    }),
    midship:profile('midship',{
      label:'MR · 미드십 학습형',category:'fictional',layout:'MR',mass:1200,wheelbase:2.50,frontWeight:.43,
      height:.42,track:1.60,inertia:1550,power:220000,maxDriveForce:9000,
      steeringLimit:.52,muScale:1.04,cornerStiffnessFront:66000,cornerStiffnessRear:100000,
      downforceCoefficient:.25,drag:.48,brakeBias:.58,brakeForceRatio:1.30,engineBrakeForce:430,
      drive:'rwd',assumptions:['뒤쪽 정적 하중과 작은 요 관성의 영향을 비교하는 가상 MR입니다. 모든 수치는 교육용 가정입니다.']
    }),
    rearengine:profile('rearengine',{
      label:'RR · 리어엔진 학습형',category:'fictional',layout:'RR',mass:1300,wheelbase:2.45,frontWeight:.38,
      height:.45,track:1.60,inertia:2100,power:250000,maxDriveForce:9500,
      steeringLimit:.52,muScale:1.04,cornerStiffnessFront:62000,cornerStiffnessRear:118000,
      downforceCoefficient:.35,aeroFront:.35,drag:.50,brakeBias:.56,brakeForceRatio:1.30,
      engineBrakeForce:460,drive:'rwd',
      assumptions:['뒤축 중심 정적 하중과 회전 관성의 영향을 비교하는 가상 RR입니다. 특정 Porsche의 재현이 아닙니다. 모든 수치는 교육용 가정입니다.']
    })
  });
  function resolveAids(value,p,fallback){
    if(typeof value==='boolean')return value;
    if(value==='on')return true;
    if(value==='off')return false;
    if(value==='default')return p.defaultAids;
    return typeof fallback==='boolean'?fallback:p.defaultAids;
  }
  // Axle capacity sums two load-sensitive tire contacts. The exponent below 1
  // means unequal left/right loading reduces their total capacity at fixed load.
  function axleCapacity(left,right,reference,mu,exponent){
    return mu*reference*(Math.pow(left/reference,exponent)+Math.pow(right/reference,exponent));
  }
  function updateLoads(s,p,surface,h){
    const mg=p.mass*G,nf0=mg*p.frontWeight,nr0=mg-nf0;
    if(h){const r=1-Math.exp(-h/p.loadResponse);s._loadAx+=(s.ax-s._loadAx)*r;s._loadAy+=(s.ay-s._loadAy)*r;}
    s.downforce=p.downforceCoefficient*Math.pow(Math.max(0,s.vx),2);
    s.aeroFront=s.downforce*p.aeroFront;s.aeroRear=s.downforce-s.aeroFront;
    s.normalTotal=mg+s.downforce;
    const longitudinal=p.mass*p.height*s._loadAx/p.wheelbase;
    s.Nf=clamp(nf0-longitudinal+s.aeroFront,s.normalTotal*.07,s.normalTotal*.93);
    s.Nr=s.normalTotal-s.Nf;
    const lateral=p.mass*p.height*s._loadAy/p.track;
    const tf=clamp(lateral*p.lateralTransferFront,-.46*s.Nf,.46*s.Nf);
    const tr=clamp(lateral*(1-p.lateralTransferFront),-.46*s.Nr,.46*s.Nr);
    s.loadFL=s.Nf/2-tf;s.loadFR=s.Nf/2+tf;s.loadRL=s.Nr/2-tr;s.loadRR=s.Nr/2+tr;
    s.mu=surface.mu*p.muScale;
    s.capF=axleCapacity(s.loadFL,s.loadFR,nf0/2,s.mu,p.loadExponent);
    s.capR=axleCapacity(s.loadRL,s.loadRR,nr0/2,s.mu,p.loadExponent);
    s.effectiveMuF=s.capF/s.Nf;s.effectiveMuR=s.capR/s.Nr;
    s.staticNf=nf0;s.staticNr=nr0;
  }
  function create(options={}){
    const vehicle=vehicles[options.vehicle]?options.vehicle:'gt3',p=vehicles[vehicle];
    const surface=surfaces[options.surface]?options.surface:p.defaultSurface;
    const speed=clamp(finite(options.speed,p.startingSpeed),0,75);
    const s={vehicle,params:p,x:0,y:0,heading:0,vx:speed,vy:0,yawRate:0,speed,beta:0,
      ax:0,ay:0,longitudinalG:0,lateralG:0,alphaF:0,alphaR:0,utilF:0,utilR:0,
      fxF:0,fxR:0,fyF:0,fyR:0,Nf:0,Nr:0,loadFL:0,loadFR:0,loadRL:0,loadRR:0,
      capF:0,capR:0,mu:0,downforce:0,normalTotal:0,time:0,steer:0,surface,
      throttle:0,brake:0,handbrake:0,clutch:0,drive:p.drive,
      aids:resolveAids(options.aids,p),tcActive:false,aidActive:false,absActive:false,
      tcReduction:0,powerDelivered:0,powerRequested:0,driveForce:0,engineBrakeForce:0,
      handbrakeForce:0,drivelineEngaged:true,_loadAx:0,_loadAy:0,_tcFactor:1,
      model:'educational-profile-bicycle-v2'};
    updateLoads(s,p,surfaces[surface],0);return s;
  }
  function pureLateral(alpha,cap,stiffness,shape){
    return cap*Math.sin(shape*Math.atan(stiffness*alpha/(shape*cap)));
  }
  function tire(alpha,fxRequest,cap,stiffness,shape){
    const fyLinear=stiffness*alpha,fyPure=pureLateral(alpha,cap,stiffness,shape);
    const demand=Math.hypot(fxRequest,fyPure),scale=demand>cap?cap/demand:1;
    return {fx:fxRequest*scale,fy:fyPure*scale,util:Math.hypot(fxRequest,fyLinear)/cap,scale};
  }
  function integrate(s,input,h,surface,drive,p){
    s.steer+=clamp(input.steer*p.steeringLimit-s.steer,-p.steeringRate*h,p.steeringRate*h);
    const c=Math.cos(s.steer),sn=Math.sin(s.steer);
    updateLoads(s,p,surface,h);
    const forward=Math.max(s.vx,2),low=clamp(Math.hypot(s.vx,s.vy)/2,.12,1);
    s.alphaF=wrap(s.steer-Math.atan2(s.vy+p.a*s.yawRate,forward));
    s.alphaR=wrap(-Math.atan2(s.vy-p.b*s.yawRate,forward));
    const cf=p.cornerStiffnessFront*surface.stiffness*Math.pow(s.Nf/s.staticNf,p.loadExponent)*low;
    const cr=p.cornerStiffnessRear*surface.stiffness*Math.pow(s.Nr/s.staticNr,p.loadExponent)*low;
    const frontDrive=drive==='fwd'?1:drive==='awd'?p.frontDriveShare:0;
    const connected=1-input.clutch;
    const requestedEngine=input.throttle*connected*Math.min(p.maxDriveForce,p.power*p.powerEfficiency/Math.max(s.vx,1));
    const engineBrake=p.engineBrakeForce*connected*(1-input.throttle)*clamp(s.vx/3,0,1);
    const braking=input.brake*p.mass*G*p.brakeForceRatio;
    const rearBrake=input.handbrake*p.mass*G*.95;
    s.powerRequested=requestedEngine*Math.max(0,s.vx);s.drivelineEngaged=connected>.02;
    // Grip-budget aid: reserves current lateral demand. No wheel-slip estimate.
    let tcTarget=1;
    if(s.aids && requestedEngine>1){
      let available=Infinity;
      if(frontDrive>0){const fy=pureLateral(s.alphaF,s.capF,cf,p.tireShape);available=Math.min(available,.96*Math.sqrt(Math.max(0,s.capF*s.capF-fy*fy))/frontDrive);}
      if(frontDrive<1){const fy=pureLateral(s.alphaR,s.capR,cr,p.tireShape);available=Math.min(available,.96*Math.sqrt(Math.max(0,s.capR*s.capR-fy*fy))/(1-frontDrive));}
      tcTarget=clamp(available/requestedEngine,0,1);
    }
    const tcResponse=1-Math.exp(-h/(tcTarget < s._tcFactor ? .035 : .15));
    s._tcFactor=s.aids?s._tcFactor+(tcTarget-s._tcFactor)*tcResponse:1;
    const engine=requestedEngine*s._tcFactor;
    s.tcReduction=1-s._tcFactor;s.tcActive=s.aids&&requestedEngine>100&&s.tcReduction>.025;s.aidActive=s.tcActive;
    s.driveForce=engine;s.engineBrakeForce=engineBrake;s.handbrakeForce=rearBrake;
    // Holding the brake at rest does not create inertial load transfer.
    if(s.speed<.07 && Math.abs(s.vy)<.07 && engine<=braking+rearBrake+engineBrake){
      s.vx=0;s.vy=0;s.yawRate=0;s.speed=0;s.beta=0;s.ax=0;s.ay=0;s.longitudinalG=0;s.lateralG=0;
      s.alphaF=0;s.alphaR=0;s.fxF=0;s.fxR=0;s.fyF=0;s.fyR=0;s.utilF=0;s.utilR=0;
      s.powerDelivered=0;s.time+=h;return;
    }
    const f=tire(s.alphaF,(engine-engineBrake)*frontDrive-braking*p.brakeBias,s.capF,cf,p.tireShape);
    const r=tire(s.alphaR,(engine-engineBrake)*(1-frontDrive)-braking*(1-p.brakeBias)-rearBrake,s.capR,cr,p.tireShape);
    s.fxF=f.fx;s.fyF=f.fy;s.fxR=r.fx;s.fyR=r.fy;s.utilF=f.util;s.utilR=r.util;
    s.powerDelivered=engine*(frontDrive*f.scale+(1-frontDrive)*r.scale)*Math.max(0,s.vx);
    const resistance=p.drag*s.vx*Math.abs(s.vx)+surface.rolling*p.mass*G*Math.tanh(s.vx/.4);
    const fx=f.fx*c-f.fy*sn+r.fx-resistance,fy=f.fy*c+f.fx*sn+r.fy;
    s.ax=fx/p.mass;s.ay=fy/p.mass;s.longitudinalG=s.ax/G;s.lateralG=s.ay/G;
    const oldVx=s.vx,oldVy=s.vy,oldYaw=s.yawRate;
    s.vx+=(s.ax+oldYaw*oldVy)*h;s.vy+=(s.ay-oldYaw*oldVx)*h;
    s.yawRate+=(p.a*(f.fy*c+f.fx*sn)-p.b*r.fy)/p.inertia*h;
    if(s.vx<0)s.vx=0;
    if(s.vx<.08 && input.throttle<.02){s.vy*=Math.exp(-h*9);s.yawRate*=Math.exp(-h*9);if(input.brake>.01||input.handbrake>.01||s.vx<.01)s.vx=0;}
    s.heading=wrap(s.heading+s.yawRate*h);
    const ch=Math.cos(s.heading),sh=Math.sin(s.heading);
    s.x+=(s.vx*ch-s.vy*sh)*h;s.y+=(s.vx*sh+s.vy*ch)*h;
    s.speed=Math.hypot(s.vx,s.vy);s.beta=s.speed>.25?Math.atan2(s.vy,s.vx):0;s.time+=h;
  }
  function step(state,controls={},dt=1/60,options={}){
    const s=state,seconds=clamp(finite(dt),0,.25);if(!seconds)return s;
    const vehicle=vehicles[s.vehicle]?s.vehicle:'gt3',p=vehicles[vehicle];
    const surfaceKey=surfaces[options.surface]?options.surface:(surfaces[s.surface]?s.surface:p.defaultSurface),surface=surfaces[surfaceKey];
    const drive=['rwd','fwd','awd'].includes(options.drive)?options.drive:(s.drive||p.drive);
    const input={steer:clamp(finite(controls.steer),-1,1),throttle:clamp(finite(controls.throttle),0,1),brake:clamp(finite(controls.brake),0,1),handbrake:clamp(finite(controls.handbrake),0,1),clutch:clamp(finite(controls.clutch),0,1)};
    s.vehicle=vehicle;s.params=p;s.surface=surfaceKey;s.drive=drive;s.aids=resolveAids(options.aids,p,s.aids);
    s.throttle=input.throttle;s.brake=input.brake;s.handbrake=input.handbrake;s.clutch=input.clutch;
    const n=Math.ceil(seconds/(1/240)),h=seconds/n;
    for(let i=0;i<n;i++)integrate(s,input,h,surface,drive,p);
    return s;
  }
  const api=Object.freeze({create,step,surfaces,vehicles,capabilities,limitations,constants:vehicles.gt3});
  root.GripPhysics=api;
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
})(typeof globalThis!=='undefined'?globalThis:this);

