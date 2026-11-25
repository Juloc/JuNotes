/* Helpers of the reading pages: the path of the web API, lists that come page by page, the cover of a notebook and the picture of a page. */
import { h, api } from '/base/base.js';

export const API = '/api/web/v1';

/**
 * All rows of a list that the API gives page by page (the lists of one person are short; at most 25 pages of 200). If the list changed between two pages (the server says
 * cursor.stale) it starts again from the top, once; what was seen twice is kept once.
 */
export async function listAll(path, retried = false) {
  const seen = new Map();
  let cursor = null;
  for (let i = 0; i < 25; i++) {
    const sep = path.includes('?') ? '&' : '?';
    let page;
    try { page = await api('GET', `${API}${path}${sep}limit=200${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`); }
    catch (e) { if (e.code === 'cursor.stale' && !retried) return listAll(path, true); throw e; }
    for (const item of page.items) seen.set(item.id, item);
    cursor = page.next;
    if (!cursor) break;
  }
  return [...seen.values()];
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

// What the picture of a page may contain: the elements and attributes the server writes (SvgAllowList.cs; a test keeps these lists the same as the server's).
const ELEMENTS = {
  svg: ['viewBox', 'preserveAspectRatio'],
  g: ['class', 'stroke-width', 'fill', 'stroke-linecap', 'stroke-linejoin', 'transform'],
  rect: ['x', 'y', 'width', 'height', 'rx', 'fill', 'fill-opacity', 'stroke', 'stroke-width'],
  line: ['x1', 'y1', 'x2', 'y2', 'stroke', 'stroke-width'],
  circle: ['cx', 'cy', 'r', 'fill', 'stroke'],
  path: ['d', 'stroke', 'stroke-width', 'stroke-opacity'],
  text: ['x', 'y', 'font-size', 'font-weight', 'text-anchor', 'dominant-baseline', 'fill', 'stroke'],
  image: ['href', 'x', 'y', 'width', 'height', 'preserveAspectRatio'],
  foreignObject: ['x', 'y', 'width', 'height'],
  div: ['style'],
};
const STYLE_PROPERTIES = ['font', 'color', 'white-space', 'overflow-wrap'];
const SVG_NS = 'http://www.w3.org/2000/svg';
const XHTML_NS = 'http://www.w3.org/1999/xhtml';
const ASSET = new RegExp(`^${API.replaceAll('/', '\\/')}\\/assets\\/[0-9a-fA-F-]{36}$`);
const risky = (value) => /url\(|javascript:|expression\(|@import/i.test(value);

// The style attribute would be ignored by the content policy; the few properties the text box uses are set through the object model instead.
function applyStyle(el, css) {
  for (const declaration of css.split(';')) {
    const i = declaration.indexOf(':');
    if (i <= 0) continue;
    const property = declaration.slice(0, i).trim().toLowerCase();
    const value = declaration.slice(i + 1).trim();
    if (STYLE_PROPERTIES.includes(property) && !risky(value)) el.style.setProperty(property, value);
  }
}

/** A copy of the element made of allowed parts only: new nodes, never the parsed ones. */
function copy(source, inForeignObject) {
  const name = source.localName;
  const isDiv = inForeignObject && source.namespaceURI === XHTML_NS && name === 'div';
  if (!isDiv && (source.namespaceURI !== SVG_NS || !Object.hasOwn(ELEMENTS, name) || name === 'div')) return null;
  const el = document.createElementNS(isDiv ? XHTML_NS : SVG_NS, name);
  for (const attr of source.attributes) {
    if (attr.namespaceURI || !ELEMENTS[name].includes(attr.localName)) continue;
    if (attr.localName === 'href') { if (name === 'image' && ASSET.test(attr.value)) el.setAttribute('href', attr.value); continue; }
    if (attr.localName === 'style') { applyStyle(el, attr.value); continue; }
    if (!risky(attr.value)) el.setAttribute(attr.localName, attr.value);
  }
  for (const child of source.childNodes) {
    if (child.nodeType === Node.ELEMENT_NODE) {
      // text holds only text; a foreignObject only the one div
      if (name !== 'text' && !isDiv) { const made = copy(child, name === 'foreignObject'); if (made) el.append(made); }
    } else if (child.nodeType === Node.TEXT_NODE && (name === 'text' || isDiv)) el.append(document.createTextNode(child.nodeValue));
  }
  return el;
}

/**
 * The picture of a page as the server drew it (paper, objects, strokes). The server has already let only its own elements through; here it is parsed as SVG and built
 * again from an allow list (elements, attributes, the one file path, four CSS properties), as new nodes and never as markup. Nothing else gets in.
 */
export function pictureEl(markup) {
  const doc = new DOMParser().parseFromString(markup, 'image/svg+xml');
  const root = doc.documentElement;
  if (!root || doc.querySelector('parsererror')) return null;
  const node = copy(root, false);
  if (!node || node.localName !== 'svg') return null;
  node.classList.add('picture');
  return node;
}
