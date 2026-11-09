// Dependency-free event/state integration checks; browser layout is tested separately.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const cache = new Map();
const ctx = {
  console,
  localStorage: {getItem: key => cache.get(key), setItem: (key,value) => cache.set(key,value)},
  document: {getElementById: () => false, createElement: () => ({}), head: {appendChild() {}}}
};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync(path.join(__dirname,'dist/lessons.js'),'utf8'),ctx);
const G = ctx.GripLessons;
assert.equal(G.data.stages.length,6);
assert.equal(G.data.questions.length,8);
assert.equal(G.data.theory.lessons.length,6);
G.data.questions.forEach(q => assert(q.options[q.correct]));
G.data.sources.forEach(s => assert.equal(new URL(s.url).protocol,'https:'));
G.data.theory.lessons.forEach(lesson => {
  assert(G.data.stages.some(stage => stage.id === lesson.id));
  assert.equal(lesson.paragraphs.length,4);
  assert.equal(lesson.observe.length,3);
  assert(lesson.checkQuestion.question && lesson.checkQuestion.answerHint);
  assert(lesson.example.situation && lesson.example.explanation);
  assert(lesson.misconception.claim && lesson.misconception.correction);
  lesson.sourceIds.forEach(id => assert(G.data.sources.some(source => source.id === id)));
});
const handlers = {};
const el = {
  classList: {add() {}}, contains: () => true, querySelector: () => null,
  addEventListener: (name,handler) => {handlers[name] = handler;},
  removeEventListener: name => {delete handlers[name];}, innerHTML: ''
};
let launch = null;
const app = G.mount(el,{onLaunch: preset => {launch = preset;}});
const click = (action,extra={}) => handlers.click({target: {closest: () => ({dataset: {learn:action,...extra},disabled:false})}});
const toggle = (type,open) => handlers.toggle({target:{open,dataset:type==='check'?{learnCheck:G.data.stages[0].id}:{},hasAttribute:name=>type==='theory'&&name==='data-learn-theory'}});
assert(el.innerHTML.includes('하중과 접지부터'));
assert(el.innerHTML.includes('data-learn-theory open'));
assert(el.innerHTML.includes('data-learn-check="load"><summary>'));
G.data.theory.lessons[0].paragraphs.forEach(paragraph => assert(el.innerHTML.includes(paragraph)));
toggle('theory',false);click('read');assert(!el.innerHTML.includes('data-learn-theory open'));
toggle('check',true);click('read');assert(el.innerHTML.includes('data-learn-check="load" open'));
assert.equal(app.getProgress().read,1);click('read');assert.equal(app.getProgress().read,1);
click('launch',{preset:'brake'});assert.equal(launch,'brake');
click('submit');assert.equal(app.getProgress().answered,0);
click('option',{index:'1'});click('submit');assert.equal(app.getProgress().answered,1);assert.equal(app.getProgress().firstCorrect,0);
click('retry');click('option',{index:'0'});click('submit');assert.equal(app.getProgress().attempts,2);assert.equal(app.getProgress().firstCorrect,0);
click('next-question');assert(el.innerHTML.includes('브레이크를 더 밟자'));
const saved = JSON.parse(cache.get('gripLabLearningV1'));
assert.equal(saved.question,1);assert.equal(saved.answers.q1.attempts,2);
for (let i=0;i<6;i++) {
  click('stage',{index:String(i)});
  const lesson=G.data.theory.lessons[i];
  assert(el.innerHTML.includes(lesson.title));
  assert(el.innerHTML.includes('data-learn-theory open'));
  assert(el.innerHTML.includes(lesson.checkQuestion.question));
  assert(el.innerHTML.includes('흔한 오해'));
  assert(el.innerHTML.includes('실험실에서 확인하는 3단계'));
}
const app2 = G.mount(el,{});
assert.equal(app2.getProgress().answered,1);assert.equal(app2.getProgress().read,1);
click('reset');assert.equal(app2.getProgress().answered,1);
click('cancel-reset');assert.equal(app2.getProgress().answered,1);
click('reset');click('reset');assert.equal(app2.getProgress().answered,0);
ctx.localStorage.setItem = () => {throw new Error('blocked');};
click('read');assert(el.innerHTML.includes('현재 화면을 사용하는 동안만'));
console.log('PASS lessons: 6 theories, 8 quizzes, source mapping, theory/answer disclosure, quiz retry accounting, persistence, reset, and blocked storage.');
