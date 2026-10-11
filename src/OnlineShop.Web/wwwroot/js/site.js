// Общие мелочи витрины: выпадающий каталог в шапке, показ фильтров на
// телефоне и «плюс/минус» у количества товара.
(function () {
    'use strict';

    // Каталог открывается боковой выезжающей панелью. Пока она открыта, страница под
    // ней не прокручивается, иначе прокрутка «проваливается» на фон.
    var catalogToggle = document.getElementById('catalog-toggle');
    var catalogMenu = document.getElementById('catalog-menu');
    var catalogBackdrop = document.getElementById('catalog-backdrop');
    var catalogClose = document.getElementById('catalog-close');

    if (catalogToggle && catalogMenu) {
        var openCatalog = function () {
            if (catalogBackdrop) {
                catalogBackdrop.hidden = false;
                // Даём браузеру отрисовать элемент до запуска перехода.
                requestAnimationFrame(function () { catalogBackdrop.classList.add('open'); });
            }
            catalogMenu.classList.add('open');
            catalogMenu.setAttribute('aria-hidden', 'false');
            catalogToggle.setAttribute('aria-expanded', 'true');
            document.body.style.overflow = 'hidden';
            var firstLink = catalogMenu.querySelector('a');
            if (firstLink) firstLink.focus({ preventScroll: true });
        };

        var closeCatalog = function () {
            catalogMenu.classList.remove('open');
            catalogMenu.setAttribute('aria-hidden', 'true');
            catalogToggle.setAttribute('aria-expanded', 'false');
            document.body.style.overflow = '';
            if (catalogBackdrop) {
                catalogBackdrop.classList.remove('open');
                setTimeout(function () {
                    if (!catalogMenu.classList.contains('open')) catalogBackdrop.hidden = true;
                }, 220);
            }
        };

        catalogToggle.addEventListener('click', function () {
            if (catalogMenu.classList.contains('open')) closeCatalog(); else openCatalog();
        });
        if (catalogClose) catalogClose.addEventListener('click', closeCatalog);
        if (catalogBackdrop) catalogBackdrop.addEventListener('click', closeCatalog);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && catalogMenu.classList.contains('open')) closeCatalog();
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
    // количества, отдельная кнопка «применить» не нужна. Небольшая пауза
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
        // max: сколько штук есть в наличии, больше заказать нельзя.
        var max = parseInt(input.getAttribute('max') || '0', 10) || Infinity;
        var value = parseInt(input.value || '1', 10);
        if (isNaN(value)) value = min;
        value += (button.getAttribute('data-qty') === 'plus' ? 1 : -1);
        var clamped = Math.min(max, Math.max(min, value));
        if (clamped === parseInt(input.value, 10)) return;
        input.value = clamped;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
})();
