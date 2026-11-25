/* Sharing one notebook with another person, read only. The dialog lists who has it now (each can be taken back, which works at once) and picks the next person from the
 * names of the people of this installation (names only). The server decides who may share and who may read; nothing here is a check. */
import { t } from '/base/i18n.js';
import { h, api, field, notice, toast, openDialog, errorText, ApiError } from '/base/base.js';
import { API, listAll } from './util.js';

export function shareDialog(notebook) {
  const list = h('div', { class: 'list' });
  const none = h('p', { class: 'field-hint', text: t('share.nobody') });
  const addArea = h('div', { class: 'stack s' });

  async function load() {
    const [shares, people] = await Promise.all([listAll(`/notebooks/${encodeURIComponent(notebook.id)}/shares`), listAll('/people')]);
    none.hidden = shares.length > 0;
    list.replaceChildren(...shares.map(row));
    addArea.replaceChildren(addForm(people.filter((p) => !shares.some((s) => s.recipientId === p.id)), people.length));
  }

  function row(share) {
    const revoke = h('button', { class: 'btn ghost danger', type: 'button', text: t('share.revoke') });
    revoke.addEventListener('click', async () => {
      try { await api('DELETE', `${API}/shares/${share.id}`); toast(t('share.revoked'), { kind: 'ok' }); await load(); }
      catch (e) { if (!(e instanceof ApiError)) throw e; toast(errorText(e), { kind: 'danger' }); }
    });
    return h('div', { class: 'row' }, h('span', { class: 'row-main' }, h('span', { class: 'row-title', text: share.recipientName })), h('span', { class: 'row-end' }, revoke));
  }

  function addForm(free, all) {
    if (!all) return notice(t('share.noPeople'));
    if (!free.length) return h('p', { class: 'field-hint', text: t('share.everyone') });
    const pick = field({ label: t('share.pick'), name: 'person', type: 'select', value: free[0].id, options: free.map((p) => [p.id, p.name]) });
    const add = h('button', { class: 'btn primary', type: 'submit', text: t('share.add') });
    const form = h('form', { class: 'form' }, pick, h('div', { class: 'form-actions' }, add));
    form.addEventListener('submit', async (e) => {
      e.preventDefault();
      const id = form.querySelector('select').value;
      try {
        const made = await api('POST', `${API}/notebooks/${encodeURIComponent(notebook.id)}/shares`, { recipientId: id });
        toast(t('share.added', { name: made.recipientName }), { kind: 'ok' });
        await load();
      } catch (err) { if (!(err instanceof ApiError)) throw err; toast(errorText(err), { kind: 'danger' }); }
    });
    return form;
  }

  const dialog = openDialog({
    title: t('share.title', { name: notebook.title }),
    body: h('div', { class: 'stack s' }, h('p', { class: 'muted', text: t('share.intro') }), none, list, addArea),
    actions: [{ label: t('share.done'), kind: 'ghost' }],
  });
  // a dialog of this notebook does not stay on screen over another page
  addEventListener('hashchange', () => dialog.close(), { once: true });
  load().catch((e) => addArea.replaceChildren(notice(errorText(e), 'danger')));
  return dialog;
}
