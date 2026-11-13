/* Exercise-first entry point. Training criteria describe this educational model only. */
(function () {
  'use strict';
  const storageKey = 'intsTrainingProgressV1';
  const revisedCircuitIds = ['monza_t1','monza_lesmo','nurb_t1','whole_monza'];
  const mounted = new WeakMap();
  const instances = new Set();
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const lanes = [
    {id:'foundation',number:'01',title:'기초 조작',detail:'브레이크 → 코너 진입',description:'처음이라면 여기부터. 멈추기와 브레이크를 놓는 타이밍을 익힙니다.'},
    {id:'circuit',number:'02',title:'서킷 훈련',detail:'Monza T1 · Lesmo · Nürburgring T1',description:'코너 하나부터 전체 코스까지. 실제 코너를 참고한 평면 구간에서 감속 → 회전 → 가속을 연결합니다.'},
    {id:'rallydrift',number:'03',title:'랠리 · 드리프트',detail:'헤어핀 · 원돌이 입문',description:'낮은 접지에서 방향을 바꾸고, 차가 향하는 쪽과 움직이는 쪽을 구분합니다.'}
  ];
  const styles = `
  .academy-root{padding-bottom:5px}.academy-root button{font:inherit}.academy-hero{display:grid;grid-template-columns:minmax(0,1.45fr) minmax(260px,.8fr);overflow:hidden;border-color:#3c4e36;background:linear-gradient(110deg,#17251e,#111b21 72%)}
  .academy-hero-copy{padding:30px 34px}.academy-hero .eyebrow{font-family:inherit;font-size:11px;letter-spacing:.6px;margin-bottom:11px}.academy-hero h1{font-size:36px;line-height:1.27;letter-spacing:-1.4px;font-weight:680}.academy-hero-copy>p:not(.eyebrow){color:#b6c5c8;max-width:510px;margin-top:13px;font-size:13px;line-height:1.85}.academy-actions{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-top:20px}.academy-root .academy-actions .btn{font-size:13px;padding:11px 17px}.academy-key-hint{display:block;margin-top:12px;font-size:11px;color:var(--muted)}.academy-key-hint kbd{font-size:11px}
  .academy-hero-visual{display:flex;flex-direction:column;justify-content:center;align-items:center;position:relative;min-width:0;padding:20px 22px;background:radial-gradient(ellipse at center,#b5f36b09,transparent 68%);border-left:1px solid #304137}.academy-hero-visual svg{display:block;width:100%;max-width:330px;height:220px}.academy-hero-visual figcaption{font-size:10px;color:#9eb19f;margin-top:1px}.academy-hero-visual{margin:0}.academy-visual-label{font-family:inherit;font-size:13px;fill:#dce9de}.academy-visual-small{font-family:inherit;font-size:11px;fill:#9cadb9}
  .academy-path{display:flex;list-style:none;gap:0;padding:0;margin:17px 0 27px;border:1px solid var(--line);border-radius:8px;background:#0d171e}.academy-path li{flex:1;display:flex;gap:11px;align-items:center;padding:12px 20px;position:relative}.academy-path li+li{border-left:1px solid var(--line)}.academy-path b{font:11px var(--mono);color:var(--lime)}.academy-path strong{font-size:12px;font-weight:550;display:block}.academy-path small{display:block;color:var(--muted);font-size:10px;line-height:1.6;margin-top:2px}
  .academy-section-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;margin-bottom:15px}.academy-section-head h2{font-size:23px;letter-spacing:-.8px}.academy-section-head p{margin-top:5px;color:var(--muted);font-size:12px}.academy-progress{font-size:11px;white-space:nowrap;color:var(--muted);padding:5px 0}.academy-progress strong{color:var(--lime);font:15px var(--mono)}
  .academy-lanes{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.academy-root .academy-lane{text-align:left;background:#111b23;border:1px solid var(--line);border-radius:8px;padding:16px 18px;min-width:0;position:relative;color:var(--text);transition:background .15s,border-color .15s}.academy-lane:hover{background:#1a2830}.academy-lane[aria-selected=true]{border-color:#89b753;background:#1c2b20}.academy-lane-title{display:flex;align-items:center;gap:10px;font-size:15px;font-weight:650}.academy-lane-number{font:10px var(--mono);color:var(--muted)}.academy-lane[aria-selected=true] .academy-lane-number{color:var(--lime)}.academy-lane-detail{display:block;font-size:10px;color:var(--muted);margin-top:6px;line-height:1.65}.academy-lane[aria-selected=true] .academy-lane-detail{color:#b8cbb0}.academy-lane-count{margin-left:auto;color:var(--muted);font-size:10px;font-weight:400;white-space:nowrap}
  .academy-panel{padding-top:17px}.academy-panel-intro{font-size:12px;color:var(--muted);margin:0 0 14px}.academy-missions{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}.academy-mission{padding:20px 21px;display:flex;flex-direction:column;min-width:0}.academy-mission-meta{display:flex;justify-content:space-between;gap:10px;align-items:center;min-height:20px;margin-bottom:9px}.academy-duration{font-size:10px;color:var(--muted)}.academy-badge{font-size:9px;border:1px solid #35464d;border-radius:3px;padding:1px 6px;color:#b2c5ce}.academy-badge.passed{background:#1a2d1b;border-color:#516f3a;color:var(--lime)}.academy-badge.retry{color:#ffb5a9;border-color:#634b45}.academy-mission h3{font-size:18px;line-height:1.5;letter-spacing:-.45px}.academy-mission-goal{margin-top:6px;font-size:13px;color:#c7d5dc;line-height:1.7}.academy-mission dl{display:grid;grid-template-columns:43px minmax(0,1fr);gap:9px 12px;font-size:11px;margin:16px 0 17px;line-height:1.8}.academy-mission dt{color:var(--muted);font-size:10px;padding-top:1px}.academy-mission dd{margin:0;color:#b9cad4}.academy-mission dd:last-child{color:#c2d7b3}.academy-card-actions{display:flex;align-items:center;gap:15px;margin-top:auto;padding-top:13px;border-top:1px solid var(--line)}.academy-root .academy-card-actions .btn{font-size:12px;padding:8px 14px}.academy-card-actions .text-btn{font-size:11px;text-decoration:none}.academy-card-actions .text-btn:hover{color:var(--text);text-decoration:underline}
  .academy-model-note{font-size:10px;color:#8fa4b1;line-height:1.8;margin:13px 0 0}.academy-more{display:flex;justify-content:space-between;align-items:center;gap:20px;padding:18px 21px;margin-top:21px}.academy-more strong{font-size:13px;font-weight:550}.academy-more p{color:var(--muted);font-size:11px;margin-top:4px}.academy-more-actions{display:flex;gap:8px;flex-wrap:wrap}.academy-root .academy-more .btn{font-size:11px}.academy-empty{padding:28px;color:var(--muted);font-size:13px}
  @media(max-width:1000px){.academy-hero{grid-template-columns:minmax(0,1.3fr) minmax(230px,.7fr)}.academy-hero-copy{padding:27px}.academy-hero h1{font-size:31px}.academy-hero-visual{padding:15px}.academy-path li{padding:12px 15px}.academy-lane-title{font-size:13px}.academy-root .academy-lane{padding:14px}.academy-lane-detail{font-size:9px}.academy-more{align-items:flex-start}}
  @media(max-width:700px){.academy-hero{grid-template-columns:1fr}.academy-hero-copy{padding:25px}.academy-hero h1{font-size:32px}.academy-hero-visual{display:none}.academy-path{margin-bottom:24px}.academy-path li{padding:11px 12px;gap:7px;align-items:flex-start}.academy-path b{font-size:9px;line-height:1.9}.academy-path strong{font-size:11px}.academy-path small{font-size:9px}.academy-section-head h2{font-size:20px}.academy-section-head{gap:10px;align-items:flex-start}.academy-progress{font-size:9px}.academy-progress strong{font-size:12px}.academy-lanes{gap:7px}.academy-root .academy-lane{padding:12px 10px}.academy-lane-title{display:block;font-size:12px;line-height:1.7}.academy-lane-number{display:block;margin-bottom:4px;font-size:9px}.academy-lane-count{display:none}.academy-lane-detail{line-height:1.65;font-size:9px}.academy-mission{padding:18px}.academy-mission h3{font-size:16px}.academy-more{display:block}.academy-more-actions{margin-top:13px}.academy-mission dl{grid-template-columns:35px minmax(0,1fr);gap:8px}.academy-mission-goal{font-size:12px}}
  @media(max-width:520px){.academy-hero-copy{padding:23px 20px}.academy-hero h1{font-size:29px}.academy-hero-copy>p:not(.eyebrow){font-size:12px}.academy-root .academy-actions .btn{font-size:12px;padding:10px 13px}.academy-path li{padding:10px 8px;gap:5px}.academy-path small{font-size:9px}.academy-path strong{font-size:10px}.academy-section-head{display:block}.academy-section-head h2{font-size:20px}.academy-progress{margin-top:8px;font-size:10px}.academy-missions{grid-template-columns:1fr}.academy-lane-detail{font-size:9px}.academy-root .academy-lane{padding:11px 9px}.academy-lane-title{font-size:11px}.academy-panel-intro{font-size:11px}.academy-mission h3{font-size:18px}.academy-mission dl{font-size:12px;grid-template-columns:40px minmax(0,1fr)}.academy-mission-goal{font-size:13px}.academy-more{padding:17px}.academy-root .academy-card-actions .btn{padding:10px 15px}}
  @media(prefers-reduced-motion:reduce){.academy-root .academy-lane{transition:none}}
  `;

  function ensureStyles() {
    if (document.getElementById('academy-styles')) return;
    const style = document.createElement('style');
    style.id = 'academy-styles';
    style.textContent = styles;
    document.head.appendChild(style);
  }
  function missions() {
    return Array.isArray(globalThis.GripTraining?.missions) ? globalThis.GripTraining.missions : [];
  }
  function readProgress() {
    try {
      const value = JSON.parse(localStorage.getItem(storageKey) || '{}');
      if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
      // Geometry and objectives changed in v0.5. Retain stored history while
      // counting only results earned under the current circuit conditions.
      for (const id of revisedCircuitIds) if (value[id]?.sceneVersion !== 'v05') delete value[id];
      return value;
    } catch (_) { return {}; }
  }
  function statusFor(record) {
    if (!record || typeof record !== 'object' || Array.isArray(record)) return '';
    if (record.mode === 'observe') return 'observed';
    if (record.mode !== 'drive') return '';
    return record.passed === true ? 'passed' : record.passed === false ? 'retry' : '';
  }
  function timeLabel(mission) {
    return mission.id === 'intro_brake' ? '약 1분' : mission.id === 'whole_monza' ? '약 5–10분' : '약 2분';
  }
  function badge(record) {
    const status = statusFor(record);
    if (status === 'passed') return '<span class="academy-badge passed">✓ 최근 목표 달성</span>';
    if (status === 'retry') return '<span class="academy-badge retry">다시 연습해 보기</span>';
    if (status === 'observed') return '<span class="academy-badge">시범 확인 · 직접 도전</span>';
    return '<span class="academy-badge">직접 해보기</span>';
  }
  function missionCard(mission, progress) {
    return `<article class="card academy-mission" aria-labelledby="academy-mission-${esc(mission.id)}">
      <div class="academy-mission-meta"><span class="academy-duration">${timeLabel(mission)} · 설명과 재시도 포함</span>${badge(progress[mission.id])}</div>
      <h3 id="academy-mission-${esc(mission.id)}">${esc(mission.title)}</h3>
      <p class="academy-mission-goal">${esc(mission.shortGoal)}</p>
      <dl><dt>조작</dt><dd>${esc(mission.inputHint)}</dd><dt>달성 조건</dt><dd>${esc(mission.finishCriterion || mission.successText)}</dd></dl>
      <div class="academy-card-actions"><button class="btn" data-academy-action="start" data-id="${esc(mission.id)}" aria-label="${esc(mission.title)} 훈련 시작">훈련 시작 <span aria-hidden="true">→</span></button><button class="text-btn" data-academy-action="observe" data-id="${esc(mission.id)}" aria-label="${esc(mission.title)} 자동 시범 보기">시범 먼저 보기</button></div>
    </article>`;
  }
  const illustration = `<figure class="academy-hero-visual" aria-label="제동 중 앞바퀴 하중이 늘고 뒤바퀴 하중이 줄어드는 개념 그림">
    <svg viewBox="0 0 330 220" role="img" aria-labelledby="academy-diagram-title"><title id="academy-diagram-title">제동하면 앞바퀴가 받는 하중은 늘고, 뒤바퀴가 받는 하중은 줄어듭니다.</title>
      <path d="M112 10V210M218 10V210" stroke="#3a4c48" stroke-width="1"/><path d="M165 0V25M165 195V220" stroke="#60746a" stroke-width="2" stroke-dasharray="7 7"/>
      <path d="M158 27l7-9 7 9M165 19v22" fill="none" stroke="#a1b09c" stroke-width="1.5"/>
      <rect x="127" y="68" width="12" height="31" rx="3" fill="#b5f36b"/><rect x="191" y="68" width="12" height="31" rx="3" fill="#b5f36b"/>
      <rect x="127" y="146" width="12" height="25" rx="3" fill="#586451"/><rect x="191" y="146" width="12" height="25" rx="3" fill="#586451"/>
      <path d="M145 51h40q9 0 9 13v104q0 15-10 15h-38q-10 0-10-15V64q0-13 9-13" fill="#1b2c27" stroke="#94aa8a" stroke-width="1.5"/>
      <path d="M144 82l4-13h34l4 13v22h-42z" fill="#31483b" stroke="#71876a"/><path d="M144 136h42l-3 24h-36z" fill="#29382f" stroke="#617558"/>
      <path d="M144 58h42" stroke="#b5f36b" stroke-width="3"/><path d="M144 176h8m26 0h8" stroke="#e8a392" stroke-width="3"/>
      <path d="M115 84H61" stroke="#8bbd58"/><circle cx="115" cy="84" r="2.5" fill="#b5f36b"/><text x="55" y="77" text-anchor="end" class="academy-visual-label">앞 ↑</text><text x="55" y="94" text-anchor="end" class="academy-visual-small">더 받음</text>
      <path d="M214 158h48" stroke="#6e8364"/><circle cx="214" cy="158" r="2.5" fill="#7e936e"/><text x="269" y="151" class="academy-visual-label">뒤 ↓</text><text x="269" y="168" class="academy-visual-small">덜 받음</text>
    </svg><figcaption>제동 중 바퀴가 받는 하중 · 개념 그림</figcaption>
  </figure>`;

  function mount(root, callbacks = {}) {
    if (!root) throw new Error('GripAcademy.mount requires a root element.');
    mounted.get(root)?.dispose();
    ensureStyles();
    root.classList.add('academy-root');
    let selected = 'foundation';
    function render(focusCategory) {
      const all = missions();
      const progress = readProgress();
      const passed = all.filter(mission => statusFor(progress[mission.id]) === 'passed').length;
      const category = lanes.find(lane => lane.id === selected) || lanes[0];
      const intro = all.find(mission => mission.id === 'intro_brake');
      const completedIntro = statusFor(progress.intro_brake) === 'passed';
      root.innerHTML = `<section class="card academy-hero" aria-labelledby="academy-title">
        <div class="academy-hero-copy"><p class="eyebrow">처음이라면 · 1분 체험</p><h1 id="academy-title">제동하면 차가<br>어떻게 달라질까?</h1><p>설정은 준비되어 있어요. 브레이크를 눌러 멈추고,<br>앞·뒤 바퀴가 받는 힘이 어떻게 변하는지 확인하세요.</p>
        <div class="academy-actions"><button class="btn primary" data-academy-action="start" data-id="intro_brake"${intro ? '' : ' disabled'}>${completedIntro ? '첫 체험 다시 하기' : '직접 제동해 보기'} <span aria-hidden="true">→</span></button><button class="btn subtle" data-academy-action="observe" data-id="intro_brake"${intro ? '' : ' disabled'}>자동 시범 보기</button></div><span class="academy-key-hint"><kbd>S</kbd> 또는 <kbd>↓</kbd> 브레이크 · 시작 후 바로 조작</span></div>${illustration}
      </section>
      <ol class="academy-path" aria-label="훈련 순서"><li><b>01</b><div><strong>훈련 선택</strong><small>차량과 구간은 준비 완료</small></div></li><li><b>02</b><div><strong>목표 한 가지만</strong><small>화면의 안내대로 조작</small></div></li><li><b>03</b><div><strong>이유를 보고 재도전</strong><small>실패한 순간 → 바꿀 한 가지</small></div></li></ol>
      <section aria-labelledby="academy-picker-title"><div class="academy-section-head"><div><h2 id="academy-picker-title">하고 싶은 훈련을 고르세요.</h2><p>실패한 순간의 입력과 차의 반응을 보고, 다음 시도에서 바꿀 한 가지를 찾습니다.</p></div><div class="academy-progress" aria-live="polite">최근 목표 달성 <strong>${passed} / ${all.length}</strong></div></div>
      <div class="academy-lanes" role="tablist" aria-label="훈련 종류">${lanes.map(lane => `<button class="academy-lane" id="academy-tab-${lane.id}" type="button" role="tab" aria-selected="${lane.id === selected}" aria-controls="academy-mission-panel" tabindex="${lane.id === selected ? '0' : '-1'}" data-academy-action="category" data-id="${lane.id}"><span class="academy-lane-title"><span class="academy-lane-number">${lane.number}</span>${lane.title}<span class="academy-lane-count">${all.filter(mission => mission.category === lane.id).length}개</span></span><span class="academy-lane-detail">${lane.detail}</span></button>`).join('')}</div>
      <div class="academy-panel" id="academy-mission-panel" role="tabpanel" aria-labelledby="academy-tab-${selected}" tabindex="0"><p class="academy-panel-intro">${category.description}</p><div class="academy-missions">${all.filter(mission => mission.category === selected).map(mission => missionCard(mission, progress)).join('') || '<p class="card academy-empty">훈련을 불러오지 못했습니다. 페이지를 새로고침해 주세요.</p>'}</div></div>
      <p class="academy-model-note">목표 기준은 이 모델에서만 적용됩니다. 차량·서킷은 교육용 근사이며, 시범 관찰은 직접 주행한 목표 달성으로 기록하지 않습니다.</p></section>
      <aside class="card academy-more" aria-label="자유 실험과 설명"><div><strong>다른 차량과 조건도 시험해 보고 싶다면</strong><p>자유 실험에서 직접 설정하거나, 가이드에서 동작의 이유를 확인하세요.</p></div><div class="academy-more-actions"><button class="btn" data-academy-action="free">자유 실험 열기 <span aria-hidden="true">↗</span></button><button class="btn subtle" data-academy-action="guide">원리 가이드 보기</button></div></aside>`;
      if (focusCategory) root.querySelector('#academy-tab-' + focusCategory)?.focus();
    }
    function selectCategory(id) {
      if (!lanes.some(lane => lane.id === id)) return;
      selected = id;
      render(id);
    }
    function click(event) {
      const button = event.target.closest('[data-academy-action]');
      if (!button || !root.contains(button) || button.disabled) return;
      const {academyAction: action, id} = button.dataset;
      if (action === 'category') selectCategory(id);
      else if (action === 'free') callbacks.onFree?.();
      else if (action === 'guide') callbacks.onGuide?.();
      else if (missions().some(mission => mission.id === id)) {
        if (action === 'start') callbacks.onStart?.(id);
        else if (action === 'observe') callbacks.onObserve?.(id);
      }
    }
    function keydown(event) {
      const button = event.target.closest('[data-academy-action="category"]');
      if (!button || !root.contains(button) || !['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
      event.preventDefault();
      const current = lanes.findIndex(lane => lane.id === button.dataset.id);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? lanes.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + lanes.length) % lanes.length;
      selectCategory(lanes[next].id);
    }
    const instance = {
      refresh: () => render(),
      dispose() {
        root.removeEventListener('click', click);
        root.removeEventListener('keydown', keydown);
        instances.delete(instance);
        mounted.delete(root);
      }
    };
    root.addEventListener('click', click);
    root.addEventListener('keydown', keydown);
    mounted.set(root, instance);
    instances.add(instance);
    render();
    return instance;
  }
  globalThis.GripAcademy = {mount, refresh: () => instances.forEach(instance => instance.refresh())};
})();
