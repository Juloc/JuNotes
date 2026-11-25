/* Notes: the front door (sign-in and setup come from the base), the library and the study pages, and the settings. Reading only; what is written on the iPad is read here.
 * Pages are in pages/, texts in i18n/. */
import { addMessages, t } from '/base/i18n.js';
import { h, icon, api, shell, router } from '/base/base.js';
import { start } from '/base/auth.js';
import { appearanceCard, securityCard, devicesCard, historyCard, installationCard, aboutCard } from '/base/account.js';
import { peopleCard } from '/base/people.js';
import { hubCard } from '/base/hub.js';
import de from '/i18n/de.js';
import en from '/i18n/en.js';
import { homePage, folderPage, notebookPage, pagePage, searchPage, sharedNotebookPage, sharedPagePage } from '/pages/library.js';
import { gradesPage, cardsPage } from '/pages/study.js';
import { pairingCard } from '/pages/devices.js';
import { overviewPage, codexPage, profilesPage, devicesPage, backupsPage } from '/pages/admin.js';

addMessages('de', de);
addMessages('en', en);
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => { /* the app works without it */ });

// A page the browser brings back from its memory of going back and forth (after sign-out, say) is not shown as it was: it is loaded again.
addEventListener('pageshow', (e) => { if (e.persisted) location.reload(); });

start({ icon: 'file', onSignedIn: run });

function run(me) {
  const admin = me.roles.includes('admin');
  const frame = shell({
    app: { name: me.app.name, icon: 'file' },
    nav: [
      { label: t('nav.library'), icon: 'home', href: '#/' },
      { label: t('nav.grades'), icon: 'check-circle', href: '#/grades' },
      { label: t('nav.cards'), icon: 'copy', href: '#/cards' },
      ...(admin ? [{ label: t('nav.admin'), icon: 'sliders', href: '#/admin' }] : []),
      { label: t('shell.settings'), icon: 'user', href: '#/settings', foot: true },
    ],
    person: { name: me.user },
    menu: [{ label: t('shell.settings'), icon: 'sliders', href: '#/settings' }],
    // somebody who came through the central sign-in is sent on to its sign-out, which ends its own session and tells the other apps
    onSignOut: async () => {
      const r = await api('POST', '/api/auth/logout');
      // the worker keeps only the static shell (SEC-08); the copies are emptied at sign-out all the same
      if ('caches' in window) await caches.keys().then((keys) => Promise.all(keys.map((k) => caches.delete(k)))).catch(() => { /* nothing to empty */ });
      // the content leaves the page before the page does, and no search term stays in the address of this history entry
      document.body.replaceChildren();
      history.replaceState(null, '', location.pathname);
      if (r && r.hubLogoutUrl) location.assign(r.hubLogoutUrl); else location.reload();
    },
  });
  document.body.append(frame.el);
  router({
    '/': homePage,
    '/folder/:id': folderPage,
    '/notebook/:id': notebookPage,
    '/shared/notebook/:id': sharedNotebookPage,
    '/shared/page/:id': sharedPagePage,
    '/page/:id': pagePage,
    '/search/:q': searchPage,
    '/grades': () => gradesPage({}),
    '/grades/:folder': gradesPage,
    '/cards': () => cardsPage({}),
    '/cards/:folder': cardsPage,
    '/settings': settingsPage,
    ...(admin ? { '/admin': overviewPage, '/admin/codex': codexPage, '/admin/profiles': profilesPage, '/admin/devices': devicesPage, '/admin/backups': backupsPage } : {}),
  }, frame);

  function settingsPage() {
    return {
      title: t('shell.settings'),
      body: h('div', { class: 'page narrow stack l' }, appearanceCard(), securityCard(me), devicesCard(), pairingCard(), historyCard(),
        admin ? peopleCard(me) : null, admin ? hubCard(me) : null, admin ? installationCard() : null, aboutCard(me)),
    };
  }
}
