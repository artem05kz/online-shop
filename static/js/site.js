// Общие мелочи витрины: выпадающий каталог в шапке, показ фильтров на
// телефоне и «плюс/минус» у количества товара.
(function () {
    'use strict';

    // Каталог в шапке
    var catalogToggle = document.getElementById('catalog-toggle');
    var catalogMenu = document.getElementById('catalog-menu');
    if (catalogToggle && catalogMenu) {
        catalogToggle.addEventListener('click', function (e) {
            e.stopPropagation();
            var isOpen = catalogMenu.classList.toggle('open');
            catalogToggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        });
        // Клик мимо меню и Esc — закрыть
        document.addEventListener('click', function (e) {
            if (catalogMenu.classList.contains('open') && !catalogMenu.contains(e.target)) {
                catalogMenu.classList.remove('open');
                catalogToggle.setAttribute('aria-expanded', 'false');
            }
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && catalogMenu.classList.contains('open')) {
                catalogMenu.classList.remove('open');
                catalogToggle.setAttribute('aria-expanded', 'false');
            }
        });
    }

    // Кнопка «Фильтры» на телефоне
    var filtersToggle = document.getElementById('filters-toggle');
    var filters = document.getElementById('filters');
    if (filtersToggle && filters) {
        filtersToggle.addEventListener('click', function () {
            var isOpen = filters.classList.toggle('open');
            filtersToggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        });
    }

    // Сортировка отправляет форму сразу при выборе
    var sortSelect = document.getElementById('sort-select');
    if (sortSelect) {
        sortSelect.addEventListener('change', function () {
            sortSelect.form.submit();
        });
    }

    // Формы с пометкой data-autosubmit отправляются сами при изменении
    // количества — отдельная кнопка «применить» не нужна. Небольшая пауза
    // нужна, чтобы при нажатии «плюс» несколько раз подряд страница
    // перезагружалась один раз, а не после каждого нажатия.
    var autoSubmitTimer = null;
    document.addEventListener('change', function (e) {
        var form = e.target.closest ? e.target.closest('form[data-autosubmit]') : null;
        if (!form) return;
        clearTimeout(autoSubmitTimer);
        autoSubmitTimer = setTimeout(function () { form.submit(); }, 500);
    });

    // Количество: минус / плюс
    document.addEventListener('click', function (e) {
        var button = e.target.closest('[data-qty]');
        if (!button) return;
        var wrap = button.closest('.qty');
        var input = wrap ? wrap.querySelector('input') : null;
        if (!input) return;
        var min = parseInt(input.getAttribute('min') || '1', 10);
        var value = parseInt(input.value || '1', 10);
        if (isNaN(value)) value = min;
        value += (button.getAttribute('data-qty') === 'plus' ? 1 : -1);
        input.value = Math.max(min, value);
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
})();
