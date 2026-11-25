/* Pairing devices (the iPad app): the card in the settings of a person. It lists the person's own devices (each can be removed) and makes a one-time pairing code, as text
 * and as a QR code. A fresh code of the second factor is asked for first (the server checks it). The code and the QR code live on this page only and are never kept. */
import { t, fmtAgo, fmtDate } from '/base/i18n.js';
import { h, svg, api, field, submit, notice, toast, confirmDialog, errorText, ApiError } from '/base/base.js';
import { qrSvg } from '/base/qr.js';
import { API } from './util.js';

const D = `${API}/devices`;

/** The code as it is read out: two groups of five. */
const spaced = (code) => `${code.slice(0, 5)}-${code.slice(5)}`;

/** The pairing only works over https (the server refuses it otherwise); on this machine itself it is allowed for trying things out. */
const secure = () => location.protocol === 'https:' || location.hostname === 'localhost';

const mmss = (ms) => { const s = Math.max(0, Math.ceil(ms / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };

export function pairingCard() {
  const list = h('div', { class: 'list' });
  const none = h('p', { class: 'field-hint', text: t('dev.none') });
  const area = h('div', { class: 'stack s' });
  let devices = [];
  let timer = null;

  async function load() {
    const page = await api('GET', `${D}?limit=200`);
    devices = page.items;
    list.replaceChildren(...devices.map(row));
    none.hidden = devices.length > 0;
  }

  function row(device) {
    const remove = h('button', { class: 'btn ghost danger', type: 'button', text: t('dev.remove') });
    remove.addEventListener('click', async () => {
      if (!await confirmDialog({ title: t('dev.removeTitle'), text: t('dev.removeText', { name: device.name }), confirmLabel: t('dev.remove'), danger: true })) return;
      try { await api('DELETE', `${D}/${device.id}`); toast(t('dev.removed'), { kind: 'ok' }); await load(); }
      catch (e) { if (!(e instanceof ApiError)) throw e; toast(errorText(e), { kind: 'danger' }); }
    });
    return h('div', { class: 'row' },
      h('div', { class: 'row-main' },
        h('span', { class: 'row-title', text: device.name }),
        h('span', { class: 'row-sub', text: [device.lastSeen ? t('dev.lastSeen', { ago: fmtAgo(device.lastSeen) }) : t('dev.never'), device.address, t('dev.since', { date: fmtDate(device.createdAt, { dateStyle: 'medium' }) })].filter(Boolean).join(' · ') })),
      h('span', { class: 'row-end' }, remove));
  }

  function stop() { if (timer) { clearInterval(timer); timer = null; } }

  /** The code with its QR code, a countdown, and a look at the list every few seconds: when a new device is there the code is done with. */
  function showCode(made) {
    stop();
    const expires = new Date(made.expiresAt).getTime();
    const before = devices.length;
    const left = h('p', { class: 'field-hint' });
    const done = h('button', { class: 'btn primary', type: 'button', text: t('dev.done') });
    const panel = h('div', { class: 'stack s' },
      h('p', { text: t('dev.scan') }),
      h('div', { class: 'qr' }, svg(qrSvg(`${location.origin}/#pair=${made.code}`, { label: t('dev.qrLabel') }))),
      h('div', { class: 'code', text: spaced(made.code) }),
      left, h('div', { class: 'form-actions' }, done));
    area.replaceChildren(panel);
    const finish = (message) => { stop(); area.replaceChildren(); if (message) toast(message, { kind: 'ok' }); showStart(); };
    done.addEventListener('click', () => finish());
    const tick = async () => {
      if (!panel.isConnected) { stop(); return; }
      const remaining = expires - Date.now();
      if (remaining <= 0) { stop(); area.replaceChildren(notice(t('dev.expired'), 'warn')); showStart(); return; }
      left.textContent = t('dev.validFor', { time: mmss(remaining) });
    };
    tick();
    let n = 0;
    timer = setInterval(async () => {
      await tick();
      if (!timer || ++n % 4) return;
      try { await load(); if (devices.length > before) finish(t('dev.paired')); } catch { /* the next look */ }
    }, 1000);
  }

  function showStart() {
    if (!secure()) { area.append(notice(t('dev.httpsOnly'), 'warn')); return; }
    const code = field({ label: t('dev.codeLabel'), name: 'code', hint: t('dev.codeHint'), inputmode: 'numeric', autocomplete: 'one-time-code', maxlength: 7, pattern: '[0-9 ]*', spellcheck: 'false' });
    const form = submit(h('form', { class: 'form' }, code, h('div', { class: 'form-actions' }, h('button', { class: 'btn primary', type: 'submit', text: t('dev.make') }))), async (v) => {
      try {
        const made = await api('POST', `${D}/pairing`, { code: v.code });
        showCode(made);
      } catch (e) { if (e instanceof ApiError && e.code === 'totp.wrong') e.fields = { code: 'totp.wrong' }; throw e; }
    });
    area.append(h('div', { class: 'stack s' }, h('p', { class: 'field-hint', text: t('dev.intro') }), form));
  }

  showStart();
  load().catch((e) => list.replaceChildren(h('div', { class: 'row' }, notice(errorText(e), 'danger'))));
  return h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('dev.title') }), none, list, area);
}
