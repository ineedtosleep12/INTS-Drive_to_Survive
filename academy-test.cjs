'use strict';
// Interaction checks for choosing a real exercise and reviewing recent progress.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const storage = new Map();
const styleIds = new Set();
const key = 'intsTrainingProgressV1';
const fixture = [
  ['intro_brake', 'foundation', '직선 제동'], ['corner_release', 'foundation', '코너 진입'],
  ['monza_t1', 'circuit', 'Monza T1'], ['monza_lesmo', 'circuit', 'Monza Lesmo'],
  ['nurb_t1', 'circuit', 'Nürburgring T1'], ['whole_monza', 'circuit', 'Monza 전체 코스'],
  ['rally_hairpin', 'rallydrift', '랠리 헤어핀'], ['drift_circle_intro', 'rallydrift', '원돌이 입문']
].map(([id, category, title]) => ({id, category, title, shortGoal:'목표 한 가지', inputHint:'S 제동', successText:'구간 마치기'}));
const context = {
  GripTraining: {missions:fixture},
  localStorage: {getItem:k => storage.get(k) ?? null, setItem() { throw new Error('Academy must not write progress'); }},
  document: {getElementById:id => styleIds.has(id), createElement:() => ({}), head:{appendChild:node => styleIds.add(node.id)}}
};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(__dirname, 'dist/academy.js'), 'utf8'), context);
const academy = context.GripAcademy;
function surface() {
  const handlers = {};
  let focused = '';
  const root = {
    innerHTML:'', classList:{add(){}}, contains:button => button.inside !== false,
    addEventListener(name, callback) { assert(!handlers[name], 'single handler after remount'); handlers[name] = callback; },
    removeEventListener(name) { delete handlers[name]; },
    querySelector:selector => ({focus:() => { focused = selector; }})
  };
  function element(action, id, extra = {}) { return {dataset:{academyAction:action, id}, ...extra}; }
  return {root, handlers, getFocused:() => focused,
    click(action, id, extra) { const button = element(action, id, extra); handlers.click({target:{closest:() => button}}); },
    press(key, id) { let prevented = false; const button = element('category', id); handlers.keydown({key, target:{closest:() => button}, preventDefault() { prevented = true; }}); return prevented; }
  };
}
const ui = surface();
const launched = [];
const calls = {free:0, guide:0};
const app = academy.mount(ui.root, {onStart:id => launched.push(['drive', id]), onObserve:id => launched.push(['observe', id]), onFree:() => calls.free++, onGuide:() => calls.guide++});
assert(ui.root.innerHTML.includes('직접 제동해 보기'));
assert(ui.root.innerHTML.includes('실패한 순간의 입력과 차의 반응'));
assert(ui.root.innerHTML.includes('Monza T1 · Lesmo · Nürburgring T1'), 'circuit path is discoverable before choosing a category');
assert.equal((ui.root.innerHTML.match(/<article /g) || []).length, 2);
ui.click('start', 'intro_brake');
ui.click('observe', 'intro_brake');
assert.deepEqual(launched, [['drive','intro_brake'], ['observe','intro_brake']]);
assert.equal(storage.size, 0, 'opening or observing exercises does not record a pass');
ui.click('category', 'circuit');
assert.equal((ui.root.innerHTML.match(/<article /g) || []).length, 4);
for (const id of ['monza_t1','monza_lesmo','nurb_t1','whole_monza']) assert(ui.root.innerHTML.includes('data-id="' + id + '"'));
assert.equal(ui.getFocused(), '#academy-tab-circuit');
ui.click('start', 'monza_lesmo');
assert.deepEqual(launched.at(-1), ['drive', 'monza_lesmo']);
ui.click('start', 'not-a-mission');
ui.click('start', 'nurb_t1', {disabled:true});
ui.click('start', 'nurb_t1', {inside:false});
assert.equal(launched.length, 3, 'invalid, disabled, and outside controls do not launch');
ui.click('free'); ui.click('guide');
assert.deepEqual(calls, {free:1, guide:1});
assert(ui.press('ArrowRight', 'circuit'));
assert.equal(ui.getFocused(), '#academy-tab-rallydrift');
assert.equal((ui.root.innerHTML.match(/<article /g) || []).length, 2);
assert(ui.press('ArrowRight', 'rallydrift'));
assert.equal(ui.getFocused(), '#academy-tab-foundation');
assert(ui.press('End', 'foundation'));
assert.equal(ui.getFocused(), '#academy-tab-rallydrift');
assert(ui.press('Home', 'rallydrift'));
assert.equal(ui.getFocused(), '#academy-tab-foundation');
assert.equal(ui.press('Escape', 'foundation'), false);

storage.set(key, JSON.stringify({intro_brake:{passed:true,mode:'observe'},corner_release:{passed:false,mode:'drive'},monza_t1:{passed:true,mode:'drive'},unknown:{passed:true,mode:'drive'}}));
app.refresh();
assert(ui.root.innerHTML.includes('최근 목표 달성 <strong>1 / 8</strong>'));
assert(ui.root.innerHTML.includes('시범 확인 · 직접 도전'));
assert(ui.root.innerHTML.includes('다시 연습해 보기'));
assert(ui.root.innerHTML.includes('직접 제동해 보기'));
storage.set(key, JSON.stringify({intro_brake:{passed:true,mode:'drive'},corner_release:{passed:'true',mode:'drive'},monza_t1:{passed:true}}));
academy.refresh();
assert(ui.root.innerHTML.includes('최근 목표 달성 <strong>1 / 8</strong>'), 'requires a boolean pass from a direct run');
assert(ui.root.innerHTML.includes('첫 체험 다시 하기'));
storage.set(key, 'broken json');
app.refresh();
assert(ui.root.innerHTML.includes('최근 목표 달성 <strong>0 / 8</strong>'));
context.localStorage.getItem = () => { throw new Error('storage blocked'); };
app.refresh();
assert(ui.root.innerHTML.includes('직접 제동해 보기'));

fixture[0].title = '<img src=x onerror=alert(1)>';
app.refresh();
assert(ui.root.innerHTML.includes('&lt;img src=x onerror=alert(1)&gt;'));
assert(!ui.root.innerHTML.includes('<img src=x'));
const remounted = academy.mount(ui.root);
assert.equal(styleIds.size, 1);
assert.equal(Object.keys(ui.handlers).length, 2);
remounted.dispose();
assert.equal(Object.keys(ui.handlers).length, 0);
const previousMarkup = ui.root.innerHTML;
academy.refresh();
assert.equal(ui.root.innerHTML, previousMarkup);
vm.runInContext(fs.readFileSync(path.join(__dirname, 'dist/training.js'), 'utf8'), context);
const real = surface();
const actualLaunches = [];
const realApp = academy.mount(real.root, {onStart:id => actualLaunches.push(id)});
assert.equal(context.GripTraining.missions.length, 8);
for (const category of ['foundation','circuit','rallydrift']) {
  real.click('category', category);
  const categoryMissions = context.GripTraining.missions.filter(mission => mission.category === category);
  assert.equal((real.root.innerHTML.match(/<article /g) || []).length, categoryMissions.length);
  for (const mission of categoryMissions) {
    assert(real.root.innerHTML.includes(mission.title));
    assert(real.root.innerHTML.includes(mission.inputHint));
    real.click('start', mission.id);
  }
}
assert.equal(new Set(actualLaunches).size, 8, 'every actual exercise is reachable from a category');
realApp.dispose();
console.log('PASS academy: all 8 real exercises reachable, observation callbacks, visible circuit entry, keyboard categories, direct-run progress, refresh, malformed/blocked storage, escaping, remount and disposal.');
