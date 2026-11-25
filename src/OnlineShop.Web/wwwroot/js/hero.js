// Первый экран главной: слайды товаров.
// - стрелки, точки, клавиши ← → и свайп пальцем;
// - сам листает только там, где есть мышь (компьютер), раз в 7 с; пауза при наведении и фокусе,
//   кнопка «пауза» останавливает совсем (так требует доступность: движение должно останавливаться);
// - на телефоне и при «уменьшить движение» в системе сам не листает.
(function () {
    var root = document.getElementById('hero');
    if (!root) return;
    var slides = root.querySelectorAll('.hero-slide');
    var dots = root.querySelectorAll('.hero-dot');

    // Рукописное слово тянется от левого края (чуть заходит в поле) почти до текста:
    // размер шрифта подбирается под длину слова и ширину области картинки.
    var GAP = 20; // зазор до текста, px
    function fitScripts() {
        root.querySelectorAll('.hero-visual').forEach(function (visual) {
            var script = visual.querySelector('.hero-script');
            if (!script) return;
            // offsetLeft отрицательный, когда слово заходит в поле слева (компьютер, планшет)
            // ×0.96: у рукописных букв хвосты выступают за свою ширину (x, f, s)
            // У слова есть внутренние отступы (под блик, см. CSS «Блик по рукописному слову») — считаем без них.
            script.style.fontSize = '100px';
            var pad = parseFloat(getComputedStyle(script).paddingLeft) || 0; // в px при 100px, растёт вместе со шрифтом
            var target = (visual.clientWidth - (script.offsetLeft + pad) - GAP) * 0.96;
            var w = script.offsetWidth - 2 * pad;
            if (!w) return;
            var size = 100 * target / w;
            var maxByHeight = visual.clientHeight * 0.95 / 0.8; // line-height .8
            script.style.fontSize = Math.min(size, maxByHeight) + 'px';
        });
    }
    fitScripts();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(fitScripts);
    window.addEventListener('resize', fitScripts);

    if (slides.length < 2) return;

    var index = 0, timer = null, stopped = false, hovering = false;
    var pauseBtn = root.querySelector('.hero-pause');
    var canAutoplay = window.matchMedia('(hover: hover) and (pointer: fine)').matches
        && !window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function show(i) {
        index = (i + slides.length) % slides.length;
        slides.forEach(function (slide, n) {
            var on = n === index;
            slide.classList.toggle('active', on);
            slide.setAttribute('aria-hidden', on ? 'false' : 'true');
            if (on) slide.removeAttribute('inert'); else slide.setAttribute('inert', '');
        });
        // Светлые точки и стрелки на тёмном блоке текста, тёмные — на светлом.
        root.classList.toggle('is-light', slides[index].getAttribute('data-tone') === 'light');
        dots.forEach(function (dot, n) {
            dot.classList.toggle('active', n === index);
            dot.setAttribute('aria-current', n === index ? 'true' : 'false');
        });
    }

    function stop() { if (timer) { clearInterval(timer); timer = null; } }
    function start() {
        stop();
        if (canAutoplay && !stopped && !hovering) timer = setInterval(function () { show(index + 1); }, 7000);
    }
    function go(i) { show(i); start(); }

    root.querySelector('.hero-next').addEventListener('click', function () { go(index + 1); });
    root.querySelector('.hero-prev').addEventListener('click', function () { go(index - 1); });
    dots.forEach(function (dot, n) { dot.addEventListener('click', function () { go(n); }); });

    root.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowRight') { go(index + 1); }
        else if (e.key === 'ArrowLeft') { go(index - 1); }
    });

    // свайп
    var x0 = null, y0 = null;
    root.addEventListener('touchstart', function (e) { x0 = e.touches[0].clientX; y0 = e.touches[0].clientY; }, { passive: true });
    root.addEventListener('touchend', function (e) {
        if (x0 === null) return;
        var dx = e.changedTouches[0].clientX - x0, dy = e.changedTouches[0].clientY - y0;
        if (Math.abs(dx) > 40 && Math.abs(dx) > Math.abs(dy)) go(index + (dx < 0 ? 1 : -1));
        x0 = y0 = null;
    }, { passive: true });

    if (canAutoplay && pauseBtn) {
        pauseBtn.hidden = false;
        pauseBtn.addEventListener('click', function () {
            stopped = !stopped;
            pauseBtn.classList.toggle('is-paused', stopped);
            pauseBtn.setAttribute('aria-label', stopped ? 'Включить смену слайдов' : 'Остановить смену слайдов');
            start();
        });
        root.addEventListener('mouseenter', function () { hovering = true; stop(); });
        root.addEventListener('mouseleave', function () { hovering = false; start(); });
        root.addEventListener('focusin', function () { hovering = true; stop(); });
        root.addEventListener('focusout', function () { hovering = false; start(); });
        start();
    }
})();
