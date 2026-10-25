// The page a device sees before it is paired: how to pair it, in its language.
import { loadLanguage, translatePage, translateWith } from './js/i18n.mjs';

await loadLanguage();
translatePage();
const url = document.createElement('strong');
url.textContent = 'http://localhost' + (location.port ? ':' + location.port : '') + '/pair';
translateWith(document.getElementById('how'), 'locked.how', { url });
