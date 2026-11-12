/* Evidence-based teaching debrief. This is not game telemetry or a causal estimator. */
(function (root) {
  'use strict';
  const finite = Number.isFinite;
  const value = (v, min = -Infinity, max = Infinity) => finite(v) && v >= min && v <= max ? v : null;
  const numeric = (n, digits = 0, unit = '') => finite(n) ? n.toFixed(digits) + unit : '미기록';
  const percentage = n => numeric(finite(n) ? n * 100 : null, 0, '%');
  const abs = n => finite(n) ? Math.abs(n) : null;
  const average = (samples, key) => {
    const values = samples.map(s => s[key]).filter(finite);
    return values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
  };

  // A missing channel stays missing: it must not become an apparent zero input.
  function cleanTrace(trace) {
    if (!Array.isArray(trace)) return [];
    const sorted = trace.slice(0, 40000).filter(s => s && finite(s.t) && s.t >= 0 && s.t <= 3600).map(s => ({
      t: s.t, s: value(s.s, 0), speed: value(s.speed, 0, 600),
      steer: value(s.steer, -1, 1), brake: value(s.brake, 0, 1), throttle: value(s.throttle, 0, 1),
      handbrake: value(s.handbrake, 0, 1), clutch: value(s.clutch, 0, 1),
      utilF: value(s.utilF, 0, 10000), utilR: value(s.utilR, 0, 10000), beta: value(s.beta, -180, 180),
      off: typeof s.off === 'boolean' ? s.off : null
    })).sort((a, b) => a.t - b.t);
    return sorted.filter((s, i) => !i || s.t !== sorted[i - 1].t);
  }

  // Require a continuous sequence, not one peak or two samples separated by a gap.
  function sustained(samples, predicate, duration = .25) {
    let start = -1;
    for (let i = 0; i < samples.length; i++) {
      const s = samples[i];
      if (!predicate(s)) { start = -1; continue; }
      if (start < 0 || (i && s.t - samples[i - 1].t > .25)) start = i;
      if (i - start >= 2 && s.t - samples[start].t >= duration - 1e-8) {
        let end = i;
        while (end + 1 < samples.length && samples[end + 1].t - samples[end].t <= .25 && predicate(samples[end + 1])) end++;
        return {start: samples[start], end: samples[end], samples: samples.slice(start, end + 1)};
      }
    }
    return null;
  }
  const above = (v, min) => finite(v) && v > min;
  const turning = s => above(abs(s.steer), .1) && above(s.speed, 10);
  const event = (sample, label, detail) => ({t: sample.t, s: sample.s, label, detail});
  function inputEvent(s, label) {
    return event(s, label, '속도 ' + numeric(s.speed, 1, ' km/h') + ' · 제동 ' + percentage(s.brake) + ' · 가속 ' + percentage(s.throttle) + ' · 조향 입력 ' + percentage(abs(s.steer)));
  }
  function incidentEvent(incident) {
    const s = incident.sample;
    return event(s, incident.type === 'spin' ? '큰 차체 슬립 시작' : '첫 코스 이탈',
      '속도 ' + numeric(s.speed, 1, ' km/h') + ' · 차체 슬립각 |β| ' + numeric(abs(s.beta), 1, '°') + ' · 모델 경로 ' + numeric(s.s, 1, ' m'));
  }
  function facts(sample, pre, trace) {
    return {
      incidentTime: sample?.t ?? null, incidentProgress: sample?.s ?? null,
      speed: sample?.speed ?? null, brakePct: finite(sample?.brake) ? sample.brake * 100 : null,
      throttlePct: finite(sample?.throttle) ? sample.throttle * 100 : null,
      steerPct: finite(sample?.steer) ? Math.abs(sample.steer) * 100 : null,
      beta: sample?.beta ?? null, frontDemandPct: finite(sample?.utilF) ? sample.utilF * 100 : null,
      rearDemandPct: finite(sample?.utilR) ? sample.utilR * 100 : null,
      windowSeconds: pre.length > 1 ? pre.at(-1).t - pre[0].t : 0, traceSamples: trace.length
    };
  }
  function answer(headline, certainty, explanation, nextAction, events = [], focusTime = null, metrics = {}) {
    return {headline, certainty, explanation, nextAction, events: events.sort((a, b) => a.t - b.t), focusTime, metrics};
  }
  function entryObservation(trace, record, scene, mission) {
    const range = mission.entrySpeedRange, gate = scene.gates?.entry, speed = record.entrySpeed;
    if (!Array.isArray(range) || range.length !== 2 || !range.every(finite) || range[0] < 0 || range[1] <= range[0] || !finite(gate) || gate < 0 || !finite(speed) || speed < 0) return null;
    const sample = trace.find((s, i) => i > 0 && finite(s.s) && finite(trace[i - 1].s) && trace[i - 1].s < gate && s.s >= gate);
    if (!sample || speed >= range[0] && speed <= range[1]) return null;
    const high = speed > range[1];
    return {sample, high, range, speed, event: event(sample, high ? '과제 진입 속도 초과' : '과제 진입 속도 미달',
      '진입 기록 ' + numeric(speed, 1, ' km/h') + ' · 이 모델의 과제 범위 ' + range[0] + '–' + range[1] + ' km/h')};
  }
  function localCurve(scene, progress) {
    const entry = scene.gates?.entry, exit = scene.gates?.exit, points = scene.points;
    if (!finite(entry) || !finite(exit) || exit <= entry || !finite(progress) || progress < entry || progress > exit || !Array.isArray(points)) return null;
    const lo = Math.max(entry, progress - 8), hi = Math.min(exit, progress + 8), headings = [];
    if (hi - lo < 4) return null;
    for (let i = 1; i < points.length; i++) {
      const a = points[i - 1], b = points[i];
      if (!a || !b || ![a.x,a.y,a.s,b.x,b.y,b.s].every(finite) || b.s <= a.s) return null;
      if (b.s < lo || a.s > hi) continue;
      const dx = b.x - a.x, dy = b.y - a.y;
      if (Math.hypot(dx, dy) < 1e-8) return null;
      headings.push(Math.atan2(dy, dx));
    }
    let signed = 0, total = 0;
    for (let i = 1; i < headings.length; i++) {
      const delta = Math.atan2(Math.sin(headings[i] - headings[i - 1]), Math.cos(headings[i] - headings[i - 1]));
      signed += delta; total += Math.abs(delta);
    }
    if (total < .08) return null; // A gate alone does not prove the nearby road turns.
    const direction = Math.abs(signed) >= .06 && Math.abs(signed) / total >= .65 ? Math.sign(signed) : 0;
    return {direction, turnRadians: total};
  }
  function noSteeringBefore(pre, scene) {
    const predicate = s => finite(s.steer) && Math.abs(s.steer) < .02 && finite(s.s) && s.s >= scene.gates.entry && s.s <= scene.gates.exit;
    // It must persist up to the incident, not be an earlier straight-line input.
    let start = pre.length;
    while (start > 0 && predicate(pre[start - 1]) && (start === pre.length || pre[start].t - pre[start - 1].t <= .25)) start--;
    return sustained(pre.slice(start), predicate, .4);
  }

  function analyze(options = {}) {
    const record = options?.record || {}, scene = options?.scene || {}, mission = options?.mission || {};
    const trace = cleanTrace(options?.trace), last = trace.at(-1);
    const enough = trace.length >= 5 && last.t - trace[0].t >= .25;
    if (!enough) return answer('원인을 판단할 주행 기록이 부족합니다.', 'unknown',
      '시간 순서가 있는 입력과 차량 반응을 충분히 확보하지 못했습니다. 결과 요약만으로 실패 원인을 정하지 않습니다.',
      '같은 과제를 다시 시작해 문제가 생기는 순간까지 주행한 뒤 기록을 확인하세요.', [], last?.t ?? null, {traceSamples: trace.length});

    const entry = entryObservation(trace, record, scene, mission);

    const firstOff = trace.find(s => s.off === true);
    const spin = sustained(trace, s => above(abs(s.beta), 65) && above(s.speed, 10.8), .35);
    let incident = firstOff ? {sample: firstOff, type: 'off'} : null;
    if (spin && (!incident || spin.start.t < incident.sample.t)) incident = {sample: spin.start, type: 'spin'};

    if (incident) {
      // Exclude the incident sample and everything after it when proposing a cause.
      const end = incident.sample.t, pre = trace.filter(s => s.t >= end - 2 && s.t < end);
      const metrics = facts(incident.sample, pre, trace), consequence = incidentEvent(incident);
      const candidates = [];
      function candidate(kind, predicate, headline, explanation, nextAction, inputLabel, responseLabel, axle) {
        const sequence = sustained(pre, predicate);
        if (sequence) candidates.push({kind, sequence, headline, explanation, nextAction, inputLabel, responseLabel, axle});
      }
      candidate('combined', s => turning(s) && above(s.brake, .25) && above(s.utilF, 1.05),
        '제동과 조향이 겹친 구간을 먼저 줄여보세요.',
        '이탈 전 앞축 요구가 모델 한계를 넘는 동안 제동과 조향을 함께 유지했습니다. 두 입력이 접지 여유를 나눠 쓴 것이 원인 후보입니다. 진입 속도나 주행 경로의 영향까지 이 기록만으로 확정할 수는 없습니다.',
        '다음 시도는 직선에서 감속을 더 끝내고, 회전을 시작할 때 브레이크를 조금 더 풀어보세요. 다른 조건은 그대로 두세요.',
        '제동 + 조향이 겹침', '앞축 요구가 한계를 계속 초과', 'utilF');
      candidate('handbrake', s => above(s.handbrake, .15) && above(s.utilR, 1.05) && above(abs(s.beta), 12) && above(s.speed, 10),
        '핸드브레이크와 뒤 슬립이 겹쳤습니다.',
        '문제 직전 핸드브레이크 입력, 뒤축 요구 초과, 큰 차체 슬립이 함께 이어졌습니다. 뒤축 제동이 방향 불안정에 기여했을 가능성이 있습니다. 바퀴 잠김 자체를 측정한 결과는 아닙니다.',
        '다음 시도는 핸드브레이크를 더 짧게 사용하고, 놓은 뒤 차체와 이동 방향이 가까워지는지 확인하세요.',
        '핸드브레이크 입력 유지', '뒤축 요구 초과 + 차체 슬립', 'utilR');
      candidate('traction', s => above(s.throttle, .4) && above(s.utilR, 1.05) && above(abs(s.beta), 12) && above(s.speed, 10) && finite(s.handbrake) && s.handbrake < .15,
        '뒤가 흐르기 전의 가속량을 줄여보세요.',
        '문제 직전 가속 입력과 뒤축 요구 초과, 큰 차체 슬립이 함께 이어졌습니다. 가속으로 뒤 접지 여유를 소모한 것이 원인 후보입니다. 뒤 슬립의 원인을 가속 하나로 확정하지는 않습니다.',
        '다음 시도는 같은 지점에서 가속을 더 천천히 늘리고, 차체 슬립각이 줄어드는지 비교하세요.',
        '가속 입력 유지', '뒤축 요구 초과 + 차체 슬립', 'utilR');
      candidate('front', s => turning(s) && above(s.utilF, 1.05),
        '앞축 요구가 커진 순간부터 확인하세요.',
        '이탈 전에 조향 중 앞축 요구가 모델 한계를 넘었습니다. 앞축의 회전력 여유가 부족했을 가능성이 있습니다. 이 지표만으로 진입 속도가 높았는지, 조향이 많았는지, 경로가 늦었는지 단정하지 않습니다.',
        '다음 시도는 진입 속도만 조금 낮추고, 같은 조향으로 코스 안에 남는지 비교하세요.',
        '조향 입력 유지', '앞축 요구가 한계를 계속 초과', 'utilF');
      candidate('rear', s => above(s.utilR, 1.05) && above(abs(s.beta), 12) && above(s.speed, 10),
        '뒤축 요구와 슬립이 먼저 커졌습니다.',
        '문제 직전 뒤축 요구 초과와 큰 차체 슬립이 함께 이어졌습니다. 뒤 접지 여유가 줄어든 상태는 확인되지만, 가속·제동·방향 전환 중 어떤 입력이 주원인인지는 분리하지 못했습니다.',
        '다음 시도는 진입 속도만 낮춰 같은 경로를 따라가며, 뒤가 흐르기 시작하는 시점을 비교하세요.',
        '슬립 증가 직전 입력', '뒤축 요구 초과 + 차체 슬립', 'utilR');

      // Earliest established response wins. Specific input combinations break ties.
      const priority = {handbrake: 0, traction: 1, combined: 2, rear: 3, front: 4};
      candidates.sort((a, b) => a.sequence.start.t - b.sequence.start.t || priority[a.kind] - priority[b.kind]);
      const selected = candidates[0];
      if (selected) {
        const q = selected.sequence, input = inputEvent(q.start, selected.inputLabel);
        if (selected.kind === 'handbrake') input.detail += ' · 핸드브레이크 ' + percentage(q.start.handbrake);
        const response = event(q.end, selected.responseLabel,
          numeric(q.end.t - q.start.t, 2, '초') + ' 동안 관측 · 평균 요구 / 한계 ' + percentage(average(q.samples, selected.axle)) + ' · 마지막 |β| ' + numeric(abs(q.end.beta), 1, '°'));
        if (selected.kind === 'front' && Math.abs(q.end.steer) - Math.abs(q.start.steer) >= .08) {
          selected.explanation = '앞축 요구가 한계를 넘는 동안 조향 입력 크기도 ' + percentage(abs(q.start.steer)) + '에서 ' + percentage(abs(q.end.steer)) + '로 늘었습니다. 조향을 더해도 그만큼 회전력이 늘지 않았을 가능성이 있습니다. 경로와 속도의 영향은 별도 확인이 필요합니다.';
          selected.nextAction = '다음 시도는 같은 진입 속도에서 조향을 더 얹는 대신 조금 풀어보고, 앞축 요구와 이동 경로가 어떻게 바뀌는지 비교하세요.';
        }
        // The wording of the combined case also fits spin incidents.
        let explanation = selected.explanation.replaceAll('이탈 전', '문제 전').replaceAll('이탈 전에', '문제 전에');
        const events = [input, response, consequence];
        if (entry?.high && entry.sample.t >= end - 2 && entry.sample.t < end) {
          events.push(entry.event);
          explanation = '같은 구간에서 진입 속도가 이 모델의 과제 범위를 넘은 것도 확인했습니다. ' + explanation;
        }
        return answer(selected.headline, 'likely', explanation, selected.nextAction,
          events, end, {...metrics, evidenceKind: selected.kind});
      }
      const entryBefore = entry?.high && entry.sample.t < end ? entry : null;
      const curve = incident.type === 'off' ? localCurve(scene, incident.sample.s) : null;
      const noSteering = curve ? noSteeringBefore(pre, scene) : null;
      if (noSteering && end - noSteering.end.t <= .25) {
        const q = noSteering, maxInput = Math.max(...q.samples.map(s => Math.abs(s.steer)));
        const direction = curve.direction > 0 ? '왼쪽' : curve.direction < 0 ? '오른쪽' : null;
        const events = [inputEvent(q.start, '회전 구간의 조향 입력'),
          event(q.end, '조향 입력이 거의 없는 상태 유지', numeric(q.end.t - q.start.t, 2, '초') + ' 동안 조향 입력 크기 2% 미만 · 관측 최대 ' + numeric(maxInput * 100, 1, '%')),
          consequence];
        let explanation = '코스가 휘는 회전 구간에서 조향 입력 크기 2% 미만이 지속된 뒤 첫 코스 이탈이 기록됐습니다. 입력이 거의 없었다는 관측이며, 필요한 최적 조향량이나 실패의 유일한 원인을 정한 것은 아닙니다.';
        if (entryBefore) { events.push(entryBefore.event); explanation += ' 진입 속도 ' + numeric(entryBefore.speed, 1, ' km/h') + '도 이 모델의 과제 범위 ' + entryBefore.range[0] + '–' + entryBefore.range[1] + ' km/h보다 높았습니다.'; }
        return answer('회전 구간에서 조향 입력이 거의 없었습니다.', 'observed', explanation,
          direction ? '다음 시도는 회전 구간에서 ' + direction + ' 조향을 조금씩 시작하고, 이동 경로가 도로를 따라가는지 확인하세요. 필요한 양은 경로를 보며 조절하세요.' : '다음 시도는 코스가 휘는 방향을 확인하며 조향을 조금씩 시작하고, 이동 경로가 도로를 따라가는지 확인하세요.',
          events, end, {...metrics, evidenceKind: 'noSteering', curveDirection: direction || '방향 전환'});
      }
      const prior = pre.at(-1);
      const events = prior ? [inputEvent(prior, '문제 직전 입력'), consequence] : [consequence];
      if (entryBefore) {
        events.push(entryBefore.event);
        return answer('과제 범위를 넘는 진입 속도가 기록됐습니다.', 'observed',
          '첫 문제 전에 진입 속도 ' + numeric(entryBefore.speed, 1, ' km/h') + '가 이 모델의 과제 범위 ' + entryBefore.range[0] + '–' + entryBefore.range[1] + ' km/h를 넘었습니다. 이는 측정된 조건 차이이며, 속도를 실패의 유일한 원인으로 판단한 것은 아닙니다. 조향 시점·방향과 경로도 확인해야 합니다.',
          '다음 시도는 직선에서 조금 더 일찍 제동해 과제 진입 범위에 맞춘 뒤, 같은 조향의 결과를 비교하세요.',
          events, end, {...metrics, evidenceKind: 'entryRange', entrySpeed: entryBefore.speed, targetMin: entryBefore.range[0], targetMax: entryBefore.range[1]});
      }
      return answer(incident.type === 'spin' ? '차체 슬립은 커졌지만 주원인은 분리하지 못했습니다.' : '코스 이탈 시점은 확인했지만 원인은 더 확인해야 합니다.', 'unknown',
        '첫 문제 전 2초에서 지속적인 접지 요구 초과와 입력의 조합을 확인하지 못했습니다. 조향 시점·방향, 진입 경로 또는 속도 등 여러 가능성이 남아 있습니다. 문제 뒤의 큰 수치를 원인으로 사용하지 않았습니다.',
        '표시된 순간부터 다시 보고, 다음 시도는 진입 속도만 낮춰 같은 조향을 재현해보세요.', events, end, metrics);
    }

    // Summary says an incident happened but no matching observation survived.
    if (above(record.offTime, 0) || ['spin', 'far', 'offtrack'].includes(record.reason)) return answer('요약에는 문제가 남았지만 해당 순간의 기록이 없습니다.', 'unknown',
      '저장된 요약과 주행 기록으로 같은 사건을 연결할 수 없습니다. 후반의 타이어 수치로 앞선 실패 원인을 추정하지 않습니다.',
      '같은 과제를 다시 달려 첫 이탈 또는 큰 슬립 직전의 입력을 기록하세요.', [], last.t, {traceSamples: trace.length});

    if (entry && record.reason === 'finish') return answer(entry.high ? '진입 속도가 이번 과제의 범위보다 높았습니다.' : '진입 속도가 이번 과제의 범위보다 낮았습니다.', 'observed',
      '진입 기록 ' + numeric(entry.speed, 1, ' km/h') + '를 과제 범위 ' + entry.range[0] + '–' + entry.range[1] + ' km/h와 비교했습니다. 이 범위는 교육용 모델의 실험 조건이며 실제 게임의 최적 속도나 브레이크 포인트가 아닙니다. 범위를 벗어났다는 사실만으로 시간 손실을 판단하지 않습니다.',
      entry.high ? '다음 시도는 직선에서 제동을 조금 먼저 시작해, 같은 조향으로 목표 진입 범위에 들어가는지 확인하세요.' : '다음 시도는 진입 직전의 제동을 조금 더 풀어, 같은 조향으로 목표 진입 범위에 들어가는지 확인하세요.',
      [entry.event, event(last, '구간 완료', '마지막 속도 ' + numeric(last.speed, 1, ' km/h'))], entry.sample.t,
      {traceSamples: trace.length, evidenceKind: 'entryRange', entrySpeed: entry.speed, targetMin: entry.range[0], targetMax: entry.range[1]});

    if (record.reason === 'stop') {
      const braking = sustained(trace, s => above(s.brake, .2), .25);
      if (!braking) return answer('멈춘 이유를 입력 기록에서 확인하지 못했습니다.', 'unknown',
        '정지는 기록됐지만 지속적인 제동 입력의 근거가 부족합니다. 감속 원인을 브레이크로 단정하지 않습니다.',
        '같은 시작 속도에서 브레이크를 일정하게 눌러 정지 과정부터 비교하세요.', [inputEvent(last, '정지 직전 기록')], last.t, {traceSamples: trace.length});
      return answer('제동 입력부터 정지까지 확인할 수 있습니다.', 'observed',
        '제동을 유지한 시점과 마지막 속도를 표시했습니다. 과제의 목표 감속량·정지 거리 충족 여부는 성공 조건에서 따로 확인하세요.',
        '다음 시도는 제동을 시작하는 위치만 바꿔 정지 위치가 어떻게 달라지는지 비교하세요.',
        [inputEvent(braking.start, '제동 유지 시작'), event(last, '주행 종료', '마지막 속도 ' + numeric(last.speed, 1, ' km/h') + ' · 이동 거리 ' + numeric(record.stopDistance, 1, ' m'))], braking.start.t, {traceSamples: trace.length});
    }
    if (record.reason === 'user') return answer('직접 종료한 시점까지의 입력을 확인하세요.', 'observed',
      '사용자가 주행을 종료했습니다. 기록에서 확인된 첫 이탈이나 큰 슬립 사건이 없어 실패 원인을 정하지 않았습니다. 완주 또는 과제 성공을 판정하는 결과도 아닙니다.',
      '같은 조건으로 다시 시작해 회전 구간 또는 끝 지점까지 이어간 뒤 입력과 반응을 비교하세요.',
      [inputEvent(last, '직접 종료 직전 입력')], last.t, {traceSamples: trace.length});
    if (record.reason === 'timeout') {
      const tail = trace.filter(s => s.t >= last.t - 3);
      const holding = sustained(tail, s => finite(s.speed) && s.speed < 2 && above(s.brake, .2) && finite(s.throttle) && s.throttle < .05, .8);
      return answer(holding ? '끝부분에서 제동한 채 멈춰 있었습니다.' : '제한 시간 안에 끝 지점에 도착하지 못했습니다.', 'observed',
        holding ? '마지막 구간에서 2 km/h 미만의 속도, 브레이크 입력과 낮은 가속 입력이 함께 이어졌습니다. 느린 기록만으로 코너링 원인을 평가하지 않습니다.' : '시간 종료와 마지막 경로 위치를 확인했습니다. 뚜렷한 이탈이나 큰 슬립 사건이 없어 속도가 느린 원인은 분리하지 않았습니다.',
        holding ? '다음 시도는 회전을 마친 뒤 브레이크를 놓고 가속을 조금씩 늘려 끝 지점까지 이어가세요.' : '같은 코스를 천천히 완주하는 것을 먼저 목표로 하고, 멈추는 구간의 입력 하나를 확인하세요.',
        [inputEvent(holding?.start || last, holding ? '제동한 채 정지' : '시간 종료 위치')], holding?.start.t ?? last.t, {traceSamples: trace.length});
    }
    const knownOff = trace.filter(s => typeof s.off === 'boolean').length;
    if (record.reason === 'finish' && record.offTime === 0 && knownOff === trace.length) return answer('코스 이탈 없이 구간을 마쳤습니다.', 'observed',
      '이 기록에서는 코스 이탈이나 지속적인 큰 차체 슬립을 찾지 못했습니다. 이것만으로 최적 라인·최고 페이스 또는 과제의 모든 성공 조건을 달성했다고 판단하지는 않습니다.',
      '같은 조건으로 다시 달려 진입 속도와 브레이크를 놓는 지점을 반복해보세요.',
      [inputEvent(trace[0], '시작 입력'), event(last, '구간 완료', '마지막 속도 ' + numeric(last.speed, 1, ' km/h'))], last.t, {traceSamples: trace.length});
    return answer('입력과 반응을 나란히 확인하세요.', 'unknown',
      '확인된 실패 사건이나 충분한 완료 정보가 없어 원인을 정하지 않았습니다. 기록에 없는 입력과 반응은 추정하지 않습니다.',
      '같은 구간을 끝까지 달린 뒤 첫 문제 또는 종료 순간을 비교하세요.', [inputEvent(last, '마지막 기록')], last.t, {traceSamples: trace.length});
  }

  const api = Object.freeze({analyze});
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.GripDebrief = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
