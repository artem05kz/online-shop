// Поле для файла (этап 6, 25.11): большая плитка .fx-drop вместо серой кнопки браузера.
// Файл можно перетащить мышкой или выбрать по нажатию; название выбранного файла показывается в плитке.
(function () {
    'use strict';

    function show(drop) {
        var input = drop.querySelector('input[type=file]');
        var label = drop.querySelector('.fx-drop-file');
        if (!input || !label) return;
        var names = Array.prototype.map.call(input.files || [], function (f) { return f.name; });
        label.textContent = names.length ? names.join(', ') : label.getAttribute('data-empty');
        drop.classList.toggle('has-file', names.length > 0);
    }

    document.addEventListener('change', function (e) {
        var drop = e.target.closest && e.target.closest('.fx-drop');
        if (drop) show(drop);
    });

    ['dragenter', 'dragover'].forEach(function (type) {
        document.addEventListener(type, function (e) {
            var drop = e.target.closest && e.target.closest('.fx-drop');
            if (!drop) return;
            e.preventDefault();
            drop.classList.add('is-over');
        });
    });
    document.addEventListener('dragleave', function (e) {
        var drop = e.target.closest && e.target.closest('.fx-drop');
        if (drop && !drop.contains(e.relatedTarget)) drop.classList.remove('is-over');
    });
    document.addEventListener('drop', function (e) {
        var drop = e.target.closest && e.target.closest('.fx-drop');
        if (!drop) return;
        e.preventDefault();
        drop.classList.remove('is-over');
        var input = drop.querySelector('input[type=file]');
        if (input && e.dataTransfer && e.dataTransfer.files.length) {
            input.files = e.dataTransfer.files;
            show(drop);
        }
    });
})();
