/* Marks and cards: lists, no calculation. */
import { t, tp, fmtDay, fmtNumber } from '/base/i18n.js';
import { h, emptyState, badge, notice } from '/base/base.js';
import { listAll, dotEl } from './util.js';

const foldersById = async () => new Map((await listAll('/folders')).map((f) => [f.id, f]));

function grouped(items, folders, rowOf, sub) {
  const groups = new Map();
  for (const item of items) {
    if (!groups.has(item.subjectId)) groups.set(item.subjectId, []);
    groups.get(item.subjectId).push(item);
  }
  return [...groups].map(([id, list]) => {
    const folder = folders.get(id);
    return h('section', { class: 'card stack s' },
      h('h2', { class: 'card-title' }, folder ? dotEl(folder.color) : null, folder ? folder.name : t('study.noFolder')),
      sub(list),
      h('div', { class: 'list' }, list.map(rowOf)));
  });
}

export async function gradesPage({ folder }) {
  const [items, folders] = await Promise.all([listAll(`/grades${folder ? '?folder=' + encodeURIComponent(folder) : ''}`), foldersById()]);
  const row = (g) => h('div', { class: 'row' },
    h('span', { class: 'row-main' }, h('span', { class: 'row-title', text: g.title || t(`study.cat.${g.category}`) }),
      h('span', { class: 'row-sub', text: [fmtDay(g.date), t(`study.cat.${g.category}`), g.weight ? t('study.weight', { w: fmtNumber(g.weight, 1) }) : ''].filter(Boolean).join(' · ') })),
    h('span', { class: 'row-end' }, badge(fmtNumber(g.value, 1), 'accent')));
  const body = h('div', { class: 'page stack l' }, notice(t('study.gradesNote')));
  if (!items.length) body.append(emptyState({ icon: 'check-circle', title: t('study.noGrades.title'), text: t('study.noGrades.text') }));
  body.append(...grouped(items, folders, row, (list) => h('p', { class: 'muted', text: tp('study.entries', list.length) })));
  return { title: t('nav.grades'), sub: t('study.gradesSub'), back: folder ? `#/folder/${folder}` : null, body };
}

export async function cardsPage({ folder }) {
  const [items, folders] = await Promise.all([listAll(`/cards${folder ? '?folder=' + encodeURIComponent(folder) : ''}`), foldersById()]);
  const row = (c) => h('div', { class: 'row' }, h('span', { class: 'row-main' }, h('span', { class: 'row-title', text: c.front }), h('span', { class: 'row-sub', text: c.back })));
  const body = h('div', { class: 'page stack l' });
  if (!items.length) body.append(emptyState({ icon: 'copy', title: t('study.noCards.title'), text: t('study.noCards.text') }));
  body.append(...grouped(items, folders, row, (list) => h('p', { class: 'muted', text: tp('study.entries', list.length) })));
  return { title: t('nav.cards'), sub: t('study.cardsSub'), back: folder ? `#/folder/${folder}` : null, body };
}
