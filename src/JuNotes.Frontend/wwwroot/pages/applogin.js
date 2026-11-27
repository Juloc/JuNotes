/* The sign-in window of the iPad app (ARCHITECTURE 11). The app opens <server>/#/app-login?state=<random> in a private sign-in window of the system; the person signs in
 * as always (hub, or password and second factor), and this page makes a pairing code from that fresh sign-in and hands it back to the app at junotes://paired. The app
 * redeems the code itself (POST /api/v1/pair), so no token ever passes through this page. The state comes back unchanged so the app knows the answer is to its own
 * request. Only the fixed address of the app is ever opened: nothing in the link can send the code anywhere else. */
import { t } from '/base/i18n.js';
import { h, api, emptyState, errorText } from '/base/base.js';

const KEY = 'notes.appLogin';
const STATE = /^[A-Za-z0-9_-]{16,128}$/;
const CALLBACK = 'junotes://paired';

/** Before the sign-in: remembers the request of the app for this tab (the sign-in through the hub leaves the page and comes back without the address). */
export function captureAppLogin() {
  const m = /^#\/app-login\?(.*)$/.exec(location.hash);
  if (!m) return;
  const state = new URLSearchParams(m[1]).get('state') || '';
  try { if (STATE.test(state)) sessionStorage.setItem(KEY, state); } catch { /* without storage the page says the app cannot be connected */ }
  history.replaceState(null, '', location.pathname);
}

/** After the sign-in: true when an app is waiting, and then the page does nothing else. */
export function appLoginWaiting() {
  try { return STATE.test(sessionStorage.getItem(KEY) || ''); } catch { return false; }
}

export async function finishAppLogin() {
  let state = '';
  try { state = sessionStorage.getItem(KEY) || ''; sessionStorage.removeItem(KEY); } catch { /* handled below */ }
  const box = h('div', { class: 'page narrow stack l' });
  document.body.append(box);
  const show = (opts) => box.replaceChildren(emptyState(opts));
  show({ icon: 'refresh', title: t('applogin.working') });
  try {
    const made = await api('POST', '/api/web/v1/devices/pairing/app');
    show({ icon: 'check-circle', title: t('applogin.done'), text: t('applogin.doneText') });
    location.replace(`${CALLBACK}?${new URLSearchParams({ code: made.code, state })}`);
  } catch (e) {
    if (e.code === 'pairing.not_fresh') {
      // signed in from before (a browser that kept its session): sign out and in again, the app keeps waiting
      const again = async () => {
        try { sessionStorage.setItem(KEY, state); } catch { /* nothing to keep */ }
        await api('POST', '/api/auth/logout').catch(() => null);
        location.reload();
      };
      show({ icon: 'lock', title: t('applogin.notFresh'), text: t('applogin.notFreshText'), action: { label: t('applogin.signInAgain'), run: again } });
      return;
    }
    const back = () => location.replace(`${CALLBACK}?${new URLSearchParams({ error: 'failed', state })}`);
    show({ icon: 'alert', title: t('applogin.failed'), text: errorText(e), action: { label: t('applogin.back'), run: back } });
  }
}
