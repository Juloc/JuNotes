/* The administration: overview, the AI connection (Codex), the AI profiles and the backups. For the role admin; the server checks the role at every path. */
import { t, tp, fmtDate, fmtDay, fmtNumber } from '/base/i18n.js';
import { h, icon, api, field, switchField, capsule, badge, notice, toast, busy, confirmDialog, emptyState, errorText, ApiError } from '/base/base.js';
import { API } from './util.js';

const A = `${API}/admin`;
const kb = (bytes) => `${fmtNumber(Math.round(bytes / 1024))} KB`;

function tabs(current) {
  return capsule({ label: t('nav.admin'), items: [
    { href: '#/admin', label: t('adm.overview'), icon: 'activity', current: current === 'overview' },
    { href: '#/admin/codex', label: t('adm.codex'), icon: 'bolt', current: current === 'codex' },
    { href: '#/admin/profiles', label: t('adm.profiles'), icon: 'sliders', current: current === 'profiles' },
    { href: '#/admin/backups', label: t('adm.backups'), icon: 'database', current: current === 'backups' },
  ] });
}

const row = (title, sub, end) => h('div', { class: 'row' }, h('span', { class: 'row-main' }, h('span', { class: 'row-title', text: title }), sub ? h('span', { class: 'row-sub', text: sub }) : null), end ? h('span', { class: 'row-end' }, end) : null);

async function act(button, request, done) {
  try { await busy(button, request); if (done) done(); }
  catch (e) { if (!(e instanceof ApiError)) throw e; toast(errorText(e), { kind: 'danger' }); }
}

/* ---------- overview ---------- */
export async function overviewPage() {
  const o = await api('GET', `${A}/overview`);
  const codex = !o.codex.installed ? [t('adm.codexMissing'), 'danger'] : o.codex.signedIn ? [t('adm.signedIn'), 'ok'] : [t('adm.signedOut'), 'warn'];
  const importButton = h('button', { class: 'btn outline', type: 'button' }, icon('refresh'), t('adm.fetchNow'));
  importButton.addEventListener('click', () => act(importButton, api('POST', `${A}/school/import`), () => { toast(t('adm.fetched'), { kind: 'ok' }); location.reload(); }));
  const school = o.school;
  const body = h('div', { class: 'page stack l' }, tabs('overview'),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.server') }), h('div', { class: 'list' },
      row(t('adm.database'), null, badge(t(o.databaseReady ? 'adm.ready' : 'adm.notReady'), o.databaseReady ? 'ok' : 'danger')),
      row(t('adm.lastBackup'), o.backup ? `${fmtDate(o.backup.at)} (${kb(o.backup.bytes)})` : t('adm.none'), badge(t(o.backupOverdue ? 'adm.overdue' : 'adm.current'), o.backupOverdue ? 'warn' : 'ok')))),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.school') }),
      school.timetableConfigured || school.newsConfigured
        ? h('div', { class: 'list' },
          row(t('adm.timetable'), t('adm.events', { n: school.eventCount }), badge(t(school.timetableConfigured ? 'adm.connected' : 'adm.notConnected'), school.timetableConfigured ? 'ok' : '')),
          row(t('adm.news'), tp('adm.items', school.newsCount), badge(t(school.newsConfigured ? 'adm.connected' : 'adm.notConnected'), school.newsConfigured ? 'ok' : '')),
          school.lastError ? notice(school.lastError, 'warn') : null,
          h('div', { class: 'cluster' }, importButton))
        : h('p', { class: 'muted', text: t('adm.schoolOff') })),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.ai') }), h('div', { class: 'list' },
      row(t('adm.connection'), null, badge(codex[0], codex[1])),
      row(t('adm.model'), `${o.model || t('adm.accountDefault')}, ${t('adm.effort')}: ${t(`adm.effort.${o.effort || 'default'}`)}`),
      row(t('adm.today'), t('adm.requestsOf', { n: o.today.requests, limit: o.limit }) + (o.today.failures ? `, ${t('adm.failed', { n: o.today.failures })}` : '')))));
  return { title: t('nav.admin'), sub: t('adm.sub'), body };
}

