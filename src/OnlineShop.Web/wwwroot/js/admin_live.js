// Автообновление панели (24.11). Раз в 30 секунд спрашиваем /admin/live, что нового:
// — числа у пунктов меню (новые заказы, записи на мойку, отзывы) обновляются сами;
// — пришёл новый заказ или запись — внизу справа плашка «Новый заказ №…» с кнопкой «Открыть» и короткий звук;
//   плашка исчезает, когда нажали «Открыть» или «×», или когда заказ/запись уже подтвердили (здесь, в другой вкладке, в Telegram);
// — на главной, в заказах и в мойке (body[data-live-reload=on]) страница сама перезагружается,
//   если вы сейчас ничего не печатаете и нет несохранённых правок; иначе ждёт, пока освободитесь.
// Звук выключается кнопкой вверху, выбор запоминается в этом браузере.
(function () {
    'use strict';
    var INTERVAL = 30000;
    var last = null;
    var pendingReload = false;
    var baseTitle = document.title;
    var soundKey = 'admin-sound';

    function soundOn() {
        try { return localStorage.getItem(soundKey) !== 'off'; } catch (e) { return true; }
    }

    function initSoundButton() {
        var button = document.getElementById('admin-sound');
        if (!button) return;
        function paint() { button.setAttribute('aria-pressed', soundOn() ? 'true' : 'false'); }
        paint();
        button.addEventListener('click', function () {
            try { localStorage.setItem(soundKey, soundOn() ? 'off' : 'on'); } catch (e) { }
            paint();
            if (soundOn()) beep();
        });
    }

    // Короткий двойной сигнал без звукового файла. Браузер разрешает звук после первого щелчка по странице.
    var audio = null;
    function beep() {
        if (!soundOn()) return;
        try {
            audio = audio || new (window.AudioContext || window.webkitAudioContext)();
            [0, 0.18].forEach(function (delay, i) {
                var osc = audio.createOscillator();
                var gain = audio.createGain();
                osc.type = 'sine';
                osc.frequency.value = i === 0 ? 880 : 1175;
                var t = audio.currentTime + delay;
                gain.gain.setValueAtTime(0.0001, t);
                gain.gain.exponentialRampToValueAtTime(0.25, t + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.16);
                osc.connect(gain).connect(audio.destination);
                osc.start(t);
                osc.stop(t + 0.18);
            });
        } catch (e) { }
    }

    function toast(kind, title, text, href, id) {
        var box = document.getElementById('live-toasts');
        if (!box) return;
        var item = document.createElement('div');
        item.className = 'live-toast is-' + kind;
        if (id) item.setAttribute('data-id', id);
        var mark = document.createElement('span');
        mark.className = 'live-toast-mark';
        mark.setAttribute('aria-hidden', 'true');
        var body = document.createElement('span');
        body.className = 'live-toast-body';
        var b = document.createElement('b');
        b.textContent = title;
        var small = document.createElement('small');
        small.textContent = text;
        body.appendChild(b);
        body.appendChild(small);
        var open = document.createElement('a');
        open.className = 'btn btn-sm';
        open.href = href;
        open.textContent = 'Открыть';
        // Открыли — значит, увидели: плашку убираем (страница уходит, поэтому запоминаем сразу).
        open.addEventListener('click', function () { item.remove(); remember(); });
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'live-toast-close';
        close.setAttribute('aria-label', 'Закрыть');
        close.textContent = '×';
        close.addEventListener('click', function () { item.remove(); remember(); });
        item.appendChild(mark);
        item.appendChild(body);
        item.appendChild(open);
        item.appendChild(close);
        box.appendChild(item);
        remember();
    }

    // Плашки переживают перезагрузку списка: храним их до закрытия (только в этой вкладке).
    function remember() {
        try {
            var list = Array.prototype.map.call(document.querySelectorAll('.live-toast'), function (t) {
                return [t.className.replace('live-toast is-', ''), t.querySelector('b').textContent, t.querySelector('small').textContent, t.querySelector('a').getAttribute('href'), Number(t.getAttribute('data-id')) || 0];
            });
            sessionStorage.setItem('admin-toasts', JSON.stringify(list));
        } catch (e) { }
    }

    function restore() {
        try {
            var list = JSON.parse(sessionStorage.getItem('admin-toasts') || '[]');
            sessionStorage.removeItem('admin-toasts');
            list.forEach(function (t) { toast(t[0], t[1], t[2], t[3], t[4]); });
        } catch (e) { }
    }

    function setCounts(data) {
        ['newOrders', 'washWaiting', 'pendingReviews'].forEach(function (key) {
            document.querySelectorAll('[data-live="' + key + '"]').forEach(function (el) {
                el.textContent = data[key];
                el.hidden = !data[key];
            });
        });
        var waiting = data.newOrders + data.washWaiting;
        document.title = (waiting > 0 ? '(' + waiting + ') ' : '') + baseTitle.replace(/^\(\d+\) /, '');
    }

    // Убираем плашки заказов и записей, которые уже не ждут ответа.
    function prune(data) {
        var changed = false;
        document.querySelectorAll('.live-toast[data-id]').forEach(function (t) {
            var id = Number(t.getAttribute('data-id'));
            var waiting = t.classList.contains('is-order') ? data.waitingOrderIds : data.waitingBookingIds;
            if (waiting && waiting.indexOf(id) < 0) { t.remove(); changed = true; }
        });
        if (changed) remember();
    }

    function busy() {
        var el = document.activeElement;
        if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') && el.type !== 'search') return true;
        if (document.querySelector('.row-edited, .unsaved-banner')) return true;
        return false;
    }

    function tryReload() {
        if (!pendingReload || busy()) return;
        location.reload();
    }

    function check() {
        fetch('/admin/live', { credentials: 'same-origin', headers: { 'Accept': 'application/json' }, cache: 'no-store' })
            .then(function (r) { return r.ok && (r.headers.get('content-type') || '').indexOf('json') >= 0 ? r.json() : null; })
            .then(function (data) {
                if (!data) return;
                setCounts(data);
                prune(data);
                var saved = null;
                try { saved = JSON.parse(sessionStorage.getItem('admin-live-last') || 'null'); } catch (e) { }
                var before = last || saved;
                if (before) {
                    var fresh = false;
                    if (data.lastOrder && (!before.lastOrder || data.lastOrder.id > before.lastOrder.id) && (data.waitingOrderIds || []).indexOf(data.lastOrder.id) >= 0) {
                        toast('order', 'Новый заказ №' + data.lastOrder.id, data.lastOrder.name + ' · ' + data.lastOrder.text, '/admin/orders/' + data.lastOrder.id, data.lastOrder.id);
                        fresh = true;
                    }
                    // Запись, которую администратор сам добавил в панели, сразу подтверждена — о ней не сигналим.
                    if (data.lastBooking && (!before.lastBooking || data.lastBooking.id > before.lastBooking.id) && (data.waitingBookingIds || []).indexOf(data.lastBooking.id) >= 0) {
                        toast('wash', 'Новая запись на мойку', data.lastBooking.name + ' · ' + data.lastBooking.text, '/admin/wash/' + data.lastBooking.id, data.lastBooking.id);
                        fresh = true;
                    }
                    if (fresh) beep();
                    if (last && data.stamp !== last.stamp && document.body.getAttribute('data-live-reload') === 'on') {
                        pendingReload = true;
                        // Даём увидеть плашку и услышать звук, потом обновляем список.
                        setTimeout(tryReload, fresh ? 4000 : 0);
                    }
                }
                last = data;
                try { sessionStorage.setItem('admin-live-last', JSON.stringify({ lastOrder: data.lastOrder, lastBooking: data.lastBooking })); } catch (e) { }
            })
            .catch(function () { });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initSoundButton();
        restore();
        check();
        setInterval(check, INTERVAL);
        // Отложенное обновление: как только поле потеряло фокус — пробуем ещё раз.
        document.addEventListener('focusout', function () { setTimeout(tryReload, 500); });
    });
})();
