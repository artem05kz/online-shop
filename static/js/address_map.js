// Выбор адреса на карте. Используем Leaflet и открытые карты
// OpenStreetMap — им не нужен платный ключ. Определение улицы по точке
// делает бесплатный сервис Nominatim; если он недоступен, форма всё равно
// работает — улицу можно вписать руками.
(function () {
    'use strict';

    var mapEl = document.getElementById('map');
    if (!mapEl || typeof L === 'undefined') return;

    var latInput = document.getElementById('id_latitude');
    var lngInput = document.getElementById('id_longitude');
    var cityInput = document.getElementById('id_city');
    var streetInput = document.getElementById('id_street');
    var houseInput = document.getElementById('id_house');
    var hint = document.getElementById('map-hint');

    var AKTAU = [43.6525, 51.1575]; // центр Актау — стартовая точка
    var saved = (latInput && latInput.value && lngInput && lngInput.value)
        ? [parseFloat(latInput.value), parseFloat(lngInput.value)]
        : null;

    var map = L.map('map').setView(saved || AKTAU, saved ? 17 : 13);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap'
    }).addTo(map);

    var marker = null;

    function setHint(text) {
        if (hint) hint.textContent = text;
    }

    function lookupAddress(lat, lng) {
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
                var city = a.city || a.town || a.village || a.municipality || '';
                var street = a.road || a.neighbourhood || a.suburb || a.quarter || '';
                // Не затираем то, что человек уже вписал сам.
                if (city && cityInput && !cityInput.value.trim()) cityInput.value = city;
                if (street && streetInput && !streetInput.value.trim()) streetInput.value = street;
                if (a.house_number && houseInput && !houseInput.value.trim()) houseInput.value = a.house_number;
                setHint(data.display_name || 'Точка выбрана.');
            })
            .catch(function () {
                setHint('Точка выбрана. Адрес не определился — впишите улицу вручную.');
            });
    }

    function placeMarker(latlng, lookup) {
        if (marker) {
            marker.setLatLng(latlng);
        } else {
            marker = L.marker(latlng, { draggable: true }).addTo(map);
            marker.on('dragend', function () {
                var p = marker.getLatLng();
                saveCoords(p);
                lookupAddress(p.lat, p.lng);
            });
        }
        saveCoords(latlng);
        if (lookup) lookupAddress(latlng.lat, latlng.lng);
    }

    function saveCoords(latlng) {
        if (latInput) latInput.value = latlng.lat.toFixed(6);
        if (lngInput) lngInput.value = latlng.lng.toFixed(6);
    }

    if (saved) {
        placeMarker(L.latLng(saved[0], saved[1]), false);
        setHint('Метка сохранена. Можно передвинуть её при необходимости.');
    }

    map.on('click', function (e) { placeMarker(e.latlng, true); });

    // Карта внутри вкладки может открыться со сбитым размером — поправим.
    setTimeout(function () { map.invalidateSize(); }, 200);
})();
