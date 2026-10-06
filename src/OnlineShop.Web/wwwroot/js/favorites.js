// Сердечко «в избранное» без перезагрузки страницы. Без скрипта форма работает как обычная.
(function () {
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form.classList || !form.classList.contains('fav-form')) return;
        event.preventDefault();
        var button = form.querySelector('.fav-btn');
        button.disabled = true;
        fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'Accept': 'application/json' } })
            .then(function (response) {
                if (!response.ok) throw new Error(response.status);
                return response.json();
            })
            .then(function (data) {
                // Один товар может быть на странице дважды (например, в карточке и в подсказках): меняем все кнопки.
                document.querySelectorAll('.fav-btn[data-product-id="' + button.dataset.productId + '"]').forEach(function (b) {
                    b.classList.toggle('active', data.active);
                    b.setAttribute('aria-pressed', data.active ? 'true' : 'false');
                    b.title = data.active ? 'Убрать из избранного' : 'В избранное';
                    var label = b.querySelector('.fav-label');
                    if (label) label.textContent = data.active ? 'В избранном' : 'В избранное';
                });
                var counter = document.getElementById('fav-count');
                if (counter) {
                    counter.textContent = data.count;
                    counter.hidden = data.count === 0;
                }
            })
            .catch(function () { form.submit(); })
            .finally(function () { button.disabled = false; });
    });
})();
