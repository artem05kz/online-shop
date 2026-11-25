// Конструктор слайда с товаром (25.11.2026): живое превью первого экрана.
// Выбор товара подставляет заголовок, рукописное слово, текст и цену; рендер сразу виден, а цвет фона
// берётся с этикетки — тот же расчёт, что в Services/SlideColors.cs. Перетаскивание файла — admin_file_drop.js.
(function () {
    'use strict';

    var form = document.getElementById('slide-form');
    if (!form) return;
    var preview = document.getElementById('slide-preview');
    var product = document.getElementById('slide-product');
    var fileInput = document.getElementById('slide-image');
    var bgInput = document.getElementById('slide-bg');
    var scriptInput = document.getElementById('slide-script-color');
    var touched = document.getElementById('slide-touched');
    var img = preview.querySelector('[data-preview="image"]');
    var noImg = preview.querySelector('.sl-noimg');
    var fields = {};
    form.querySelectorAll('[data-field]').forEach(function (f) { fields[f.getAttribute('data-field')] = f; });
    var placeholders = { title: 'Заголовок слайда', text: '', script: '' };

    // ---------- цвета (как SlideColors.cs) ----------
    function toHsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        var max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
        if (max - min < 1e-6) return [0, 0, l];
        var d = max - min, s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
        return [h * 60, s, l];
    }
    function fromHsl(h, s, l) {
        var c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs(h / 60 % 2 - 1)), m = l - c / 2, rgb;
        if (h < 60) rgb = [c, x, 0]; else if (h < 120) rgb = [x, c, 0]; else if (h < 180) rgb = [0, c, x];
        else if (h < 240) rgb = [0, x, c]; else if (h < 300) rgb = [x, 0, c]; else rgb = [c, 0, x];
        return '#' + rgb.map(function (v) {
            return ('0' + Math.round(Math.min(1, Math.max(0, v + m)) * 255).toString(16)).slice(-2);
        }).join('');
    }
    function parse(hex) { var v = parseInt(hex.slice(1), 16); return [(v >> 16) & 255, (v >> 8) & 255, v & 255]; }
    function clamp(v, a, b) { return Math.min(b, Math.max(a, v)); }
    function isDark(hex) { var c = parse(hex); return (0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]) / 255 < 0.6; }
    function scriptFor(bg) {
        var c = parse(bg), hsl = toHsl(c[0], c[1], c[2]);
        return fromHsl(hsl[0], Math.min(1, hsl[1] + 0.1), clamp(isDark(bg) ? hsl[2] + 0.15 : hsl[2] - 0.15, 0.08, 0.92));
    }
    function fromImage(image) {
        var side = Math.max(image.naturalWidth, image.naturalHeight);
        var scale = Math.min(1, 160 / side);
        var w = Math.max(1, Math.round(image.naturalWidth * scale)), h = Math.max(1, Math.round(image.naturalHeight * scale));
        var canvas = document.createElement('canvas');
        canvas.width = w; canvas.height = h;
        var ctx = canvas.getContext('2d');
        ctx.drawImage(image, 0, 0, w, h);
        var data;
        try { data = ctx.getImageData(0, 0, w, h).data; } catch (e) { return null; }
        var bins = 24, weight = new Array(bins).fill(0), sums = [];
        for (var i = 0; i < bins; i++) sums.push([0, 0, 0]);
        for (var p = 0; p < data.length; p += 4) {
            if (data[p + 3] < 200) continue;
            var hsl = toHsl(data[p], data[p + 1], data[p + 2]);
            if (hsl[1] < 0.35 || hsl[2] < 0.15 || hsl[2] > 0.85) continue;
            var bin = Math.floor(hsl[0] / 360 * bins) % bins;
            weight[bin] += hsl[1];
            sums[bin][0] += data[p] * hsl[1]; sums[bin][1] += data[p + 1] * hsl[1]; sums[bin][2] += data[p + 2] * hsl[1];
        }
        var best = 0;
        for (var b = 1; b < bins; b++) if (weight[b] > weight[best]) best = b;
        if (weight[best] <= 0) return null;
        var avg = toHsl(sums[best][0] / weight[best], sums[best][1] / weight[best], sums[best][2] / weight[best]);
        var warmGreen = avg[0] >= 45 && avg[0] <= 160;
        var bg = fromHsl(avg[0], clamp(avg[1], 0.55, 0.9), warmGreen ? clamp(avg[2], 0.6, 0.7) : clamp(avg[2], 0.45, 0.62));
        return { bg: bg, script: scriptFor(bg) };
    }

    // ---------- превью ----------
    function paint() {
        var bg = bgInput.value, script = scriptInput.value;
        preview.style.setProperty('--ps-bg', bg);
        preview.style.setProperty('--ps-script', script);
        preview.style.setProperty('--ps-ink', isDark(bg) ? '#ffffff' : '#0f0f0f');
        bgInput.closest('.sl-color').querySelector('small').textContent = bg;
        scriptInput.closest('.sl-color').querySelector('small').textContent = script;
    }
    function fitScript() {
        var word = preview.querySelector('[data-preview="script"]');
        var box = preview.querySelector('.sl-visual');
        word.style.fontSize = '';
        var max = box.clientWidth * 1.02;
        if (word.scrollWidth > max) {
            word.style.fontSize = (parseFloat(getComputedStyle(word).fontSize) * max / word.scrollWidth) + 'px';
        }
    }
    function text(name) {
        var value = fields[name].value.trim();
        preview.querySelector('[data-preview="' + name + '"]').textContent = value || placeholders[name];
        if (name === 'script') fitScript();
    }
    Object.keys(fields).forEach(function (name) {
        fields[name].addEventListener('input', function () { text(name); });
    });

    // ---------- товар ----------
    var auto = {};
    function option() { return product.options[product.selectedIndex]; }
    function fill(force) {
        var o = option();
        preview.querySelector('[data-preview="price"]').textContent = o && o.value ? o.getAttribute('data-price') : '';
        if (!o || !o.value) return;
        ['title', 'script', 'text'].forEach(function (name) {
            var field = fields[name], suggestion = o.getAttribute('data-' + name) || '';
            // Своё, написанное руками, не трогаем: заменяем пустое или то, что подставили мы же.
            if (force || field.value.trim() === '' || field.value === auto[name]) {
                field.value = suggestion;
                auto[name] = suggestion;
                text(name);
            }
        });
    }
    product.addEventListener('change', function () { fill(false); });
    document.getElementById('slide-refill').addEventListener('click', function () { fill(true); });

    // ---------- рендер и цвет ----------
    function pick() {
        if (!img.src || img.hidden || !img.complete || !img.naturalWidth) return;
        var colors = fromImage(img);
        var help = document.getElementById('slide-color-help');
        if (!colors) {
            help.textContent = 'На рендере не нашлось яркого цвета — выберите фон сами, нажав на квадратик.';
            return;
        }
        bgInput.value = colors.bg;
        scriptInput.value = colors.script;
        touched.value = 'true';
        help.textContent = 'Цвет взят с этикетки. Можно выбрать свой — нажмите на квадратик.';
        paint();
    }
    var url = null;
    function showFile() {
        var file = fileInput.files && fileInput.files[0];
        if (!file) return;
        if (url) URL.revokeObjectURL(url);
        url = URL.createObjectURL(file);
        img.onload = function () { pick(); fitScript(); };
        img.src = url;
        img.hidden = false;
        noImg.hidden = true;
    }
    fileInput.addEventListener('change', showFile);
    fileInput.closest('.fx-drop').addEventListener('drop', function () { setTimeout(showFile, 0); });
    document.getElementById('slide-pick').addEventListener('click', pick);
    bgInput.addEventListener('input', function () {
        scriptInput.value = scriptFor(bgInput.value);
        touched.value = 'true';
        paint();
    });
    scriptInput.addEventListener('input', function () { touched.value = 'true'; paint(); });

    if (document.fonts && document.fonts.ready) document.fonts.ready.then(fitScript);
    window.addEventListener('resize', fitScript);
    fill(false);
    paint();
    fitScript();
})();
