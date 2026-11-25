// Баннер (25.11.2026): выбранная или перетащенная картинка сразу видна в поле в форме первого экрана,
// под ним — её размер и что обрежется. Перетаскивание и подсветку поля делает admin_file_drop.js.
(function () {
    'use strict';

    var drop = document.querySelector('[data-banner-drop]');
    if (!drop) return;
    var input = drop.querySelector('input[type=file]');
    var img = drop.querySelector('.bn-drop-img');
    var note = document.getElementById('banner-note');
    var ratio = Number(drop.getAttribute('data-ratio'));
    var min = Number(drop.getAttribute('data-min'));
    var size = drop.getAttribute('data-w') + ' × ' + drop.getAttribute('data-h');
    var url = null;

    function describe(w, h) {
        var text = 'Размер ' + w + ' × ' + h + '.';
        var r = w / h;
        if (r < ratio * 0.97) {
            text += ' Картинка выше первого экрана: сверху и снизу обрежется примерно по ' +
                Math.round((h - w / ratio) / h / 2 * 100) + '%. Лучше ' + size + '.';
        } else if (r > ratio * 1.03) {
            text += ' Картинка шире первого экрана: слева и справа обрежется примерно по ' +
                Math.round((w - h * ratio) / w / 2 * 100) + '%. Лучше ' + size + '.';
        } else {
            text += ' Подходит.';
        }
        if (w < min) text += ' Маловато: на компьютере будет нечётко.';
        return text;
    }

    function show() {
        var file = input.files && input.files[0];
        if (!file) return;
        if (url) URL.revokeObjectURL(url);
        url = URL.createObjectURL(file);
        img.onload = function () {
            note.textContent = file.name + '. ' + describe(img.naturalWidth, img.naturalHeight) + ' Нажмите «Сохранить».';
        };
        img.src = url;
        img.hidden = false;
        drop.classList.add('has-file');
    }

    input.addEventListener('change', show);
    // admin_file_drop.js кладёт перетащенный файл в поле и сообщает об этом через has-file; ловим и это.
    drop.addEventListener('drop', function () { setTimeout(show, 0); });
})();
