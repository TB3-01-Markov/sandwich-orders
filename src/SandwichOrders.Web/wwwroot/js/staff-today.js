// The live "Today's orders" page for the canteen: the single-page part of the app.
//  - Every 15 seconds the lists are rebuilt from GET /api/staff/orders/today; new orders are highlighted.
//  - Ready / Collected / Cancel are sent to the API with fetch; the page itself is never reloaded.
//  - Every POST carries the page's anti-forgery token in the RequestVerificationToken header (the API uses the
//    same login cookie as the pages, so without the token another site could make the browser send requests).
// Names come from customers, so everything is put on the page with textContent, never innerHTML.
(function () {
    'use strict';
    const live = document.getElementById('staff-live');
    if (!live || !window.fetch) return;

    const POLL_MS = 15000;
    const text = live.dataset;
    const errorBox = document.getElementById('live-error');
    const knownIds = new Set([...document.querySelectorAll('[data-order-id]')].map(e => e.dataset.orderId));
    let busy = false;

    // ---- small DOM helpers ------------------------------------------------------------------------------------
    function el(tag, className, content) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (content !== undefined && content !== null) element.textContent = content;
        return element;
    }

    function phoneLink(phone, className) {
        const link = el('a', className, phone);
        link.href = 'tel:' + phone;
        return link;
    }

    function button(label, className, action, order, status) {
        const b = el('button', 'btn btn-sm ' + className, label);
        b.type = 'button';
        b.dataset.action = action;
        b.dataset.id = order.id;
        if (status) b.dataset.status = status;
        return b;
    }

    function actions(order) {
        const list = [];
        if (order.status === 'Placed') list.push(button(text.textReady, 'btn-success', 'status', order, 'Prepared'));
        if (order.status === 'Placed' || order.status === 'Prepared') {
            list.push(button(text.textCollected, 'btn-outline-secondary', 'status', order, 'Collected'));
            list.push(button(text.textCancel, 'btn-outline-danger', 'cancel', order));
        }
        return list;
    }

    const badgeClass = { Prepared: 'text-bg-success', Collected: 'text-bg-secondary' };

    // ---- rendering ----------------------------------------------------------------------------------------------
    function render(day) {
        const hasOrders = day.orders.length > 0;
        document.getElementById('no-orders').classList.toggle('d-none', hasOrders);
        document.getElementById('orders-content').classList.toggle('d-none', !hasOrders);

        document.getElementById('prep-rows').replaceChildren(...day.rows.map(row => {
            const tr = el('tr');
            tr.append(el('td', null, row.number), el('td', null, row.name), el('td', null, row.breadText),
                el('td', 'text-end', row.quantity));
            return tr;
        }));
        document.getElementById('prep-total').textContent = `${day.totalSandwiches} · ${day.totalAmountText}`;

        const rows = [];
        const cards = [];
        for (const order of day.orders) {
            const isNew = !knownIds.has(String(order.id));
            knownIds.add(String(order.id));
            const muted = order.status === 'Collected' ? 'text-body-secondary' : '';

            // Table (tablets and computers): one row per sandwich, the buttons on the first row.
            order.lines.forEach((line, index) => {
                const tr = el('tr', `${muted} ${isNew ? 'live-new' : ''}`);
                tr.dataset.orderId = order.id;
                const phone = el('td', 'text-nowrap');
                if (order.phone) phone.append(phoneLink(order.phone));
                const buttons = el('td', 'text-end text-nowrap');
                if (index === 0) actions(order).forEach(b => buttons.append(b, ' '));
                tr.append(el('td', null, order.customerName), phone, el('td', null, `#${line.number} ${line.name}`),
                    el('td', null, line.breadText), el('td', 'text-end', line.priceText), el('td', null, order.statusText),
                    buttons);
                rows.push(tr);
            });

            // Card (phones).
            const card = el('div', `card ${muted} ${isNew ? 'live-new' : ''}`);
            card.dataset.orderId = order.id;
            const body = el('div', 'card-body p-3');
            const head = el('div', 'd-flex justify-content-between align-items-start gap-2');
            head.append(el('strong', null, order.customerName),
                el('span', 'badge ' + (badgeClass[order.status] ?? 'text-bg-primary'), order.statusText));
            body.append(head);
            if (order.phone) body.append(phoneLink(order.phone, 'small'));
            order.lines.forEach(line =>
                body.append(el('div', 'mt-1', `#${line.number} ${line.name} · ${line.breadText} · ${line.priceText}`)));
            const buttons = actions(order);
            if (buttons.length) {
                const holder = el('div', 'd-flex flex-wrap gap-2 mt-2');
                holder.append(...buttons);
                body.append(holder);
            }
            card.append(body);
            cards.push(card);
        }
        document.getElementById('order-rows').replaceChildren(...rows);
        document.getElementById('order-cards').replaceChildren(...cards);

        const updated = document.getElementById('live-updated');
        if (updated) {
            const time = new Date().toLocaleTimeString(document.documentElement.lang || undefined,
                { hour: '2-digit', minute: '2-digit', second: '2-digit' });
            updated.textContent = text.textUpdated.replace('{0}', time);
        }
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.classList.toggle('d-none', !message);
    }

    // ---- talking to the API -------------------------------------------------------------------------------------
    async function refresh() {
        if (busy) return;
        try {
            const response = await fetch(live.dataset.url, { headers: { Accept: 'application/json' }, cache: 'no-store' });
            if (response.status === 401 || response.status === 403) {
                location.reload(); // signed out in the meantime: the page sends the user to the login page
                return;
            }
            if (!response.ok) return;
            render(await response.json());
        } catch {
            // No connection for a moment: the next round tries again.
        }
    }

    async function send(target) {
        const { action, id, status } = target.dataset;
        if (action === 'cancel' && !confirm(text.textConfirmCancel)) return;

        busy = true;
        target.disabled = true;
        try {
            const response = await fetch(`/api/staff/orders/${id}/${action}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Accept: 'application/json',
                    RequestVerificationToken: document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? ''
                },
                body: JSON.stringify(action === 'status' ? { status } : {})
            });
            if (response.ok) {
                showError('');
            } else {
                // 404 / 409 come with a translated message from the server.
                const problem = await response.json().catch(() => null);
                showError(problem?.error ?? text.textError);
            }
        } catch {
            showError(text.textError);
        } finally {
            busy = false;
            await refresh();
        }
    }

    document.getElementById('orders-content').addEventListener('click', event => {
        const target = event.target.closest('button[data-action]');
        if (target) send(target);
    });

    // With JavaScript, "Refresh" updates the lists in place instead of reloading the page.
    document.getElementById('refresh-button')?.addEventListener('click', event => {
        event.preventDefault();
        refresh();
    });

    refresh();
    setInterval(() => { if (!document.hidden) refresh(); }, POLL_MS);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
})();
