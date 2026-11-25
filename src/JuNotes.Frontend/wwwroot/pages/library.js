/* The library: folders, notebooks, one notebook with its pages, one page. Reading only. */
import { t, tp } from '/base/i18n.js';
import { h, icon, api, field, emptyState, badge, notice } from '/base/base.js';
import { API, listAll, coverEl, dotEl, pictureEl } from './util.js';

const folderIcon = (folder) => (folder.isSubject ? 'file' : 'folder');

function folderTile(folder) {
  return h('a', { class: 'tile', href: `#/folder/${folder.id}` },
    h('span', { class: 'tile-lead' }, icon(folderIcon(folder))),
    h('span', null, h('span', { class: 'tile-title', text: folder.name })));
}

function notebookTile(notebook) {
  return h('a', { class: 'tile notebook', href: `#/notebook/${notebook.id}` },
    coverEl(notebook),
    h('span', null, h('span', { class: 'tile-title', text: notebook.title }), h('span', { class: 'tile-sub', text: t(notebook.type === 'whiteboard' ? 'lib.whiteboard' : 'lib.notebook') })));
}

function searchField() {
  const box = field({ label: t('lib.search'), name: 'q', type: 'search', placeholder: t('lib.search'), autocomplete: 'off', maxlength: 100 });
  box.querySelector('label').classList.add('sr-only');
  box.querySelector('input').addEventListener('keydown', (e) => {
    const q = e.target.value.trim();
    if (e.key === 'Enter' && q) location.hash = '#/search/' + encodeURIComponent(q);
  });
  return box;
}

