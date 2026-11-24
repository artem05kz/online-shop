// Запись на мойку: при смене мойки, услуги или дня подгружает свободное время и обновляет «Ваша запись».
// Без скрипта страница тоже работает: время показано на выбранный день, форма отправляется как обычно.
(function () {
    'use strict';
    var form = document.getElementById('wash-form');
    if (!form) return;
    var slotsBox = document.getElementById('wash-slots');
    var empty = document.getElementById('wash-slots-empty');
    var url = form.getAttribute('data-slots-url');

    function checked(name) { return form.querySelector('input[name="' + name + '"]:checked'); }
    function setText(id, text) { var el = document.getElementById(id); if (el) el.textContent = text; }

    function summary() {
        var wash = checked('Form.Wash'), service = checked('Form.Service'), day = checked('Form.Date'), time = checked('Form.Time');
        setText('sum-wash', wash ? wash.getAttribute('data-name') : '');
        setText('sum-service', service ? service.getAttribute('data-name') : '');
        setText('sum-when', day && time ? day.getAttribute('data-label') + ', ' + time.value : 'выберите время');
    }

    var request = 0;
    function loadSlots() {
        var wash = checked('Form.Wash'), service = checked('Form.Service'), day = checked('Form.Date');
        if (!wash || !service || !day) return;
        var previous = checked('Form.Time');
        var keep = previous ? previous.value : null;
        var mine = ++request;
        slotsBox.classList.add('is-loading');
        fetch(url + '?wash=' + encodeURIComponent(wash.value) + '&service=' + encodeURIComponent(service.value)
            + '&date=' + encodeURIComponent(day.value), { headers: { 'Accept': 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (list) {
                if (mine !== request) return; // пришёл ответ на старый выбор
                slotsBox.textContent = '';
                var anyFree = false;
                list.forEach(function (slot) {
                    var label = document.createElement('label');
                    label.className = 'wash-slot' + (slot.free ? '' : ' is-busy');
                    var input = document.createElement('input');
                    input.type = 'radio';
                    input.name = 'Form.Time';
                    input.value = slot.time;
                    input.disabled = !slot.free;
                    if (slot.free && slot.time === keep) input.checked = true;
                    var span = document.createElement('span');
                    span.textContent = slot.time;
                    label.appendChild(input);
                    label.appendChild(span);
                    slotsBox.appendChild(label);
                    if (slot.free) anyFree = true;
                });
                empty.hidden = anyFree;
                slotsBox.classList.remove('is-loading');
                summary();
            })
            .catch(function () { slotsBox.classList.remove('is-loading'); });
    }

    form.addEventListener('change', function (e) {
        var name = e.target.name;
        if (name === 'Form.Wash' || name === 'Form.Service' || name === 'Form.Date') loadSlots();
        summary();
    });

    // Согласие и время проверяем до отправки: сразу видно, чего не хватает.
    var box = form.querySelector('.consent input[type="checkbox"]');
    form.addEventListener('submit', function (e) {
        if (!checked('Form.Time')) {
            e.preventDefault();
            slotsBox.classList.add('is-invalid');
            slotsBox.scrollIntoView({ block: 'center', behavior: 'smooth' });
            return;
        }
        if (box && !box.checked) {
            e.preventDefault();
            var consent = box.closest('.consent');
            consent.classList.add('is-invalid');
            consent.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }
    });
    slotsBox.addEventListener('change', function () { slotsBox.classList.remove('is-invalid'); });
    if (box) box.addEventListener('change', function () { box.closest('.consent').classList.remove('is-invalid'); });

    summary();
})();
