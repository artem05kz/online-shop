"""Browser-based version of `python manage.py import_1c`, for the manager —
she shouldn't need a terminal to load a new price list or refresh stock.

Flow: upload the file(s) -> see a preview of what will change (no writes
yet) -> confirm -> applied for real. Uses the same parsing/apply code as the
management command (catalog/importers/).
"""
import uuid
from pathlib import Path

from django.conf import settings
from django.contrib.admin.views.decorators import staff_member_required
from django.contrib import messages
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


@staff_member_required
def import_upload(request):
    if request.method != 'POST':
        return render(request, 'catalog/import_upload.html')

    price_list_file = request.FILES.get('price_list')
    stock_file = request.FILES.get('stock')
    if not price_list_file and not stock_file:
        messages.error(request, 'Выберите хотя бы один файл: прайс-лист и/или остатки.')
        return redirect('catalog:import_upload')

    context = {}
    try:
        if price_list_file:
            price_path = _save_temp(price_list_file)
            price_result = parse_price_list(str(price_path))
            context['price_result'] = price_result
            context['price_temp_name'] = price_path.name
            context['price_variant_count'] = sum(len(p.variants) for p in price_result.products)
            context['price_brands'] = sorted({p.brand for p in price_result.products})
        if stock_file:
            stock_path = _save_temp(stock_file)
            context['stock_result'] = parse_stock(str(stock_path))
            context['stock_temp_name'] = stock_path.name
    except ValueError as exc:
        messages.error(request, f'Не смог разобрать файл: {exc}')
        return redirect('catalog:import_upload')

    return render(request, 'catalog/import_preview.html', context)


@staff_member_required
def import_confirm(request):
    if request.method != 'POST':
        return redirect('catalog:import_upload')

    price_temp_name = request.POST.get('price_temp_name')
    stock_temp_name = request.POST.get('stock_temp_name')

    stats = dict(EMPTY_STATS)
    used_paths = []

    if price_temp_name:
        price_path = TMP_DIR / price_temp_name
        if price_path.exists():
            apply_price_list(parse_price_list(str(price_path)), stats)
            used_paths.append(price_path)
    if stock_temp_name:
        stock_path = TMP_DIR / stock_temp_name
        if stock_path.exists():
            apply_stock(parse_stock(str(stock_path)), stats)
            used_paths.append(stock_path)

    for path in used_paths:
        path.unlink(missing_ok=True)

    return render(request, 'catalog/import_result.html', {'stats': stats})
