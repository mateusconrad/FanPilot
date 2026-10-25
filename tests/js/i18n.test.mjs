// The panel's two languages (wwwroot/js/i18n.mjs, wwwroot/i18n/*.json). Run from the repository root:
// node --test "tests/js/*.test.mjs"
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pickLanguage, fill, lookup, formatNumber, LANGUAGES } from '../../wwwroot/js/i18n.mjs';

const read = (path) => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
const dicts = Object.fromEntries(Object.keys(LANGUAGES).map((lang) => [lang, JSON.parse(read(`wwwroot/i18n/${lang}.json`))]));
const placeholders = (text) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
// A key exists as itself or as plural forms (key.one, key.other).
const has = (dict, key) => key in dict || (key + '.other') in dict;

test('os dois idiomas têm as mesmas chaves, com os mesmos parâmetros', () => {
  const [a, b] = Object.values(dicts);
  assert.deepEqual(Object.keys(a).sort(), Object.keys(b).sort());
  for (const key of Object.keys(a)) {
    assert.deepEqual(placeholders(a[key]), placeholders(b[key]), `parâmetros diferentes em ${key}`);
    for (const dict of Object.values(dicts)) assert.ok(dict[key].trim(), `${key} vazio`);
  }
});

test('o idioma segue o escolhido no aparelho, senão o navegador, senão inglês', () => {
  assert.equal(pickLanguage('en', ['pt-BR']), 'en');
  assert.equal(pickLanguage(null, ['pt-BR', 'en']), 'pt-BR');
  assert.equal(pickLanguage(null, ['pt-PT']), 'pt-BR');
  assert.equal(pickLanguage(null, ['es-ES', 'pt']), 'pt-BR');
  assert.equal(pickLanguage(null, ['en-GB', 'pt-BR']), 'en');
  assert.equal(pickLanguage(null, ['de-DE']), 'en');
  assert.equal(pickLanguage('xx', []), 'en');
});

test('parâmetros, números e plurais', () => {
  assert.equal(fill('Fixo {n}%', { n: 40 }, 'pt-BR'), 'Fixo 40%');
  assert.equal(fill('{t} °C', { t: 55.5 }, 'pt-BR'), '55,5 °C');
  assert.equal(fill('{t} °C', { t: 55.5 }, 'en'), '55.5 °C');
  assert.equal(fill('{name} profile', {}, 'en'), '{name} profile');
  assert.equal(formatNumber(1340, 0, 'pt-BR'), '1340');
  assert.equal(formatNumber(-128, 0, 'en'), '-128');
  assert.equal(formatNumber(4.567, 2, 'pt-BR'), '4,57');
  const pt = dicts['pt-BR'];
  assert.equal(fill(lookup(pt, 'alerts.active', { count: 1 }, 'pt-BR'), { count: 1 }, 'pt-BR'), '1 ativo');
  assert.equal(fill(lookup(pt, 'alerts.active', { count: 3 }, 'pt-BR'), { count: 3 }, 'pt-BR'), '3 ativos');
});

// Every key the pages use: t('…') and tr('…') calls, strings shaped like a key of a known namespace (maps of keys),
// and the data-i18n attributes of the HTML.
function usedKeys() {
  const namespaces = new Set(Object.keys(dicts.en).map((k) => k.split('.')[0]));
  const keys = new Set();
  for (const file of ['wwwroot/app.js', 'wwwroot/pair.js', 'wwwroot/locked.js']) {
    const src = read(file);
    for (const m of src.matchAll(/\b(?:t|tr)\(\s*'([\w.]+)'/g)) keys.add(m[1]);
    for (const m of src.matchAll(/'([a-z]\w*(?:\.\w+)+)'/g)) if (namespaces.has(m[1].split('.')[0])) keys.add(m[1]);
  }
  for (const file of ['wwwroot/index.html', 'wwwroot/pair.html', 'wwwroot/locked.html']) {
    const html = read(file);
    for (const m of html.matchAll(/data-i18n(?:-title)?="([\w.]+)"/g)) keys.add(m[1]);
    for (const m of html.matchAll(/data-i18n-attr="([^"]+)"/g)) for (const pair of m[1].split(';')) keys.add(pair.split(':')[1].trim());
  }
  return keys;
}

test('toda chave usada pelo painel existe nos dois idiomas', () => {
  const keys = usedKeys();
  assert.ok(keys.size > 150, `só ${keys.size} chaves encontradas: a busca falhou`);
  for (const [lang, dict] of Object.entries(dicts)) {
    const missing = [...keys].filter((k) => !has(dict, k));
    assert.deepEqual(missing, [], `faltam em ${lang}.json`);
  }
});

// What may stay in the HTML as it is: names, units and symbols, the same in any language.
const FIXED = new Set(['FanPilot', 'CPU', 'GPU', 'CPU (°C)', 'GPU (°C)', '°C', '%', '−', '+', '50%', '&#x25C0;&#xFE0E;', '&#x25B6;&#xFE0E;']);

test('nenhum texto visível fixo no HTML das páginas', () => {
  for (const file of ['wwwroot/index.html', 'wwwroot/pair.html', 'wwwroot/locked.html']) {
    const html = read(file).replace(/<!--[\s\S]*?-->/g, '').replace(/<(script|style)[\s\S]*?<\/\1>/g, '');
    const texts = html.split(/<[^>]*>/).map((s) => s.trim()).filter(Boolean);
    assert.deepEqual(texts.filter((s) => !FIXED.has(s)), [], `texto fixo em ${file}`);
    const attrs = [...html.matchAll(/\s(aria-label|title|placeholder|alt)="([^"]*)"/g)].map((m) => m[2]).filter((v) => v && !v.startsWith('https://'));
    assert.deepEqual(attrs, [], `atributo com texto fixo em ${file}`);
  }
});
