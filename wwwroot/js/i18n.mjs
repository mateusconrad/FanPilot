// The panel's words in Portuguese or English (item 21). The dictionaries are wwwroot/i18n/<language>.json, flat keys
// with {name} placeholders; FanPilot.exe reads the same files for the messages it writes itself. The language follows
// the browser unless one was chosen on this device; numbers and times follow the language too.
// Run from tests/js: the pure functions here don't touch the page.

export const LANGUAGES = { 'pt-BR': 'Português', en: 'English' };
const STORE = 'fanpilot.lang';

// The language chosen on this device, else the browser's first Portuguese or English one, else English.
export function pickLanguage(stored, preferred) {
  if (Object.prototype.hasOwnProperty.call(LANGUAGES, stored)) return stored;
  for (const tag of preferred || []) {
    const base = String(tag).toLowerCase().split('-')[0];
    if (base === 'pt') return 'pt-BR';
    if (base === 'en') return 'en';
  }
  return 'en';
}

// Numbers without thousands separators ("1340 RPM"), with the language's decimal mark.
const numberFormats = new Map();
export function formatNumber(v, digits, lang) {
  const key = lang + '/' + digits;
  if (!numberFormats.has(key)) {
    numberFormats.set(key, new Intl.NumberFormat(lang, { minimumFractionDigits: digits, maximumFractionDigits: digits, useGrouping: false }));
  }
  return numberFormats.get(key).format(v);
}

// Fills {name} from params. A number goes through the language's format (up to one decimal); a missing parameter
// stays as {name}, so it shows instead of vanishing.
export function fill(template, params, lang) {
  return template.replace(/\{(\w+)\}/g, (all, name) => {
    if (!params || !(name in params)) return all;
    const v = params[name];
    return typeof v === 'number' ? formatNumber(v, Number.isInteger(v) ? 0 : 1, lang) : String(v);
  });
}

// key.one / key.other by the language's plural rules when params.count is a number, else key.
export function lookup(dict, key, params, lang) {
  if (params && typeof params.count === 'number') {
    const plural = dict[key + '.' + new Intl.PluralRules(lang).select(params.count)];
    if (plural !== undefined) return plural;
  }
  return dict[key];
}

let current = 'en';
let dict = {};

export const language = () => current;

// The text for key, or the fallback (or the key itself) when the dictionary has none: a gap shows up instead of a blank.
export function t(key, params, fallback) {
  const template = lookup(dict, key, params, current);
  if (template === undefined) return fallback !== undefined ? fallback : key;
  return fill(template, params, current);
}

// A message from FanPilot ({code, params, text}): translated by its code, or its own (English) text.
export const msg = (m) => (m && typeof m === 'object' ? t(m.code, m.params, m.text) : m == null ? '' : String(m));

export const num = (v, digits = 0) => formatNumber(v, digits, current);
export const time = (ms, options) => new Date(ms).toLocaleTimeString(current, options);
export const dateTime = (ms) => new Date(ms).toLocaleString(current);

// Picks the language and loads its dictionary; the page gets lang="…" for screen readers and hyphenation.
export async function loadLanguage() {
  let stored = null;
  try { stored = localStorage.getItem(STORE); } catch (e) { /* storage blocked: follow the browser */ }
  current = pickLanguage(stored, navigator.languages || [navigator.language]);
  const r = await fetch('/i18n/' + current + '.json');
  dict = await r.json();
  document.documentElement.lang = current;
  return current;
}

// Kept on this device; the page reloads in the new language.
export function chooseLanguage(lang) {
  try { localStorage.setItem(STORE, lang); } catch (e) { /* only for this visit */ }
  location.reload();
}

// The page's fixed text: data-i18n="key" sets the text, data-i18n-attr="aria-label:key;title:key" the attributes,
// and data-i18n-title="key" the document title.
export function translatePage(root = document) {
  root.querySelectorAll('[data-i18n]').forEach((n) => { n.textContent = t(n.getAttribute('data-i18n')); });
  root.querySelectorAll('[data-i18n-attr]').forEach((n) => {
    for (const pair of n.getAttribute('data-i18n-attr').split(';')) {
      const [attr, key] = pair.split(':');
      n.setAttribute(attr.trim(), t(key.trim()));
    }
  });
  const title = document.querySelector('[data-i18n-title]');
  if (title) document.title = t(title.getAttribute('data-i18n-title'));
}

// Text with elements in it: "Open {url} on the PC" puts the url element where {url} is, in any word order.
export function translateWith(el, key, nodes) {
  const parts = t(key).split(/\{(\w+)\}/);
  el.replaceChildren(...parts.map((p, i) => (i % 2 ? nodes[p] || '{' + p + '}' : p)));
}

// The language switch: a <select> with every language under its own name.
export function wireLanguageSelect(select) {
  select.replaceChildren(...Object.entries(LANGUAGES).map(([code, name]) => {
    const o = document.createElement('option');
    o.value = code;
    o.lang = code;
    o.textContent = name;
    o.selected = code === current;
    return o;
  }));
  select.onchange = () => chooseLanguage(select.value);
}
