// Simple banner carousel: shows one slide at a time (via the "active"
// class), advances automatically, and can be paged manually with the
// arrows or the dots. Only ever one slide has "active" at once, so only
// that slide's own link is visible or clickable, so there are no mix-ups between
// banners.
(function () {
    var root = document.getElementById('banner-carousel');
    if (!root) return;

    var slides = root.querySelectorAll('.banner-slide');
    var dots = root.querySelectorAll('.banner-dot');
    if (slides.length < 2) return; // nothing to page through

    var index = 0;
    var timer = null;

    function show(i) {
        index = (i + slides.length) % slides.length;
        slides.forEach(function (slide, n) {
            slide.classList.toggle('active', n === index);
        });
        dots.forEach(function (dot, n) {
            dot.classList.toggle('active', n === index);
        });
    }

    function restartAutoplay() {
        if (timer) clearInterval(timer);
        timer = setInterval(function () { show(index + 1); }, 6000);
    }

    var nextBtn = root.querySelector('.banner-next');
    var prevBtn = root.querySelector('.banner-prev');
    if (nextBtn) nextBtn.addEventListener('click', function () { show(index + 1); restartAutoplay(); });
    if (prevBtn) prevBtn.addEventListener('click', function () { show(index - 1); restartAutoplay(); });
    dots.forEach(function (dot, n) {
        dot.addEventListener('click', function () { show(n); restartAutoplay(); });
    });

    restartAutoplay();
})();
