// Подсказки под строкой поиска: товары с фото и ценой появляются по мере набора.
// Стрелки вверх и вниз выбирают товар, Enter открывает его, Esc закрывает список.
// Без скрипта поиск работает как обычно: форма уходит на страницу результатов.
(function () {
    'use strict';

    var input = document.getElementById('search-input');
    var box = document.getElementById('search-suggest');
    if (!input || !box) return;

    var timer = null;
    var lastQuery = '';
    var active = -1;
    var controller = null;

    function items() { return box.querySelectorAll('[role=option]'); }

    function close() {
        box.hidden = true;
        box.innerHTML = '';
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
        active = -1;
    }

    function highlight(index) {
        var list = items();
        if (!list.length) return;
        active = (index + list.length) % list.length;
        list.forEach(function (el, i) { el.classList.toggle('active', i === active); });
        input.setAttribute('aria-activedescendant', list[active].id);
    }

    function el(tag, cls, text) {
        var node = document.createElement(tag);
        if (cls) node.className = cls;
        if (text) node.textContent = text;
        return node;
    }

    function render(products, query) {
        box.innerHTML = '';
        if (!products.length) {
            box.appendChild(el('div', 'suggest-empty', 'Ничего не нашлось. Нажмите Enter, чтобы искать по описанию и артикулу.'));
        }
        products.forEach(function (p, i) {
            var a = el('a', 'suggest-item');
            a.href = p.url;
            a.id = 'suggest-' + i;
            a.setAttribute('role', 'option');
            var img = el('img');
            img.src = p.image;
            img.alt = '';
            a.appendChild(img);
            var text = el('span', 'suggest-text');
            text.appendChild(el('span', 'suggest-name', p.name));
            text.appendChild(el('span', 'suggest-price' + (p.inStock ? '' : ' out'), p.price + (p.inStock ? '' : ', нет в наличии')));
            a.appendChild(text);
            box.appendChild(a);
        });
        var all = el('a', 'suggest-all', 'Все результаты по «' + query + '»');
        all.href = '/search?q=' + encodeURIComponent(query);
        all.id = 'suggest-all';
        all.setAttribute('role', 'option');
        box.appendChild(all);
        box.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        active = -1;
    }

    function load() {
        var query = input.value.trim();
        if (query.length < 2) { lastQuery = ''; close(); return; }
        if (query === lastQuery) return;
        lastQuery = query;
        if (controller) controller.abort();
        controller = new AbortController();
        fetch('/search/suggest?q=' + encodeURIComponent(query), { signal: controller.signal })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (products) { if (input.value.trim() === query) render(products, query); })
            .catch(function () { /* оборванный запрос: пользователь уже печатает дальше */ });
    }

    input.addEventListener('input', function () {
        clearTimeout(timer);
        // Небольшая пауза, чтобы не спрашивать сервер на каждую букву.
        timer = setTimeout(load, 220);
    });
    input.addEventListener('focus', function () { if (box.innerHTML) { box.hidden = false; input.setAttribute('aria-expanded', 'true'); } });

    input.addEventListener('keydown', function (e) {
        if (box.hidden) return;
        if (e.key === 'ArrowDown') { e.preventDefault(); highlight(active + 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); highlight(active - 1); }
        else if (e.key === 'Escape') { close(); }
        else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); items()[active].click(); }
    });

    document.addEventListener('click', function (e) {
        if (!box.contains(e.target) && e.target !== input) {
            box.hidden = true;
            input.setAttribute('aria-expanded', 'false');
        }
    });
})();
