/* The sign-in window of the iPad app (ARCHITECTURE 11). The app opens <server>/#/app-login?state=<random>&challenge=<SHA-256 of its secret> in a private sign-in window of
 * the system; the person signs in as always (hub, or password and second factor), and this page makes a pairing code from that fresh sign-in and hands it back to the
 * app at junotes://paired. The code is bound to the challenge (PKCE): only the app that knows the secret can redeem it at POST /api/v1/pair, so a code caught on its way
 * is worth nothing. No token ever passes through this page. The state comes back unchanged so the app knows the answer is to its own request. Only the fixed address
 * of the app is ever opened: nothing in the link can send the code anywhere else. */
import { t } from '/base/i18n.js';
import { h, api, emptyState, errorText } from '/base/base.js';

const KEY = 'notes.appLogin';
const STATE = /^[A-Za-z0-9_-]{16,128}$/;
const CHALLENGE = /^[A-Za-z0-9_-]{43}$/;
const CALLBACK = 'junotes://paired';
// A request of the app that was not finished within this time is forgotten (a sign-in much later in the same tab hands nothing out).
const LIFETIME = 10 * 60 * 1000;

function read() {
  try {
    const r = JSON.parse(sessionStorage.getItem(KEY) || 'null');
    return r && STATE.test(r.state) && CHALLENGE.test(r.challenge) && Date.now() - r.at < LIFETIME ? r : null;
  } catch { return null; }
}

function keep(r) {
  try { sessionStorage.setItem(KEY, JSON.stringify(r)); } catch { /* without storage the page says the app cannot be connected */ }
}

/** Before the sign-in: remembers the request of the app for this tab (the sign-in through the hub leaves the page and comes back without the address). */
export function captureAppLogin() {
  const m = /^#\/app-login\?(.*)$/.exec(location.hash);
  if (!m) return;
  const q = new URLSearchParams(m[1]);
  const state = q.get('state') || '', challenge = q.get('challenge') || '';
  if (STATE.test(state) && CHALLENGE.test(challenge)) keep({ state, challenge, at: Date.now() });
  history.replaceState(null, '', location.pathname);
}

/** After the sign-in: true when an app is waiting, and then the page does nothing else. */
export function appLoginWaiting() {
  if (read()) return true;
  try { sessionStorage.removeItem(KEY); } catch { /* nothing to forget */ }
  return false;
}

export async function finishAppLogin() {
  const request = read();
  try { sessionStorage.removeItem(KEY); } catch { /* handled below */ }
  const box = h('div', { class: 'page narrow stack l' });
  document.body.append(box);
  const show = (opts) => box.replaceChildren(emptyState(opts));
  const back = (error) => location.replace(`${CALLBACK}?${new URLSearchParams({ error, state: request ? request.state : '' })}`);
  if (!request) {
    show({ icon: 'alert', title: t('applogin.failed'), text: t('applogin.expired'), action: { label: t('applogin.back'), run: () => back('expired') } });
    return;
  }
  show({ icon: 'refresh', title: t('applogin.working') });
  try {
    const made = await api('POST', '/api/web/v1/devices/pairing/app', { challenge: request.challenge });
    show({ icon: 'check-circle', title: t('applogin.done'), text: t('applogin.doneText') });
    location.replace(`${CALLBACK}?${new URLSearchParams({ code: made.code, state: request.state })}`);
  } catch (e) {
    if (e.code === 'pairing.not_fresh') {
      // signed in from before (a browser that kept its session): sign out and in again, the app keeps waiting
      const again = async () => {
        keep({ ...request, at: Date.now() });
        await api('POST', '/api/auth/logout').catch(() => null);
        location.reload();
      };
      show({ icon: 'lock', title: t('applogin.notFresh'), text: t('applogin.notFreshText'), action: { label: t('applogin.signInAgain'), run: again } });
      return;
    }
    show({ icon: 'alert', title: t('applogin.failed'), text: errorText(e), action: { label: t('applogin.back'), run: () => back('failed') } });
  }
}
