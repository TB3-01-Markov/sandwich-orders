// Opens the running app in Chromium at phone, tablet and desktop sizes, takes screenshots and measures
// layout problems: content wider than the screen, touch targets that are too small on phones, and form
// fields with a font below 16px (iPhone Safari zooms into those when tapped).
// Usage: BASE_URL=http://127.0.0.1:5080 node check.mjs   (writes ./out)
import { chromium } from 'playwright';
import fs from 'node:fs';

const BASE = process.env.BASE_URL ?? 'http://127.0.0.1:5080';
const STAFF_EMAIL = process.env.STAFF_EMAIL ?? 'staff@example.test';
const STAFF_PASSWORD = process.env.STAFF_PASSWORD ?? 'Staff-Demo-1234';
const OUT = 'out';
fs.mkdirSync(OUT, { recursive: true });

const viewports = [
  { name: 'phone-360', width: 360, height: 740, mobile: true },
  { name: 'phone-390', width: 390, height: 844, mobile: true },
  { name: 'tablet-768', width: 768, height: 1024, mobile: true },
  { name: 'laptop-1366', width: 1366, height: 768, mobile: false },
  { name: 'desktop-1920', width: 1920, height: 1080, mobile: false },
];

const browser = await chromium.launch();
const report = [];

async function newContext(vp, storageState) {
  return browser.newContext({
    viewport: { width: vp.width, height: vp.height },
    isMobile: vp.mobile && vp.width < 700,
    hasTouch: vp.mobile,
    deviceScaleFactor: 1,
    storageState,
  });
}

async function measure(page, vp, name) {
  const result = await page.evaluate((isPhone) => {
    const width = document.documentElement.clientWidth;
    const scrollWidth = document.documentElement.scrollWidth;
    const insideScroller = (el) => {
      for (let p = el.parentElement; p; p = p.parentElement) {
        const o = getComputedStyle(p).overflowX;
        if (o === 'auto' || o === 'scroll') return true;
      }
      return false;
    };
    const describe = (el) => {
      const cls = typeof el.className === 'string' ? el.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
      const text = (el.innerText || el.value || '').trim().replace(/\s+/g, ' ').slice(0, 30);
      return `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}${cls ? '.' + cls : ''}${text ? ' "' + text + '"' : ''}`;
    };
    const visible = (el) => { const b = el.getBoundingClientRect(); return b.width > 0 && b.height > 0; };

    const tooWide = [];
    if (scrollWidth > width) {
      for (const el of document.querySelectorAll('body *')) {
        const b = el.getBoundingClientRect();
        if (b.width > 0 && b.right > width + 1 && !insideScroller(el)) tooWide.push(`${describe(el)} (right edge ${Math.round(b.right)}px)`);
        if (tooWide.length >= 6) break;
      }
    }
    const scrollers = [...document.querySelectorAll('.table-responsive')]
      .filter(el => el.scrollWidth > el.clientWidth + 1)
      .map(el => `${describe(el.querySelector('th') ?? el)}: ${el.scrollWidth}px in ${el.clientWidth}px`);

    const controls = [...document.querySelectorAll('a.btn, button, select, input:not([type=hidden]), textarea')].filter(visible);
    const smallTargets = isPhone
      ? controls.filter(el => el.getBoundingClientRect().height < 40 && !['checkbox', 'radio'].includes(el.type))
          .map(el => `${describe(el)} ${Math.round(el.getBoundingClientRect().height)}px`)
      : [];
    const smallFonts = isPhone
      ? controls.filter(el => ['INPUT', 'SELECT', 'TEXTAREA'].includes(el.tagName) && !['checkbox', 'radio', 'file'].includes(el.type)
            && parseFloat(getComputedStyle(el).fontSize) < 16)
          .map(el => `${describe(el)} ${getComputedStyle(el).fontSize}`)
      : [];
    return { width, scrollWidth, tooWide, scrollers, smallTargets: [...new Set(smallTargets)].slice(0, 8), smallTargetCount: smallTargets.length, smallFonts: [...new Set(smallFonts)].slice(0, 5) };
  }, vp.width < 700);
  report.push({ viewport: vp.name, page: name, ...result });
}

async function shot(page, vp, name, options = {}) {
  await page.waitForLoadState('networkidle');
  await measure(page, vp, name);
  await page.screenshot({ path: `${OUT}/${vp.name}--${name}.png`, ...options });
}

// ---- Setup with one desktop browser: staff opens ordering, adds a notice, three guests order -----------------
const desk = viewports[3];
const staffCtx = await newContext(desk);
const staff = await staffCtx.newPage();
await staff.goto(`${BASE}/Account/Login`);
await staff.fill('#Email', STAFF_EMAIL);
await staff.fill('#Password', STAFF_PASSWORD);
await staff.click('form button[type=submit].w-100');
await staff.waitForURL(u => !u.pathname.includes('/Account/Login'));

await staff.goto(`${BASE}/Staff/Deadline`);
await staff.fill('#Deadline', '23:59');
await staff.click('main form button.btn-primary');
await staff.waitForLoadState('networkidle');

