// "My order": asks GET /api/orders/today every 15 seconds. When the status is different from the one on the page
// (the canteen pressed Ready or Collected, or cancelled the order), the page is reloaded so the server shows the
// new state with the right texts. The phone vibrates once when the sandwich is ready (if the phone allows it).
(function () {
    'use strict';
    const live = document.getElementById('live-order');
    if (!live || !window.fetch) return;

    const POLL_MS = 15000;
    const shownStatus = live.dataset.status;

    async function check() {
        try {
            const response = await fetch(live.dataset.url, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (response.status === 204) {
                location.reload(); // no order any more: it was cancelled by the canteen
                return;
            }
            if (!response.ok) return;
            const order = await response.json();
            if (order.status !== shownStatus) {
                if (order.status === 'Prepared') navigator.vibrate?.(300);
                location.reload();
            }
        } catch {
            // No connection for a moment: the next round tries again.
        }
    }

    setInterval(() => { if (!document.hidden) check(); }, POLL_MS);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) check(); });
})();