/* ---------- AI connection ---------- */
export async function codexPage() {
  const c = await api('GET', `${A}/codex`);
  const status = c.status;
  const signIn = h('div', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.codexOnServer') }));
  const reload = () => location.reload();
  if (!status.installed) {
    signIn.append(notice(t('adm.codexMissingLong'), 'danger'));
  } else if (status.signedIn) {
    const out = h('button', { class: 'btn ghost danger', type: 'button' }, icon('logout'), t('adm.signOut'));
    out.addEventListener('click', async () => {
      if (!await confirmDialog({ title: t('adm.signOut'), text: t('adm.signOutText'), confirmLabel: t('adm.signOut'), danger: true })) return;
      act(out, api('POST', `${A}/codex/logout`), () => { toast(t('adm.signedOutDone'), { kind: 'ok' }); reload(); });
    });
    signIn.append(h('div', { class: 'list' }, row(t('adm.signedIn'), status.signIn === 'chatgpt' ? t('adm.withChatgpt') : status.signIn === 'apiKey' ? t('adm.withKey') : '', badge(t('adm.ok'), 'ok')), row(t('adm.version'), status.version || '')), h('div', { class: 'cluster' }, out));
  } else if (c.waiting) {
    const cancel = h('button', { class: 'btn outline', type: 'button' }, t('common.cancel'));
    cancel.addEventListener('click', () => act(cancel, api('POST', `${A}/codex/cancel`), reload));
    signIn.append(h('p', { text: t('adm.deviceText') }), h('p', null, h('a', { href: c.waiting.url, target: '_blank', rel: 'noopener noreferrer', text: c.waiting.url })), h('p', { class: 'code', text: c.waiting.code }), h('p', { class: 'muted', text: t('adm.deviceNote') }), h('div', { class: 'cluster' }, cancel));
    // the page looks again every few seconds and shows "signed in" as soon as it worked
    const timer = setInterval(async () => {
      if (!location.hash.startsWith('#/admin/codex')) { clearInterval(timer); return; }
      try { if ((await api('GET', `${A}/codex`)).status.signedIn) { clearInterval(timer); reload(); } } catch { /* try again */ }
    }, 5000);
  } else {
    const login = h('button', { class: 'btn primary', type: 'button' }, t('adm.signIn'));
    login.addEventListener('click', () => act(login, api('POST', `${A}/codex/login`), reload));
    signIn.append(notice(t('adm.signedOutLong'), 'warn'), h('div', { class: 'cluster' }, login));
  }

  const model = field({ label: t('adm.model'), name: 'model', value: c.settings.model || '', placeholder: t('adm.modelPlaceholder'), autocomplete: 'off', maxlength: 80 });
  const effort = field({ label: t('adm.effortLabel'), name: 'effort', type: 'select', value: c.settings.effort || '', options: [['', t('adm.effort.default')], ...c.efforts.map((e) => [e, t(`adm.effort.${e}`)])] });
  const limit = field({ label: t('adm.limitLabel'), name: 'dailyLimit', type: 'number', value: c.settings.dailyLimit ?? '', min: 1, max: c.maxDailyLimit, placeholder: t('adm.limitPlaceholder', { n: c.limit }) });
  const save = h('button', { class: 'btn primary', type: 'submit' }, t('common.save'));
  const form = h('form', { class: 'card stack' }, h('h2', { class: 'card-title', text: t('adm.modelAndEffort') }), h('p', { class: 'muted', text: t('adm.modelNote') }), model, effort, limit, h('p', { class: 'muted', text: t('adm.effortNote') }), h('div', { class: 'cluster' }, save));
  form.addEventListener('submit', (e) => {
    e.preventDefault();
    const body = { model: model.querySelector('input').value.trim() || null, effort: effort.querySelector('select').value || null, dailyLimit: limit.querySelector('input').value ? Number(limit.querySelector('input').value) : null };
    act(save, api('PUT', `${A}/codex/settings`, body), () => toast(t('adm.savedNext'), { kind: 'ok' }));
  });

  const usage = h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.usage') }), h('div', { class: 'list' },
    row(t('adm.today'), t('adm.requestsOf', { n: c.today.requests, limit: c.limit }) + (c.today.failures ? `, ${t('adm.failed', { n: c.today.failures })}` : '')),
    c.recent.filter((r) => r.day !== c.today.day).map((r) => row(fmtDay(r.day), t('adm.requests', { n: r.requests }) + (r.failures ? `, ${t('adm.failed', { n: r.failures })}` : '')))),
    h('p', { class: 'muted', text: t('adm.usageNote') }));
  return { title: t('adm.codex'), sub: t('adm.codexSub'), body: h('div', { class: 'page stack l' }, tabs('codex'), signIn, form, usage) };
}

