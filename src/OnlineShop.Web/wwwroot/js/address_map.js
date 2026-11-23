// Выбор точки на карте: адрес доставки в кабинете и точка магазина в панели.
//
// Два режима, выбирает страница через data-provider у блока карты:
//   * twogis: карта 2ГИС и поиск адреса по точке через 2ГИС, нужен ключ (data-key);
//   * osm: открытые карты OpenStreetMap, ключ не нужен. Так сайт работает, пока ключа 2ГИС нет.
//
// Какие поля заполнять, страница указывает атрибутами data-lat, data-lng и, если нужно,
// data-city, data-street, data-house (это id полей формы). Клик по карте заполняет их заново:
// это осознанное действие, и подставленное раньше значение должно обновиться.
(function () {
    'use strict';

    var mapEl = document.querySelector('[data-map-picker]');
    if (!mapEl) return;

    function field(name) {
        var id = mapEl.getAttribute('data-' + name);
        return id ? document.getElementById(id) : null;
    }
    var latInput = field('lat');
    var lngInput = field('lng');
    var cityInput = field('city');
    var streetInput = field('street');
    var houseInput = field('house');
    var hint = document.getElementById(mapEl.getAttribute('data-hint') || 'map-hint');

    var AKTAU = { lat: 43.6525, lng: 51.1575 };
    function parse(v) { var n = parseFloat(String(v || '').replace(',', '.')); return isNaN(n) ? null : n; }
    var saved = latInput && lngInput && parse(latInput.value) !== null && parse(lngInput.value) !== null
        ? { lat: parse(latInput.value), lng: parse(lngInput.value) }
        : null;

    function setHint(text) { if (hint) hint.textContent = text; }

    function saveCoords(lat, lng) {
        if (latInput) latInput.value = Number(lat).toFixed(6);
        if (lngInput) lngInput.value = Number(lng).toFixed(6);
        if (latInput) latInput.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // Подставляем то, что удалось определить. Пустые куски не затираем: например, если номер
    // дома карта не знает, оставляем введённый вручную.
    function fillFields(parts) {
        if (parts.city && cityInput) cityInput.value = parts.city;
        if (parts.street && streetInput) streetInput.value = parts.street;
        if (parts.house && houseInput) houseInput.value = parts.house;
    }

    // Колесо мыши над картой листает страницу, а не карту: иначе страница «застревает»,
    // пока курсор над картой. Приблизить можно кнопками + и −, двойным кликом или колесом с Ctrl.
    var wheelNote = null, wheelTimer = 0;
    function showWheelNote() {
        if (!wheelNote) {
            wheelNote = document.createElement('div');
            wheelNote.className = 'map-wheel-note';
            wheelNote.textContent = 'Чтобы приблизить карту, зажмите Ctrl и крутите колесо или нажмите +';
            mapEl.appendChild(wheelNote);
        }
        wheelNote.classList.add('is-on');
        clearTimeout(wheelTimer);
        wheelTimer = setTimeout(function () { wheelNote.classList.remove('is-on'); }, 1400);
    }

    var NOT_FOUND = 'Точка выбрана. Адрес не определился, впишите улицу вручную.';
    var needsAddress = !!(cityInput || streetInput || houseInput);

    /* ------------------------------ 2ГИС ------------------------------ */
    function initTwoGis(key) {
        var start = saved || AKTAU;
        var map = new mapgl.Map(mapEl, {
            key: key,
            center: [start.lng, start.lat],
            zoom: saved ? 17 : 13,
            lang: 'ru',
            disableZoomOnScroll: true,
        });
        var marker = saved ? new mapgl.Marker(map, { coordinates: [saved.lng, saved.lat] }) : null;

        function lookup(lat, lng) {
            if (!needsAddress) { setHint('Точка выбрана.'); return; }
            setHint('Определяем адрес...');
            var url = 'https://catalog.api.2gis.com/3.0/items/geocode'
                + '?lat=' + encodeURIComponent(lat) + '&lon=' + encodeURIComponent(lng)
                + '&fields=items.address,items.adm_div&locale=ru_KZ&key=' + encodeURIComponent(key);
            fetch(url)
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    var item = data && data.result && data.result.items && data.result.items[0];
                    if (!item) { setHint(NOT_FOUND); return; }
                    var parts = { city: '', street: '', house: '' };
                    (item.adm_div || []).forEach(function (d) {
                        if (d.type === 'city' || (!parts.city && d.type === 'settlement')) parts.city = d.name;
                    });
                    var components = (item.address && item.address.components) || [];
                    components.forEach(function (c) {
                        if (c.street && !parts.street) parts.street = c.street;
                        if (c.number && !parts.house) parts.house = c.number;
                    });
                    // В Актау адреса чаще по микрорайонам: «14-й микрорайон, 5» приходит в address_name.
                    if (!parts.street && item.address_name) {
                        var pieces = item.address_name.split(',');
                        parts.street = pieces[0].trim();
                        if (!parts.house && pieces.length > 1) parts.house = pieces[pieces.length - 1].trim();
                    }
                    fillFields(parts);
                    setHint(item.full_name || item.address_name || item.name || 'Точка выбрана.');
                })
                .catch(function () { setHint(NOT_FOUND); });
        }

        map.on('click', function (e) {
            var lng = e.lngLat[0], lat = e.lngLat[1];
            if (marker) marker.setCoordinates([lng, lat]);
            else marker = new mapgl.Marker(map, { coordinates: [lng, lat] });
            saveCoords(lat, lng);
            lookup(lat, lng);
        });
        setHint(saved ? 'Метка сохранена. Нажмите на другое место, чтобы её передвинуть.' : 'Нажмите на карту, чтобы поставить метку.');
    }

    /* -------------------------- OpenStreetMap -------------------------- */
    function initOsm() {
        if (typeof L === 'undefined') {
            setHint('Карта не загрузилась. Заполните адрес вручную, это тоже работает.');
            return;
        }
        var start = saved || AKTAU;
        var map = L.map(mapEl, { scrollWheelZoom: false }).setView([start.lat, start.lng], saved ? 17 : 13);
        map.attributionControl.setPrefix(false);
        // Колесо с Ctrl приближает карту, без Ctrl листает страницу и показывает подсказку.
        mapEl.addEventListener('wheel', function (e) {
            if (e.ctrlKey || e.metaKey) {
                e.preventDefault();
                map.setZoom(map.getZoom() + (e.deltaY < 0 ? 1 : -1));
            } else {
                showWheelNote();
            }
        }, { passive: false });
        // Своя метка: чёрная капля с белой точкой, как в дизайне сайта.
        var pin = L.divIcon({
            className: 'map-pin',
            html: '<span></span>',
            iconSize: [34, 44],
            iconAnchor: [17, 42],
        });
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);
        var marker = null;

        function lookup(lat, lng) {
            if (!needsAddress) { setHint('Точка выбрана.'); return; }
            setHint('Определяем адрес...');
            var url = 'https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18'
                + '&lat=' + encodeURIComponent(lat) + '&lon=' + encodeURIComponent(lng) + '&accept-language=ru';
            fetch(url, { headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data || !data.address) { setHint(NOT_FOUND); return; }
                    var a = data.address;
                    fillFields({
                        city: a.city || a.town || a.village || a.municipality || '',
                        street: a.road || a.neighbourhood || a.suburb || a.quarter || '',
                        house: a.house_number || '',
                    });
                    setHint(data.display_name || 'Точка выбрана.');
                })
                .catch(function () { setHint(NOT_FOUND); });
        }

        function place(latlng, doLookup) {
            if (marker) {
                marker.setLatLng(latlng);
            } else {
                marker = L.marker(latlng, { draggable: true, icon: pin }).addTo(map);
                marker.on('dragend', function () {
                    var p = marker.getLatLng();
                    saveCoords(p.lat, p.lng);
                    lookup(p.lat, p.lng);
                });
            }
            saveCoords(latlng.lat, latlng.lng);
            if (doLookup) lookup(latlng.lat, latlng.lng);
        }

        if (saved) place(L.latLng(saved.lat, saved.lng), false);
        setHint(saved ? 'Метка сохранена. Можно передвинуть её или нажать на другое место.' : 'Нажмите на карту, чтобы поставить метку.');
        map.on('click', function (e) { place(e.latlng, true); });
        setTimeout(function () { map.invalidateSize(); }, 200);
    }

    var key = mapEl.getAttribute('data-key');
    if (mapEl.getAttribute('data-provider') === 'twogis' && key && typeof mapgl !== 'undefined') {
        initTwoGis(key);
    } else {
        initOsm();
    }
})();