await staff.goto(`${BASE}/Staff/AnnouncementEdit`);
await staff.fill('#MessageEn', 'The canteen is closed next week for the holidays.');
await staff.fill('#MessageNl', 'De kantine is volgende week gesloten wegens vakantie.');
await staff.click('main form button[type=submit].btn-primary');
await staff.waitForLoadState('networkidle');

const guestNames = ['Ada Lovelace', 'Grace Hopper', 'Alan Turing'];
let firstGuestState;
for (let i = 0; i < guestNames.length; i++) {
  const ctx = await newContext(desk);
  const p = await ctx.newPage();
  await p.goto(`${BASE}/`);
  await p.fill('#guestName', guestNames[i]);
  const form = p.locator('form.order-form').nth(i * 3);
  if (i === 1) await form.locator('select').selectOption({ index: 1 });
  await form.locator('button[type=submit]').click();
  await p.waitForURL(u => u.pathname.includes('/Orders/Today'));
  if (i === 0) firstGuestState = await ctx.storageState();
  await ctx.close();
}

await staff.goto(`${BASE}/Staff/Today`);
await staff.locator('form button.btn-success').first().click(); // first order: Ready
await staff.waitForLoadState('networkidle');
const staffState = await staffCtx.storageState();
const editHref = await staff.goto(`${BASE}/Staff/Menu`).then(() => staff.locator('a[href*="SandwichEdit/"], a[href*="SandwichEdit?id"]').first().getAttribute('href'));
await staffCtx.close();

// ---- Every page at every size -----------------------------------------------------------------------------
for (const vp of viewports) {
  const guestCtx = await newContext(vp);
  const g = await guestCtx.newPage();
  await g.goto(`${BASE}/`);
  await shot(g, vp, '01-menu');
  await g.locator('form.order-form').first().scrollIntoViewIfNeeded();
  await g.evaluate(() => window.scrollBy(0, -120));
  await shot(g, vp, '02-menu-first-sandwich');
  await g.goto(`${BASE}/`);
  await g.click('summary');
  await shot(g, vp, '03-menu-filter-open');
  if (vp.width < 992) {
    await g.goto(`${BASE}/`);
    await g.click('.navbar-toggler');
    await g.waitForTimeout(400);
    await shot(g, vp, '04-navigation-open');
  }
  await g.goto(`${BASE}/`);
  await g.click('button[name=culture][value=nl]');
  await g.waitForLoadState('networkidle');
  await g.locator('form.order-form').first().scrollIntoViewIfNeeded();
  await g.evaluate(() => window.scrollBy(0, -120));
  await shot(g, vp, '05-menu-dutch');
  await g.click('button[name=culture][value=en]');
  await g.goto(`${BASE}/Account/Login`);
  await shot(g, vp, '06-login', { fullPage: true });
  await g.goto(`${BASE}/Account/Register`);
  await shot(g, vp, '07-register', { fullPage: true });
  await guestCtx.close();

  const orderedCtx = await newContext(vp, firstGuestState);
  const o = await orderedCtx.newPage();
  await o.goto(`${BASE}/Orders/Today`);
  await shot(o, vp, '08-my-order', { fullPage: true });
  await o.goto(`${BASE}/`);
  await shot(o, vp, '09-menu-after-ordering');
  await orderedCtx.close();

  const sCtx = await newContext(vp, staffState);
  const s = await sCtx.newPage();
  await s.goto(`${BASE}/Staff/Today`);
  await shot(s, vp, '10-staff-today', { fullPage: true });
  await s.goto(`${BASE}/Staff/Menu`);
  await shot(s, vp, '11-staff-menu');
  if (editHref) {
    await s.goto(`${BASE}${editHref}`);
    await shot(s, vp, '12-staff-sandwich-edit', { fullPage: true });
  }
  await s.goto(`${BASE}/Staff/Deadline`);
  await shot(s, vp, '13-staff-deadline', { fullPage: true });
  await s.goto(`${BASE}/Staff/Announcements`);
  await shot(s, vp, '14-staff-announcements', { fullPage: true });
  await s.goto(`${BASE}/Staff/AnnouncementEdit`);
  await shot(s, vp, '15-staff-announcement-edit', { fullPage: true });
  await sCtx.close();
}

await browser.close();

// ---- Report -------------------------------------------------------------------------------------------------
fs.writeFileSync(`${OUT}/report.json`, JSON.stringify(report, null, 2));
const lines = ['# UI check', '', `Base URL: ${BASE}`, '',
  '| Viewport | Page | Wider than screen | Tables scrolling sideways | Small touch targets (<40px) | Inputs < 16px |',
  '|---|---|---|---|---|---|'];
for (const r of report) {
  lines.push(`| ${r.viewport} | ${r.page} | ${r.scrollWidth > r.width ? `**${r.scrollWidth}px > ${r.width}px**: ${r.tooWide.join('; ')}` : 'no'} | ${r.scrollers.join('; ') || '-'} | ${r.smallTargetCount ? `${r.smallTargetCount}: ${r.smallTargets.join('; ')}` : '-'} | ${r.smallFonts.join('; ') || '-'} |`);
}
fs.writeFileSync(`${OUT}/report.md`, lines.join('\n') + '\n');
console.log(lines.join('\n'));
