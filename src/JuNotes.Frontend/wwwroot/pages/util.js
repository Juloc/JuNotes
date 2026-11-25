/* Helpers of the reading pages: the path of the web API, lists that come page by page, the cover of a notebook and the picture of a page. */
import { h, api } from '/base/base.js';

export const API = '/api/web/v1';

/** All rows of a list that the API gives page by page (the lists of one person are short; at most 25 pages of 200). */
export async function listAll(path) {
  const items = [];
  let cursor = null;
  for (let i = 0; i < 25; i++) {
    const sep = path.includes('?') ? '&' : '?';
    const page = await api('GET', `${API}${path}${sep}limit=200${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`);
    items.push(...page.items);
    cursor = page.next;
    if (!cursor) break;
  }
  return items;
}

const HEX = /^#[0-9a-fA-F]{6}$/;
const GUID = /^[0-9a-fA-F-]{36}$/;

/** The front of a notebook as the person made it. The colours come from the data, so they go in through the object model (the content policy allows no style attribute) and only as plain hex. */
export function coverEl(notebook, cls = '') {
  const cover = notebook.cover;
  const el = h('div', { class: `cover ${cls}`.trim() });
  if (cover) {
    const a = HEX.test(cover.colorA) ? cover.colorA : null;
    const b = HEX.test(cover.colorB) ? cover.colorB : a;
    if (cover.style === 'image' && GUID.test(cover.assetId || '')) {
      el.style.backgroundImage = `url(${API}/assets/${cover.assetId})`;
      el.style.backgroundSize = 'cover';
      el.style.backgroundPosition = 'center';
    } else if (a) {
      el.style.background = cover.style === 'color' ? a : `linear-gradient(135deg, ${a}, ${b})`;
    }
    if (cover.emoji) el.append(h('span', { class: 'cover-emoji', text: cover.emoji }));
  }
  return el;
}

/** A folder colour as a dot, with the same care. */
export function dotEl(color) {
  const el = h('span', { class: 'dot' });
  if (HEX.test(color || '')) el.style.background = color;
  return el;
}

/**
 * The picture of a page as the server drew it (paper, objects, strokes). It is parsed as SVG and put in as nodes, never as markup; scripts, event handlers and foreign
 * links are dropped, and style attributes (which the content policy would ignore) are applied through the object model.
 */
export function pictureEl(markup) {
  const doc = new DOMParser().parseFromString(markup, 'image/svg+xml');
  const root = doc.documentElement;
  if (!root || root.localName !== 'svg' || doc.querySelector('parsererror')) return null;
  for (const el of root.querySelectorAll('script, iframe, object, embed')) el.remove();
  const node = document.importNode(root, true);
  for (const el of [node, ...node.querySelectorAll('*')]) {
    for (const attr of [...el.attributes]) {
      const name = attr.name.toLowerCase();
      if (name.startsWith('on')) el.removeAttribute(attr.name);
      else if ((name === 'href' || name === 'xlink:href') && !attr.value.startsWith(`${API}/assets/`)) el.removeAttribute(attr.name);
    }
    const css = el.getAttribute('style');
    if (css !== null) {
      el.removeAttribute('style');
      for (const declaration of css.split(';')) {
        const i = declaration.indexOf(':');
        if (i > 0) el.style.setProperty(declaration.slice(0, i).trim(), declaration.slice(i + 1).trim());
      }
    }
  }
  node.classList.add('picture');
  return node;
}
