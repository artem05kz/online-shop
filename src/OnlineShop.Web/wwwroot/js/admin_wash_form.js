// Форма записи на мойку в панели (CRM, 24.11).
// — «другой день»: выбранная в календаре дата становится выбранным днём;
// — смена услуги подставляет её цену, если цену ещё не меняли вручную;
// — «Оплачено» показывает выбор способа оплаты.
(function () {
    'use strict';
    document.addEventListener('DOMContentLoaded', function () {
        var form = document.querySelector('[data-wash-form]');
        if (!form) return;

        var pick = form.querySelector('[data-date-pick]');
        if (pick) {
            pick.addEventListener('change', function () {
                if (!pick.value) return;
                var radio = form.querySelector('input[name="Input.Date"][value="' + pick.value + '"]');
                if (!radio) {
                    var label = document.createElement('label');
                    label.className = 'od-chip wf-chip';
                    radio = document.createElement('input');
                    radio.type = 'radio';
                    radio.name = 'Input.Date';
                    radio.value = pick.value;
                    var span = document.createElement('span');
                    var parts = pick.value.split('-');
                    span.textContent = parts[2] + '.' + parts[1];
                    label.appendChild(radio);
                    label.appendChild(span);
                    pick.closest('label').before(label);
                }
                radio.checked = true;
            });
        }

        var price = form.querySelector('#Input_Price');
        var auto = price && price.value;
        form.querySelectorAll('input[name="Input.Service"]').forEach(function (r) {
            r.addEventListener('change', function () {
                if (!price || !r.dataset.price) return;
                if (!price.value || price.value === auto) {
                    price.value = r.dataset.price.replace(/\.0+$/, '');
                    auto = price.value;
                }
            });
        });

        var paid = form.querySelector('[data-paid]');
        var methods = form.querySelector('[data-methods]');
        if (paid && methods) {
            paid.addEventListener('change', function () { methods.hidden = !paid.checked; });
        }
    });
})();