/* ---------- AI profiles ---------- */
export async function profilesPage() {
  const data = await api('GET', `${A}/profiles`);
  const cards = data.profiles.map((p) => {
    const prompt = field({ label: t('adm.instruction'), name: 'systemPrompt', type: 'textarea', value: p.systemPrompt, rows: 6, maxlength: 4000 });
    const difficulty = field({ label: t('adm.difficulty'), name: 'difficulty', type: 'select', value: p.difficulty, options: data.difficulties.map((d) => [d, d]) });
    const tone = field({ label: t('adm.tone'), name: 'tone', type: 'select', value: p.tone, options: data.tones.map((d) => [d, d]) });
    const model = field({ label: t('adm.modelForTask'), name: 'model', value: p.model || '', placeholder: t('adm.modelForTaskPlaceholder'), autocomplete: 'off', maxlength: 80 });
    const effort = field({ label: t('adm.effortForTask'), name: 'effort', type: 'select', value: p.effort || '', options: [['', t('adm.effortLikeGeneral')], ...data.efforts.map((e) => [e, t(`adm.effort.${e}`)])] });
    const enabled = switchField({ label: t('adm.enabled'), name: 'enabled', checked: p.enabled });
    const save = h('button', { class: 'btn primary', type: 'submit' }, t('common.save'));
    const reset = h('button', { class: 'btn ghost danger', type: 'button' }, t('adm.resetDefault'));
    const form = h('form', { class: 'card stack' }, h('h2', { class: 'card-title', text: p.name }), prompt, difficulty, tone, model, effort, enabled, h('div', { class: 'cluster' }, save, reset));
    form.addEventListener('submit', (e) => {
      e.preventDefault();
      const body = { systemPrompt: prompt.querySelector('textarea').value, difficulty: difficulty.querySelector('select').value, tone: tone.querySelector('select').value, model: model.querySelector('input').value.trim() || null, effort: effort.querySelector('select').value || null, enabled: enabled.querySelector('input').checked };
      act(save, api('PUT', `${A}/profiles/${encodeURIComponent(p.key)}`, body), () => toast(t('adm.profileSaved', { name: p.name }), { kind: 'ok' }));
    });
    reset.addEventListener('click', async () => {
      if (!await confirmDialog({ title: t('adm.resetDefault'), text: t('adm.resetText', { name: p.name }), confirmLabel: t('adm.resetDefault'), danger: true })) return;
      act(reset, api('POST', `${A}/profiles/${encodeURIComponent(p.key)}/reset`), () => { toast(t('adm.resetDone'), { kind: 'ok' }); location.reload(); });
    });
    return form;
  });
  return { title: t('adm.profiles'), sub: t('adm.profilesSub'), body: h('div', { class: 'page stack l' }, tabs('profiles'), notice(t('adm.profilesNote')), cards) };
}

/* ---------- backups ---------- */
export async function backupsPage() {
  const b = await api('GET', `${A}/backups`);
  const run = h('button', { class: 'btn primary', type: 'button' }, icon('database'), t('adm.backupNow'));
  run.addEventListener('click', () => act(run, api('POST', `${A}/backups/run`), () => { toast(t('adm.backupDone'), { kind: 'ok' }); location.reload(); }));
  const keyNote = b.keyState === 'with-copies' ? h('p', { class: 'muted', text: t('adm.keyWithCopies') })
    : b.keyState === 'nowhere' ? notice(t('adm.keyNowhere'), 'danger') : h('p', { class: 'muted', text: t('adm.keyNone') });
  const kinds = { Manual: t('adm.kind.manual'), BeforeMigration: t('adm.kind.before'), Automatic: t('adm.kind.night') };
  const body = h('div', { class: 'page stack l' }, tabs('backups'),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.backupTitle') }), h('p', { class: 'muted', text: t('adm.backupText', { hour: b.hour, keep: b.keep }) }), h('div', { class: 'cluster' }, run)),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.keyTitle') }), keyNote),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.otherPlaces') }),
      b.destinations.length
        ? [h('p', { class: 'muted', text: t(b.encrypts ? 'adm.copiesEncrypted' : 'adm.copiesPlain') }), h('div', { class: 'list' }, b.destinations.map((d) => {
          const last = b.results.find((r) => r.destination === d);
          return row(d, last ? (last.ok ? t('adm.lastOk') : last.error) : t('adm.noCopyYet'), badge(last ? (last.ok ? t('adm.ok') : t('adm.failedShort')) : '–', last ? (last.ok ? 'ok' : 'danger') : ''));
        }))]
        : h('p', { class: 'muted', text: t('adm.noPlaces') })),
    h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('adm.existing') }),
      b.files.length ? h('div', { class: 'list' }, b.files.map((f) => row(fmtDate(f.at), `${kinds[f.kind] || f.kind}`, `${t('adm.database')} ${kb(f.bytes)}${f.assetBytes ? ' · ' + t('adm.scans') + ' ' + kb(f.assetBytes) : ''}`)))
        : emptyState({ icon: 'database', title: t('adm.noBackups') })));
  return { title: t('adm.backups'), sub: t('adm.backupsSub'), body };
}
