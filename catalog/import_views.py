"""Browser-based version of `python manage.py import_1c`, wired into Django
admin at /admin/catalog/import/ — the manager already lives in /admin/ to
fix prices, so the import lives right there too instead of a separate page.

Flow: upload the file(s) -> tick which rows to actually load (search/sort
client-side to find what you want, everything is checked by default) ->
confirm -> applied for real. Uses the same parsing/apply code as the
management command (catalog/importers/).
"""
import uuid
from pathlib import Path

from django.conf import settings
from django.contrib import admin, messages
from django.shortcuts import redirect, render

from catalog.importers.apply import EMPTY_STATS, apply_price_list, apply_stock
from catalog.importers.onec import parse_price_list, parse_stock

TMP_DIR = Path(settings.MEDIA_ROOT) / 'imports' / 'tmp'


def _save_temp(uploaded_file) -> Path:
    TMP_DIR.mkdir(parents=True, exist_ok=True)
    ext = Path(uploaded_file.name).suffix or '.xlsx'
    path = TMP_DIR / f'{uuid.uuid4().hex}{ext}'
    with open(path, 'wb') as out:
        for chunk in uploaded_file.chunks():
            out.write(chunk)
    return path


def _product_key(parsed_product) -> str:
    """A stable id for a parsed product to use as a checkbox value — the
    article of its first pack size (every parsed product has at least one
    variant, that's how the parser recognises a product-master row)."""
    return parsed_product.variants[0].article


def import_stock_upload(request):
    """A shortcut entry point for the most common, lowest-risk action:
    refresh stock quantities on products that already exist, without
    touching names/prices/photos. Same processing as import_upload() below
    (its POST handling doesn't care which page linked to it) — this view
    just serves a GET page with only the "Остатки" field, no price-list
    input to avoid confusing it with a full catalog reload."""
    context = admin.site.each_context(request)
    context['title'] = 'Обновить остаток на складе'
    return render(request, 'admin/catalog/import_stock_upload.html', context)


def import_upload(request):
    """Step 1: pick the file(s). admin.site.admin_view() (applied in
    catalog/admin.py where this is wired into the urls) already enforces
    staff-only access, CSRF and no-cache — this view can stay plain."""
    context = admin.site.each_context(request)
    context['title'] = 'Загрузка товаров из 1С'

    if request.method != 'POST':
        return render(request, 'admin/catalog/import_upload.html', context)

    price_list_file = request.FILES.get('price_list')
    stock_file = request.FILES.get('stock')
    if not price_list_file and not stock_file:
        messages.error(request, 'Выберите хотя бы один файл: прайс-лист и/или остатки.')
        return redirect('admin:catalog_import_upload')

    try:
        if price_list_file:
            price_path = _save_temp(price_list_file)
            price_result = parse_price_list(str(price_path))
            context['price_result'] = price_result
            context['price_temp_name'] = price_path.name
            context['price_variant_count'] = sum(len(p.variants) for p in price_result.products)
            context['price_brands'] = sorted({p.brand for p in price_result.products})
            context['price_rows'] = [{'key': _product_key(p), 'product': p} for p in price_result.products]
        if stock_file:
            stock_path = _save_temp(stock_file)
            stock_result = parse_stock(str(stock_path))
            context['stock_result'] = stock_result
            context['stock_temp_name'] = stock_path.name
            context['stock_rows'] = sorted(stock_result.quantities.items())
    except ValueError as exc:
        messages.error(request, f'Не смог разобрать файл: {exc}')
        return redirect('admin:catalog_import_upload')

    return render(request, 'admin/catalog/import_preview.html', context)


def import_confirm(request):
    """Step 2: only the rows whose checkbox was still ticked get applied —
    unticked rows are simply absent from the POST data (normal checkbox
    behaviour), so we filter the freshly re-parsed file down to those.

    Also applies any edits made on the preview page: name/category/brand
    per product, quantity per stock row. The preview names those inputs
    `<field>_<key>`, so we look them up by the same key we used to build
    the checkboxes and overwrite the freshly-parsed value before saving."""
    if request.method != 'POST':
        return redirect('admin:catalog_import_upload')

    price_temp_name = request.POST.get('price_temp_name')
    stock_temp_name = request.POST.get('stock_temp_name')
    selected_products = set(request.POST.getlist('selected_products'))
    selected_stock = set(request.POST.getlist('selected_stock'))

    stats = dict(EMPTY_STATS)
    used_paths = []

    if price_temp_name:
        price_path = TMP_DIR / price_temp_name
        if price_path.exists():
            result = parse_price_list(str(price_path))
            kept = []
            for p in result.products:
                key = _product_key(p)
                if key not in selected_products:
                    continue
                p.brand = request.POST.get(f'brand_{key}', p.brand).strip() or p.brand
                p.category_path = request.POST.get(f'category_{key}', p.category_path).strip() or p.category_path
                p.name = request.POST.get(f'name_{key}', p.name).strip() or p.name
                kept.append(p)
            result.products = kept
            apply_price_list(result, stats)
            used_paths.append(price_path)
    if stock_temp_name:
        stock_path = TMP_DIR / stock_temp_name
        if stock_path.exists():
            result = parse_stock(str(stock_path))
            quantities = {}
            for article in result.quantities:
                if article not in selected_stock:
                    continue
                raw = request.POST.get(f'qty_{article}')
                try:
                    quantities[article] = int(raw) if raw is not None else result.quantities[article]
                except ValueError:
                    quantities[article] = result.quantities[article]
            result.quantities = quantities
            apply_stock(result, stats)
            used_paths.append(stock_path)

    for path in used_paths:
        path.unlink(missing_ok=True)

    context = admin.site.each_context(request)
    context['title'] = 'Загрузка завершена'
    context['stats'] = stats
    return render(request, 'admin/catalog/import_result.html', context)
