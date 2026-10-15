// Keeps the menu page up to date without reloading it:
//  - a countdown to the deadline; at the deadline the order buttons disappear and "Ordering closed" is shown;
//  - "Only N left" / "Sold out" for every sandwich, asked from GET /api/menu/status every 20 seconds.
// The countdown starts from the seconds the SERVER sent (a phone's clock can be wrong). This is only for comfort:
// the server checks the deadline and the stock again when an order arrives.
(function () {
    'use strict';
    const root = document.getElementById('live-menu');
    if (!root || !window.fetch) return;

    const POLL_MS = 20000;
    const text = {
        soldOut: root.dataset.textSoldOut,
        unavailable: root.dataset.textUnavailable,
        left: root.dataset.textLeft
    };
    const countdown = document.getElementById('countdown');
    const badge = document.getElementById('ordering-badge');
    let isOpen = root.dataset.isOpen === 'true';
    let closesAt = Date.now() + Number(root.dataset.secondsLeft) * 1000;

    function orderForms() {
        return document.querySelectorAll('form.order-form');
    }

    function closeOrdering() {
        isOpen = false;
        orderForms().forEach(form => form.classList.add('d-none'));
        if (badge) {
            badge.textContent = badge.dataset.closedText;
            badge.classList.replace('text-bg-success', 'text-bg-danger');
        }
        countdown?.classList.add('d-none');
        document.getElementById('closed-alert')?.classList.remove('d-none');
        document.getElementById('guest-name-card')?.classList.add('d-none');
    }

    // Shown during the last hour: "Closes in 12:05".
    function tick() {
        if (!isOpen) return;
        const seconds = Math.max(0, Math.round((closesAt - Date.now()) / 1000));
        if (seconds === 0) {
            closeOrdering();
            return;
        }
        if (countdown) {
            const minutes = Math.floor(seconds / 60);
            const rest = String(seconds % 60).padStart(2, '0');
            countdown.textContent = countdown.dataset.template.replace('{0}', `${minutes}:${rest}`);
            countdown.classList.toggle('d-none', seconds > 3600);
        }
    }

    function stockBadge(sandwich) {
        if (!sandwich.available) {
            return ['text-bg-danger', sandwich.left === 0 ? text.soldOut : text.unavailable];
        }
        if (sandwich.left !== null && sandwich.left <= 5) {
            return ['text-bg-warning', text.left.replace('{0}', sandwich.left)];
        }
        return null;
    }

    function showStock(card, sandwich) {
        const holder = card.querySelector('.js-stock');
        const wanted = stockBadge(sandwich);
        holder.replaceChildren();
        if (wanted) {
            const span = document.createElement('span');
            span.className = `badge ${wanted[0]} d-inline-block mb-2`;
            span.textContent = wanted[1]; // textContent, never innerHTML: no way to inject HTML
            holder.append(span);
        }
        card.classList.toggle('opacity-75', !sandwich.available);
        const form = card.querySelector('form.order-form');
        if (form && isOpen) form.classList.toggle('d-none', !sandwich.available);
    }

    async function refresh() {
        try {
            const response = await fetch(root.dataset.statusUrl, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (!response.ok) return;
            const status = await response.json();

            if (status.isOpen && !isOpen) {
                location.reload(); // the canteen moved the deadline later: a fresh page shows the order buttons again
                return;
            }
            if (!status.isOpen && isOpen) closeOrdering();
            if (status.isOpen) closesAt = Date.now() + status.secondsLeft * 1000;

            const byId = new Map(status.sandwiches.map(s => [s.id, s]));
            document.querySelectorAll('[data-sandwich-id]').forEach(card => {
                const sandwich = byId.get(Number(card.dataset.sandwichId));
                if (sandwich) showStock(card, sandwich);
            });
        } catch {
            // No connection for a moment: the next round tries again.
        }
    }

    tick();
    setInterval(tick, 1000);
    setInterval(() => { if (!document.hidden) refresh(); }, POLL_MS);
    // Coming back to the tab (phone unlocked): update at once instead of waiting for the next round.
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { tick(); refresh(); } });
})();
