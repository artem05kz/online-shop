// Выбор адреса доставки на карте.
//
// Работает в двух режимах:
//   * если в настройках задан ключ Google Maps — используется карта Google;
//   * если ключа нет — открытые карты OpenStreetMap (бесплатно, без ключа).
//
// В обоих случаях по клику или перетаскиванию метки поля «Город», «Улица»
// и «Дом» заполняются заново — клик по карте это осознанное действие, и
// подставленное раньше значение должно обновиться.
(function () {
    'use strict';

    var mapEl = document.getElementById('map');
    if (!mapEl) return;

    var latInput = document.getElementById('id_latitude');
    var lngInput = document.getElementById('id_longitude');
    var cityInput = document.getElementById('id_city');
    var streetInput = document.getElementById('id_street');
    var houseInput = document.getElementById('id_house');
    var hint = document.getElementById('map-hint');

    var AKTAU = { lat: 43.6525, lng: 51.1575 };
    var saved = (latInput && latInput.value && lngInput && lngInput.value)
        ? { lat: parseFloat(latInput.value), lng: parseFloat(lngInput.value) }
        : null;

    function setHint(text) {
        if (hint) hint.textContent = text;
    }

    function saveCoords(lat, lng) {
        if (latInput) latInput.value = Number(lat).toFixed(6);
        if (lngInput) lngInput.value = Number(lng).toFixed(6);
    }

    // Подставляем то, что удалось определить. Пустые куски не затираем —
    // например, если номер дома карта не знает, оставляем введённый вручную.
    function fillFields(parts) {
        if (parts.city && cityInput) cityInput.value = parts.city;
        if (parts.street && streetInput) streetInput.value = parts.street;
        if (parts.house && houseInput) houseInput.value = parts.house;
    }

    /* --------------------------- Google Maps --------------------------- */
    function initGoogle() {
        var start = saved || AKTAU;
        var map = new google.maps.Map(mapEl, {
            center: start,
            zoom: saved ? 17 : 13,
            mapTypeControl: false,
            streetViewControl: false,
        });
        var geocoder = new google.maps.Geocoder();
        var marker = new google.maps.Marker({
            map: map,
            draggable: true,
            position: saved || null,
        });

        function lookup(latLng) {
            setHint('Определяем адрес…');
            geocoder.geocode({ location: latLng, language: 'ru' }, function (results, status) {
                if (status !== 'OK' || !results || !results.length) {
                    setHint('Точка выбрана. Адрес не определился — впишите улицу вручную.');
                    return;
                }
                var parts = { city: '', street: '', house: '' };
                results[0].address_components.forEach(function (c) {
                    if (c.types.indexOf('locality') > -1) parts.city = c.long_name;
                    if (!parts.city && c.types.indexOf('administrative_area_level_2') > -1) parts.city = c.long_name;
                    if (c.types.indexOf('route') > -1 || c.types.indexOf('neighborhood') > -1) {
                        if (!parts.street) parts.street = c.long_name;
                    }
                    if (c.types.indexOf('street_number') > -1) parts.house = c.long_name;
                });
                fillFields(parts);
                setHint(results[0].formatted_address);
            });
        }

        function place(latLng, doLookup) {
            marker.setPosition(latLng);
            saveCoords(latLng.lat(), latLng.lng());
            if (doLookup) lookup(latLng);
        }

        map.addListener('click', function (e) { place(e.latLng, true); });
        marker.addListener('dragend', function () { place(marker.getPosition(), true); });

        if (saved) {
            setHint('Метка сохранена. Можно передвинуть её или нажать на другое место.');
        } else {
            setHint('Нажмите на карту, чтобы поставить метку.');
        }
    }

    /* ------------------------- OpenStreetMap --------------------------- */
    function initLeaflet() {
        if (typeof L === 'undefined') {
            setHint('Карта не загрузилась. Заполните адрес вручную — это тоже работает.');
            return;
        }
        var start = saved || AKTAU;
        var map = L.map('map').setView([start.lat, start.lng], saved ? 17 : 13);
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap'
        }).addTo(map);

        var marker = null;

        function lookup(lat, lng) {
            setHint('Определяем адрес…');
            var url = 'https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=18'
                + '&lat=' + encodeURIComponent(lat) + '&lon=' + encodeURIComponent(lng)
                + '&accept-language=ru';
            fetch(url, { headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data || !data.address) {
                        setHint('Точка выбрана. Адрес не определился — впишите улицу вручную.');
                        return;
                    }
                    var a = data.address;
                    fillFields({
                        city: a.city || a.town || a.village || a.municipality || '',
                        street: a.road || a.neighbourhood || a.suburb || a.quarter || '',
                        house: a.house_number || '',
                    });
                    setHint(data.display_name || 'Точка выбрана.');
                })
                .catch(function () {
                    setHint('Точка выбрана. Адрес не определился — впишите улицу вручную.');
                });
        }

        function place(latlng, doLookup) {
            if (marker) {
                marker.setLatLng(latlng);
            } else {
                marker = L.marker(latlng, { draggable: true }).addTo(map);
                marker.on('dragend', function () {
                    var p = marker.getLatLng();
                    saveCoords(p.lat, p.lng);
                    lookup(p.lat, p.lng);
                });
            }
            saveCoords(latlng.lat, latlng.lng);
            if (doLookup) lookup(latlng.lat, latlng.lng);
        }

        if (saved) {
            place(L.latLng(saved.lat, saved.lng), false);
            setHint('Метка сохранена. Можно передвинуть её или нажать на другое место.');
        } else {
            setHint('Нажмите на карту, чтобы поставить метку.');
        }

        map.on('click', function (e) { place(e.latlng, true); });
        setTimeout(function () { map.invalidateSize(); }, 200);
    }

    // Какой режим включить, решает шаблон: он выставляет window.MAP_PROVIDER.
    if (window.MAP_PROVIDER === 'google' && typeof google !== 'undefined' && google.maps) {
        initGoogle();
    } else {
        initLeaflet();
    }
})();
