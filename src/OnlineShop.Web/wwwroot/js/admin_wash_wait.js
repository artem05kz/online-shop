// Отсчёт «Ждём клиента · N мин» (мойка, 25.11). Правило: опоздал больше чем на 15 минут — запись
// отменяется. Сервер присылает, сколько секунд осталось (data-wait-left); здесь число минут уменьшается само,
// а когда время вышло — страница обновляется и показывает «Опоздал» с кнопкой «Не приехал».
(function () {
    'use strict';
    var items = document.querySelectorAll('[data-wait-left]');
    if (!items.length) return;
    var started = Date.now();

    function busy() {
        var el = document.activeElement;
        return el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT');
    }

    function tick() {
        var passed = (Date.now() - started) / 1000;
        var expired = false;
        items.forEach(function (el) {
            var left = Number(el.getAttribute('data-wait-left')) - passed;
            if (left <= 0) {
                expired = true;
                el.textContent = 'время вышло';
            } else {
                el.textContent = Math.ceil(left / 60) + ' мин';
            }
        });
        if (expired && !busy()) location.reload();
    }

    setInterval(tick, 10000);
})();
