// Подсвечивает строки списка, в которых что-то поменяли, но ещё не
// сохранили. Подсветка снимается, если вернуть прежнее значение, и
// пропадает после нажатия «Сохранить» — страница перезагружается.
//
// Дополнительно предупреждаем, если уйти со страницы с несохранёнными
// правками: иначе изменения молча теряются.
(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', function () {
        var form = document.getElementById('changelist-form');
        if (!form) return;

        var rows = form.querySelectorAll('#result_list tbody tr');
        if (!rows.length) return;

        var banner = null;
        var saving = false;

        function initialValue(field) {
            if (field.type === 'checkbox' || field.type === 'radio') {
                return field.defaultChecked ? '1' : '';
            }
            if (field.tagName === 'SELECT') {
                var picked = Array.prototype.filter.call(field.options, function (o) { return o.defaultSelected; });
                return picked.length ? picked[0].value : '';
            }
            return field.defaultValue;
        }

        function currentValue(field) {
            if (field.type === 'checkbox' || field.type === 'radio') {
                return field.checked ? '1' : '';
            }
            return field.value;
        }

        function editedCount() {
            return form.querySelectorAll('tr.row-edited').length;
        }

        function updateBanner() {
            var count = editedCount();
            if (count === 0) {
                if (banner) { banner.remove(); banner = null; }
                return;
            }
            if (!banner) {
                banner = document.createElement('p');
                banner.className = 'unsaved-banner';
                form.insertBefore(banner, form.firstChild);
            }
            banner.textContent = 'Изменено строк: ' + count
                + '. Нажмите «Сохранить» внизу страницы, иначе правки не применятся.';
        }

        function refreshRow(row) {
            var fields = row.querySelectorAll('input:not([type=hidden]), select, textarea');
            var changed = false;
            Array.prototype.forEach.call(fields, function (field) {
                // Галочка выбора строки для массового действия — не правка данных.
                if (field.classList.contains('action-select')) return;
                if (currentValue(field) !== initialValue(field)) changed = true;
            });

            row.classList.toggle('row-edited', changed);

            var note = row.querySelector('.unsaved-note');
            if (changed && !note) {
                var firstCell = row.querySelector('th, td');
                if (firstCell) {
                    note = document.createElement('span');
                    note.className = 'unsaved-note';
                    note.textContent = 'не сохранено';
                    firstCell.appendChild(note);
                }
            } else if (!changed && note) {
                note.remove();
            }
            updateBanner();
        }

        Array.prototype.forEach.call(rows, function (row) {
            row.addEventListener('input', function () { refreshRow(row); });
            row.addEventListener('change', function () { refreshRow(row); });
        });

        form.addEventListener('submit', function () { saving = true; });

        window.addEventListener('beforeunload', function (e) {
            if (!saving && editedCount() > 0) {
                e.preventDefault();
                e.returnValue = '';
            }
        });
    });
})();