/** The first page: folders of the top level and the notebooks that lie in no folder. */
export async function homePage() {
  const [folders, loose] = await Promise.all([listAll('/folders'), listAll('/notebooks?folder=root')]);
  const top = folders.filter((f) => !f.parentId);
  const body = h('div', { class: 'page stack l' }, searchField());
  if (!top.length && !loose.length) {
    body.append(emptyState({ icon: 'inbox', title: t('lib.empty.title'), text: t('lib.empty.text') }));
  }
  if (top.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.folders') }), h('div', { class: 'tiles' }, top.map(folderTile))));
  if (loose.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.loose') }), h('div', { class: 'tiles' }, loose.map(notebookTile))));
  return { title: t('nav.library'), sub: t('lib.sub'), body };
}

export async function folderPage({ id }) {
  const [folders, notebooks] = await Promise.all([listAll('/folders'), listAll(`/notebooks?folder=${encodeURIComponent(id)}`)]);
  const folder = folders.find((f) => f.id === id);
  if (!folder) throw Object.assign(new Error('not found'), { notFound: true });
  const children = folders.filter((f) => f.parentId === id);
  const body = h('div', { class: 'page stack l' });
  if (folder.isSubject) {
    body.append(h('div', { class: 'chips' },
      h('a', { class: 'chip', href: `#/grades/${id}` }, icon('check-circle'), t('nav.grades')),
      h('a', { class: 'chip', href: `#/cards/${id}` }, icon('copy'), t('nav.cards'))));
  }
  if (!children.length && !notebooks.length) body.append(emptyState({ icon: 'folder', title: t('lib.folderEmpty.title'), text: t('lib.folderEmpty.text') }));
  if (children.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.folders') }), h('div', { class: 'tiles' }, children.map(folderTile))));
  if (notebooks.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.notebooks') }), h('div', { class: 'tiles' }, notebooks.map(notebookTile))));
  return { title: folder.name, back: folder.parentId ? `#/folder/${folder.parentId}` : '#/', body };
}

export async function notebookPage({ id }) {
  const detail = await api('GET', `${API}/notebooks/${encodeURIComponent(id)}`);
  const pages = await listAll(`/notebooks/${encodeURIComponent(id)}/pages`);
  const notebook = detail.notebook;
  const rows = pages.map((page, index) => h('a', { class: 'row', href: `#/page/${page.id}` },
    h('span', { class: 'row-lead', text: String(index + 1) }),
    h('span', { class: 'row-main' },
      h('span', { class: 'row-title', text: page.heading || t('lib.pageN', { n: index + 1 }) }),
      h('span', { class: 'row-sub', text: page.printedText ? page.printedText.split('\n')[0] : t(`lib.type.${page.contentType}`) })),
    h('span', { class: 'row-end' }, page.contentType !== 'drawing' ? badge(t(`lib.type.${page.contentType}`)) : null, icon('right'))));
  const body = h('div', { class: 'page stack l' },
    h('div', { class: 'notebook-head' }, coverEl(notebook, 'small'), h('p', { class: 'muted', text: tp('lib.pages', pages.length) })),
    pages.length ? h('div', { class: 'list' }, rows) : emptyState({ icon: 'file', title: t('lib.noPages.title'), text: t('lib.noPages.text') }));
  return { title: notebook.title, back: notebook.folderId ? `#/folder/${notebook.folderId}` : '#/', body };
}

/** One page as it is drawn, with the text that belongs to it; the pages before and after, in the order of the notebook. */
export async function pagePage({ id }) {
  const view = await api('GET', `${API}/pages/${encodeURIComponent(id)}`);
  const page = view.page;
  const [detail, pages] = await Promise.all([api('GET', `${API}/notebooks/${page.notebookId}`), listAll(`/notebooks/${page.notebookId}/pages`)]);
  const index = pages.findIndex((p) => p.id === page.id);
  const board = view.notebookType === 'whiteboard';
  const sheet = h('div', { class: board ? 'board' : 'sheet' });
  const layer = h('div', { class: 'layer' });
  const picture = pictureEl(view.svg);
  if (!board && page.contentType === 'scan' && page.assetId) sheet.append(h('img', { class: 'scan', src: `${API}/assets/${page.assetId}`, alt: t('lib.scanAlt'), loading: 'lazy' }));
  if (picture) layer.append(picture);
  sheet.append(layer);
  if (!board && page.contentType === 'pdf' && page.assetId) {
    sheet.append(h('a', { class: 'btn ghost pdf-link', href: `${API}/assets/${page.assetId}`, target: '_blank', rel: 'noopener noreferrer' }, icon('external'), t('lib.openPdf', { n: page.pdfPageIndex + 1 })));
  }
  if (!board) {
    if (page.heading) sheet.append(h('div', { class: 'heading', text: page.heading }));
    if (page.printedText) sheet.append(h('div', { class: 'printed', text: page.printedText }));
  }
  const body = h('div', { class: 'page stack' }, sheet);
  if (!board && page.solution) body.append(h('details', { class: 'card' }, h('summary', { text: t('lib.solution') }), h('p', { class: 'pre', text: page.solution })));
  if (!board && page.additions.length) body.append(h('section', { class: 'card stack s' }, h('h2', { class: 'card-title', text: t('lib.additions') }), page.additions.map((a) => h('p', { class: 'pre', text: a }))));
  const link = (target, label, ic) => (target
    ? h('a', { class: 'btn outline', href: `#/page/${target.id}` }, ic === 'left' ? icon(ic) : null, label, ic === 'right' ? icon(ic) : null)
    : h('span'));
  body.append(h('div', { class: 'cluster between' }, link(pages[index - 1], t('lib.prev'), 'left'), h('span', { class: 'muted', text: t('lib.pageOf', { n: index + 1, total: pages.length }) }), link(pages[index + 1], t('lib.next'), 'right')));
  return { title: detail.notebook.title, back: `#/notebook/${page.notebookId}`, body };
}

export async function searchPage({ q }) {
  const term = decodeURIComponent(q);
  const result = await api('GET', `${API}/search?q=${encodeURIComponent(term)}`);
  const body = h('div', { class: 'page stack l' }, searchField());
  body.querySelector('input').value = term;
  if (!result.folders.length && !result.notebooks.length) body.append(emptyState({ icon: 'search', title: t('lib.noHits.title'), text: t('lib.noHits.text') }));
  if (result.folders.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.folders') }), h('div', { class: 'tiles' }, result.folders.map(folderTile))));
  if (result.notebooks.length) body.append(h('section', { class: 'stack s' }, h('h2', { class: 'section-title', text: t('lib.notebooks') }), h('div', { class: 'tiles' }, result.notebooks.map(notebookTile))));
  body.append(notice(t('lib.searchNote')));
  return { title: t('lib.searchTitle', { q: term }), back: '#/', body };
}
