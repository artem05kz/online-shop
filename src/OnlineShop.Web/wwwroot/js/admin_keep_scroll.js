// Панель (25.11): после нажатия кнопки в списке («Приехал», «Помыли», «Подтвердить»…) страница перезагружается,
// но остаётся там же, где была, а не прыгает наверх. Работает для форм внутри блока с data-keep-scroll.
// Сообщения «Отмечено…» показываются плашкой внизу экрана (admin-flash) и через несколько секунд исчезают.
(function () {
    'use strict';
    var key = 'admin-keep-scroll';

    document.addEventListener('submit', function (e) {
        if (!e.target.closest || !e.target.closest('[data-keep-scroll]')) return;
        try { sessionStorage.setItem(key, JSON.stringify({ path: location.pathname, y: window.scrollY, at: Date.now() })); } catch (err) { }
    }, true);

    function restore() {
        var saved = null;
        try {
            saved = JSON.parse(sessionStorage.getItem(key) || 'null');
            sessionStorage.removeItem(key);
        } catch (err) { }
        if (saved && saved.path === location.pathname && Date.now() - saved.at < 15000) {
            window.scrollTo(0, saved.y);
        }
    }

    function flash() {
        var box = document.querySelector('.admin-flash');
        if (!box) return;
        var timer = null;
        function hide() { box.classList.add('is-hiding'); setTimeout(function () { box.remove(); }, 300); }
        function start() { timer = setTimeout(hide, 5000); }
        box.addEventListener('mouseenter', function () { clearTimeout(timer); });
        box.addEventListener('mouseleave', start);
        var close = box.querySelector('.admin-flash-close');
        if (close) close.addEventListener('click', hide);
        start();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { restore(); flash(); });
    } else {
        restore();
        flash();
    }
})();
