// Общее поведение форм вместо атрибутов onsubmit/onchange в разметке: такие атрибуты запрещает
// Content-Security-Policy, потому что ими же пользуются вставленные злоумышленником скрипты.
//   data-confirm="Удалить?"  у формы: спросить подтверждение перед отправкой;
//   data-autosubmit          у поля: отправить форму сразу после выбора значения.
(function () {
    document.addEventListener('submit', function (event) {
        var form = event.target;
        var question = form.getAttribute && form.getAttribute('data-confirm');
        if (question && !window.confirm(question)) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);

    document.addEventListener('change', function (event) {
        var field = event.target;
        if (field.hasAttribute && field.hasAttribute('data-autosubmit') && field.form) {
            field.form.submit();
        }
    });
})();
