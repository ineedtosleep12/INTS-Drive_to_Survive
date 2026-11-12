/* Original, coarse teaching centerlines. No game files or map artwork are bundled. */
(function(root){
  'use strict';
  const APPROX='공식 레이아웃 참고·평면 근사';
  const BOARD_NOTE='회전 기준선까지 모델 거리 · 실제 코스의 제동 지점이 아닙니다.';
  const BOARD_DISTANCES=[200,150,100,50];
  // These offsets define teaching gates on our schematic, not surveyed turn-in points.
  const TURN_IN_BEFORE={monza:{rettifilo:80,lesmo1:110},nurbgp:{t1:75}};
  const MONZA='https://www.monzanet.it/en/circuit/';
  const NURB='https://www.nuerburgring.de/info/nuerburgring/race-tracks/grand-prix-track';
  const MONZA_MAP='https://www.monzanet.it/media/2023/05/2023_Brochure_Experience_Monza_compressed.pdf';
  const NURB_MAP='https://www.nuerburgring-langstrecken-serie.de/wp-content/uploads/2023/08/2023-Regulations-PuE-V5.pdf';
  const distance=(a,b)=>Math.hypot(b.x-a.x,b.y-a.y);
  const xy=a=>({x:a[0],y:a[1]});
  const definitions={
    monza:{name:'몬자 GP',type:'circuit',width:12,closed:true,length:5793,timeLimit:600,
      description:'긴 직선, 시케인과 두 레스모를 연결하는 한 바퀴. '+APPROX,
      url:MONZA,mapUrl:MONZA_MAP,
      // Hand-authored schematic: Rettifilo R-L; Biassono R; Roggia L-R;
      // Lesmo R/R; Serraglio L; Ascari L-R-L; Alboreto R. Not surveyed coordinates.
      nodes:[[0,0],[200,0],[550,0],[750,0],[780,-8],[787,-48],[808,-58],[827,-48],[834,-23],[855,-15],[940,-18],[1110,-55],[1260,-130],[1370,-240],[1430,-380],[1440,-500],[1430,-560],[1452,-579],[1454,-613],[1433,-632],[1422,-676],[1414,-750],[1395,-798],[1352,-820],[1280,-824],[1198,-825],[1160,-811],[1144,-775],[1120,-749],[1000,-697],[850,-642],[600,-560],[493,-525],[465,-539],[442,-570],[402,-580],[364,-557],[330,-544],[276,-554],[160,-578],[-130,-637],[-340,-679],[-408,-672],[-458,-635],[-482,-573],[-484,-480],[-467,-374],[-431,-264],[-376,-158],[-306,-66],[-216,-12],[-150,0]],
      corners:[['rettifilo','레티필로 시케인',5],['biassono','비아소노',14],['roggia','로지아 시케인',18],['lesmo1','레스모 1',23],['lesmo2','레스모 2',27],['serraglio','세랄리오',31],['ascari','아스카리',36],['alboreto','알보레토 / 파라볼리카',45]]},
    nurbgp:{name:'뉘르부르크링 GP',type:'circuit',width:12,closed:true,length:5148,timeLimit:600,
      description:'AMG 아레나와 뮐렌바흐 구간을 포함한 GP 전체 한 바퀴. '+APPROX,
      url:NURB,mapUrl:NURB_MAP,
      // Full GP arrangement, including the southern loop; not the Sprint/Nordschleife.
      // Names identify approximate regions, never real-world braking/apex positions.
      nodes:[[0,0],[180,0],[400,0],[465,-5],[488,-32],[484,-60],[457,-78],[388,-92],[332,-110],[308,-141],[306,-192],[326,-220],[366,-228],[405,-225],[438,-248],[452,-290],[455,-342],[476,-380],[525,-397],[581,-411],[624,-446],[647,-510],[665,-635],[678,-778],[674,-850],[648,-895],[604,-913],[564,-899],[541,-866],[529,-798],[510,-735],[473,-693],[433,-656],[411,-606],[400,-545],[378,-495],[338,-466],[272,-452],[201,-440],[151,-408],[132,-354],[111,-310],[56,-280],[-64,-225],[-188,-170],[-236,-150],[-270,-150],[-287,-129],[-307,-120],[-331,-124],[-360,-109],[-378,-80],[-377,-45],[-360,-18],[-331,-4],[-280,0],[-210,0],[-120,0]],
      corners:[['t1','T1 헤어핀',4],['arena','AMG 아레나',11],['t5t6','T5–T6 연결',19],['hairpin','뮐렌바흐 헤어핀',26],['schumacher','슈마허 S',33],['t10t11','T10–T11 연결',39],['chicane','마지막 시케인',47],['last','최종 코너',53]]},
    rally_stage:{name:'숲길 랠리 스테이지',type:'rally',width:10,closed:false,length:680,timeLimit:180,
      description:'좌우 중속 코너와 좁아지는 회전을 연결한 680 m 가상 스테이지.',
      nodes:[[0,0],[45,0],[85,8],[115,36],[145,54],[180,48],[204,18],[225,-12],[254,-20],[284,-5],[310,31],[338,63],[369,67],[398,42],[410,4],[428,-19],[461,-18],[492,5],[520,23],[565,23]],
      corners:[['r1','완만한 좌회전',3],['r2','연속 우회전',6],['r3','긴 좌회전',11],['r4','좁은 우회전',14],['r5','탈출 좌회전',17]]},
    playground:{name:'드리프트 플레이그라운드',type:'drift',width:80,closed:false,length:null,timeLimit:90,freePractice:true,
      description:'넓은 가상 패드에서 원 선회와 반대 조향을 자유롭게 연습합니다.',
      // The 28 m radius guide with 80 m road width fills the center as an open pad.
      nodes:Array.from({length:25},(_,i)=>{const a=-Math.PI/2+i*Math.PI*2/24;return [28*Math.cos(a),28+28*Math.sin(a)];}),
      corners:[]},
    touge:{name:'산길 연결 코너',type:'drift',width:8,closed:false,length:780,timeLimit:180,
      description:'서로 다른 반경과 두 헤어핀을 잇는 780 m 가상 산길. 고저차는 모사하지 않습니다.',
      nodes:[[0,0],[42,0],[76,12],[94,35],[94,62],[73,80],[48,82],[31,101],[33,126],[61,144],[111,146],[153,159],[174,185],[170,211],[147,228],[116,229],[95,246],[100,271],[129,284],[174,277],[211,252],[237,251],[255,274],[259,309],[279,334],[322,341]],
      corners:[['t1','첫 헤어핀',5],['t2','탈출 전환',8],['t3','두 번째 헤어핀',14],['t4','좁은 전환',17],['t5','긴 탈출',22]]}
  };

  function tangent(nodes,i,closed){
    const n=nodes.length,p=nodes[i],prev=nodes[(i+n-1)%n],next=nodes[(i+1)%n];
    if(!closed&&i===0)return {x:next.x-p.x,y:next.y-p.y};
    if(!closed&&i===n-1)return {x:p.x-prev.x,y:p.y-prev.y};
    const dx=next.x-prev.x,dy=next.y-prev.y;
    const scale=Math.min(distance(prev,p),distance(p,next))*.8/Math.max(Math.hypot(dx,dy),1e-9);
    return {x:dx*scale,y:dy*scale};
  }
  function smooth(nodes,closed){
    const points=[{...nodes[0]}],count=nodes.length-(closed?0:1);
    for(let i=0;i<count;i++){
      const a=nodes[i],b=nodes[(i+1)%nodes.length],ta=tangent(nodes,i,closed),tb=tangent(nodes,(i+1)%nodes.length,closed);
      const steps=Math.max(12,Math.ceil(distance(a,b)/1.5));
      for(let j=1;j<=steps;j++){
        const t=j/steps,t2=t*t,t3=t2*t,h0=2*t3-3*t2+1,h1=t3-2*t2+t,h2=-2*t3+3*t2,h3=t3-t2;
        points.push({x:h0*a.x+h1*ta.x+h2*b.x+h3*tb.x,y:h0*a.y+h1*ta.y+h2*b.y+h3*tb.y});
      }
    }
    return points;
  }
  function measure(points){
    let total=0;points[0].s=0;
    for(let i=1;i<points.length;i++){total+=distance(points[i-1],points[i]);points[i].s=total;}
    return total;
  }
  function resample(dense,step){
    const total=measure(dense),count=Math.ceil(total/step),result=[{...dense[0]}];
    let index=1;
    for(let i=1;i<count;i++){
      const s=total*i/count;while(index<dense.length-1&&dense[index].s<s)index++;
      const a=dense[index-1],b=dense[index],t=(s-a.s)/(b.s-a.s);
      result.push({x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t});
    }
    result.push({...dense[dense.length-1]});return result;
  }
  function make(id){
    if(!Object.prototype.hasOwnProperty.call(definitions,id))return null;
    const d=definitions[id];
    const controls=d.nodes.map(xy),dense=smooth(controls,d.closed),rawLength=measure(dense);
    const initialScale=d.length?d.length/rawLength:1;
    for(const p of dense){p.x*=initialScale;p.y*=initialScale;}
    const points=resample(dense,5),sampleLength=measure(points),correction=d.length?d.length/sampleLength:1;
    const origin={x:points[0].x,y:points[0].y};
    for(const p of points){p.x=(p.x-origin.x)*correction;p.y=(p.y-origin.y)*correction;}
    if(d.closed){points[points.length-1].x=0;points[points.length-1].y=0;}
    const total=measure(points),scale=initialScale*correction;
    const corners=d.corners.map(([cornerId,name,index])=>{
      const target={x:(controls[index].x*initialScale-origin.x)*correction,y:(controls[index].y*initialScale-origin.y)*correction};
      let best=points[0];for(const p of points)if(distance(p,target)<distance(best,target))best=p;
      return {id:cornerId,name,s:best.s};
    }).sort((a,b)=>a.s-b.s);
    const turnInReferences=d.type==='circuit'?corners.map(c=>{
      const offsetBeforeCornerM=TURN_IN_BEFORE[id]?.[c.id]??80;
      return {id:c.id+'-turn-in',cornerId:c.id,cornerName:c.name,s:c.s-offsetBeforeCornerM,label:'회전 기준선',modelOnly:true,offsetBeforeCornerM};
    }):[];
    const brakingBoards=turnInReferences.flatMap(ref=>BOARD_DISTANCES.map(m=>({s:ref.s-m,distance:m,label:m+' m',targetS:ref.s,cornerName:ref.cornerName})))
      .filter(board=>board.s>=0&&board.targetS<=total).sort((a,b)=>a.s-b.s);
    const source={url:d.url||null,officialLengthM:d.url?d.length:null,approximation:true,
      mapUrl:d.mapUrl||null,label:d.url?APPROX:'가상 연습 코스',
      brakingBoardNote:d.type==='circuit'?BOARD_NOTE:'',
      turnInReferenceNote:d.type==='circuit'?'코너 대표 위치 앞에 학습용 회전 기준선을 정의했습니다. 표지판 숫자는 이 모델 중심선을 따라 기준선까지 남은 거리이며 실제 회전·제동 위치를 뜻하지 않습니다.':'',
      turnInReferences:turnInReferences.map(ref=>({...ref})),
      notes:d.url?APPROX+' · 공식 공개 코너 순서와 개요도를 참고해 직접 작성한 중심선입니다. 전체 길이만 공식 길이에 맞췄으며 코너 위치·반경·일정한 12 m 폭은 학습용 근사입니다. 고저차·뱅킹·캠버·연석·실제 제동 지점은 재현하지 않습니다.':'직접 설계한 가상 평면 연습장입니다. 실제 도로·실측 코스를 나타내지 않습니다.'};
    return {preset:id,id,name:d.name,type:d.type,description:d.description,points,width:d.width,total,closed:d.closed,startHeading:Math.atan2(points[1].y,points[1].x),corners,source,timeLimit:d.timeLimit,freePractice:!!d.freePractice,scale,
      brakingBoards,turnInReferences,brakingBoardNote:d.type==='circuit'?BOARD_NOTE:''};
  }
  const catalog=Object.freeze(Object.keys(definitions).map(id=>{
    const d=definitions[id];return Object.freeze({id,name:d.name,type:d.type,description:d.description,officialLengthM:d.url?d.length:null,sourcedURLs:Object.freeze(d.url?[d.url,d.mapUrl]:[]),closed:d.closed,timeLimit:d.timeLimit});
  }));
  root.GripTracks=Object.freeze({catalog,get:make});
})(typeof globalThis!=='undefined'?globalThis:this);
