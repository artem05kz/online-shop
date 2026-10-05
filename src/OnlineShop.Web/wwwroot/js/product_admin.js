// The "pH" field starts collapsed on the product edit page (most products
// don't need it). If this product already has a pH value saved, open that
// section automatically so existing data is never hidden from view.
document.addEventListener('DOMContentLoaded', function () {
    var phInput = document.getElementById('Input_Ph');
    if (!phInput || !phInput.value.trim()) {
        return;
    }
    var details = phInput.closest('details');
    if (details) {
        details.open = true;
    }
});
