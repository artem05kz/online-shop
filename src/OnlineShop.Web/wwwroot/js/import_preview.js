// Предпросмотр загрузки из 1С: поиск по строкам, сортировка по клику на заголовок,
// «выбрать все» и счётчик отмеченных строк. Перенесено из шаблона Django-версии.
// Кнопки «Выбрать все» и «Снять все»: data-toggle-all="price" data-value="true".
document.addEventListener('click', function (event) {
    var button = event.target.closest && event.target.closest('[data-toggle-all]');
    if (button) toggleAll(button.getAttribute('data-toggle-all'), button.getAttribute('data-value') === 'true');
});

function toggleAll(prefix, checked) {
    document.querySelectorAll('#' + prefix + '-table tbody input[type=checkbox]').forEach(function (cb) {
        // Затрагиваем только строки, видимые при текущем поиске.
        if (cb.closest('tr').classList.contains('row-hidden')) return;
        cb.checked = checked;
    });
    updateCount(prefix);
}

function updateCount(prefix) {
    var table = document.getElementById(prefix + '-table');
    var span = document.getElementById(prefix + '-count');
    if (!table || !span) return;
    var boxes = table.querySelectorAll('tbody input[type=checkbox]');
    var checked = table.querySelectorAll('tbody input[type=checkbox]:checked').length;
    span.textContent = 'выбрано ' + checked + ' из ' + boxes.length;
}

// Название, категория, бренд и остаток лежат в полях ввода, а их текущее значение не входит
// в textContent ячейки. Поэтому поиск и сортировка читают значение поля, если оно есть.
function cellText(cell) {
    var input = cell.querySelector('input:not([type=checkbox])');
    return (input ? input.value : cell.textContent).trim();
}

function setupSearch(inputId, tableId) {
    var input = document.getElementById(inputId);
    if (!input) return;
    input.addEventListener('input', function () {
        var term = input.value.toLowerCase();
        document.querySelectorAll('#' + tableId + ' tbody tr').forEach(function (row) {
            var text = Array.prototype.map.call(row.children, cellText).join(' ').toLowerCase();
            row.classList.toggle('row-hidden', text.indexOf(term) === -1);
        });
    });
}

function setupSort(tableId) {
    var table = document.getElementById(tableId);
    if (!table) return;
    var headers = table.querySelectorAll('th');
    headers.forEach(function (th, colIndex) {
        if (!th.hasAttribute('data-sort')) return;
        th.addEventListener('click', function () {
            var tbody = table.querySelector('tbody');
            var rows = Array.prototype.slice.call(tbody.querySelectorAll('tr'));
            var type = th.getAttribute('data-sort');
            var asc = !th.classList.contains('sorted-asc');
            headers.forEach(function (h) { h.classList.remove('sorted-asc', 'sorted-desc'); });
            th.classList.add(asc ? 'sorted-asc' : 'sorted-desc');
            rows.sort(function (a, b) {
                var av = cellText(a.children[colIndex]);
                var bv = cellText(b.children[colIndex]);
                if (type === 'number') return (parseFloat(av) || 0) - (parseFloat(bv) || 0);
                return av.localeCompare(bv, 'ru');
            });
            if (!asc) rows.reverse();
            rows.forEach(function (row) { tbody.appendChild(row); });
        });
    });
}

document.querySelectorAll('.import-table').forEach(function (table) {
    table.addEventListener('change', function (e) {
        if (e.target.matches('input[type=checkbox]')) updateCount(table.id.replace('-table', ''));
    });
});

setupSearch('price-search', 'price-table');
setupSearch('stock-search', 'stock-table');
setupSort('price-table');
setupSort('stock-table');
updateCount('price');
updateCount('stock');
