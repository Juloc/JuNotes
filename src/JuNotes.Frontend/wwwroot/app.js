/* Notes, the front door: sign-in and setup come from the base (start), and from here the person goes on to the notebooks, and the administrator to the administration.
 * Until the web pages move to the base (milestone 2) those two are the old pages; the settings page is the cards of the base. */
import { addMessages, t } from '/base/i18n.js';
import { h, icon, api, shell, router } from '/base/base.js';
import { start } from '/base/auth.js';
import { appearanceCard, securityCard, devicesCard, historyCard, installationCard, aboutCard } from '/base/account.js';
import { peopleCard } from '/base/people.js';
import { hubCard } from '/base/hub.js';
import de from '/i18n/de.js';
import en from '/i18n/en.js';

addMessages('de', de);
addMessages('en', en);
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => { /* the app works without it */ });

start({ icon: 'file', onSignedIn: run });

function run(me) {
  const admin = me.roles.includes('admin');
  const frame = shell({
    app: { name: me.app.name, icon: 'file' },
    nav: [
      { label: t('nav.home'), icon: 'home', href: '#/' },
      { label: t('shell.settings'), icon: 'sliders', href: '#/settings', foot: true },
    ],
    person: { name: me.user },
    menu: [{ label: t('shell.settings'), icon: 'sliders', href: '#/settings' }],
    // somebody who came through the central sign-in is sent on to its sign-out, which ends its own session and tells the other apps
    onSignOut: async () => { const r = await api('POST', '/api/auth/logout'); if (r && r.hubLogoutUrl) location.assign(r.hubLogoutUrl); else location.reload(); },
  });
  document.body.append(frame.el);
  router({ '/': homePage, '/settings': settingsPage }, frame);

  function tile(href, iconName, title, text) {
    return h('a', { class: 'card stack', href },
      h('h2', { class: 'card-title' }, icon(iconName), ' ', title),
      h('p', { class: 'muted', text }),
      h('span', { class: 'btn primary' }, t('home.open'), icon('right')));
  }

  function homePage() {
    return {
      title: t('home.title'), sub: t('home.sub'),
      body: h('div', { class: 'page narrow stack' },
        tile('/Notes', 'file', t('home.notes'), t('home.notesText')),
        admin ? tile('/Admin', 'sliders', t('home.admin'), t('home.adminText')) : null),
    };
  }

  function settingsPage() {
    return {
      title: t('shell.settings'),
      body: h('div', { class: 'page narrow stack l' }, appearanceCard(), securityCard(me), devicesCard(), historyCard(),
        admin ? peopleCard(me) : null, admin ? hubCard(me) : null, admin ? installationCard() : null, aboutCard(me)),
    };
  }
}
