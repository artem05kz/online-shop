"""Parsing for the two Excel reports the manager pulls out of 1C:

* "прайс лист" — the product catalog: name, photo (embedded image),
  description, pH, article, pack size, box/pallet quantity, wholesale price.
* "остатки" — a warehouse stock ledger: article -> quantity in stock.

Both are 1C's standard "report to Excel" export: a flat sheet where category
groups appear as their own header rows (only a name, no other data) mixed in
with the actual data rows. There is no clean table to just pandas.read_excel()
— we have to walk the rows and tell headers from data ourselves.

This module only *parses* the files into plain Python data structures. It
never touches the database — that keeps it testable and lets the management
command show a dry-run preview before writing anything. See
catalog/management/commands/import_1c.py for the part that applies the
result to the models.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from decimal import Decimal, InvalidOperation

import openpyxl

# Row/column layout of the price-list export. These are positions, not just
# labels, because the same header text is split across two header rows
# (a merged parent header + a sub-header row with the real column names).
# If a new export ever shifts columns around, re-run with --dry-run first —
# the printed column mapping makes it obvious something is off.
BRAND_KEYWORDS = ['ifoam', 'grass']


def normalize_article(value) -> str:
    """1C sometimes pads articles with non-breaking spaces, and Excel stores
    numeric-looking articles as numbers (131122 instead of "131122"). Bring
    both sides of the join key to the same plain string form."""
    if value is None:
        return ''
    if isinstance(value, float) and value.is_integer():
        value = int(value)
    text = str(value).replace('\xa0', ' ').strip()
    return text


def parse_pack_label(raw) -> tuple[Decimal | None, str]:
    """"22 кг" -> (Decimal(22), "кг"); "5,5 кг" -> (Decimal('5.5'), "кг")."""
    if raw is None:
        return None, ''
    text = str(raw).replace('\xa0', ' ').strip()
    match = re.match(r'^([\d]+(?:[.,]\d+)?)\s*(.*)$', text)
    if not match:
        return None, text
    number, unit = match.groups()
    try:
        value = Decimal(number.replace(',', '.'))
    except InvalidOperation:
        value = None
    return value, unit.strip()


def guess_brand_and_category(raw_header_text: str) -> tuple[str, str]:
    """Category header rows look like ' Автошампуни для ... ifoam' — the
    brand name is tacked onto the end of the group name rather than living
    in its own column. Pull it out; if we can't recognise it, flag it
    clearly instead of silently misfiling the products."""
    text = raw_header_text.strip()
    lowered = text.lower()
    for brand in BRAND_KEYWORDS:
        if lowered.endswith(brand):
            clean_name = re.sub(rf'\s*{brand}\s*$', '', text, flags=re.I).strip()
            return brand.capitalize(), clean_name or text
    return 'Без бренда (уточнить)', text


def _cell(row, idx):
    return row[idx] if idx is not None and idx < len(row) else None


def _text(row, idx) -> str:
    value = _cell(row, idx)
    return '' if value is None else str(value).replace('\xa0', ' ').strip()


@dataclass
class ParsedVariant:
    article: str
    pack_label: str
    volume_value: Decimal | None
    volume_unit: str
    box_qty: int | None
    pallet_qty: int | None
    wholesale_price: Decimal | None


@dataclass
class ParsedProduct:
    name: str
    brand: str
    category_path: str  # cleaned category name, for display
    source_group_path: str  # raw text from the 1C export, for debugging
    description: str
    ph: str
    variants: list[ParsedVariant] = field(default_factory=list)
    image_bytes: bytes | None = None
    image_ext: str = ''


@dataclass
class PriceListParseResult:
    products: list[ParsedProduct]
    warnings: list[str]
    column_map: dict


def _find_header_row(ws, required_headers: list[str], search_rows: int = 10) -> int | None:
    """Return the 0-indexed row that contains all of `required_headers`
    (case-insensitive substring match), or None if not found in the first
    `search_rows` rows."""
    for row_idx, row in enumerate(ws.iter_rows(min_row=1, max_row=search_rows, values_only=True)):
        cells = [str(c).strip().lower() for c in row if c is not None]
        if all(any(h.lower() in c for c in cells) for h in required_headers):
            return row_idx
    return None


def parse_price_list(path: str) -> PriceListParseResult:
    wb = openpyxl.load_workbook(path, data_only=True)
    ws = wb.worksheets[0]
    warnings: list[str] = []

    header_row_idx = _find_header_row(ws, ['артикул', 'объем'])
    if header_row_idx is None:
        header_row_idx = _find_header_row(ws, ['артикул'])
    if header_row_idx is None:
        raise ValueError('Не нашёл строку заголовков (со словом "Артикул") в первых 10 строках прайс-листа.')

    all_rows = list(ws.iter_rows(min_row=1, values_only=True))
    header = list(all_rows[header_row_idx])
    sub_header = list(all_rows[header_row_idx + 1]) if header_row_idx + 1 < len(all_rows) else []

    def find_col(label, in_row=None):
        row_to_search = in_row if in_row is not None else header
        for i, v in enumerate(row_to_search):
            if v and label.lower() in str(v).strip().lower():
                return i
        return None

    col = {
        'name': find_col('название'),
        'description': find_col('описание'),
        'ph': find_col('ph'),
        'article': find_col('артикул'),
        'pack_label': find_col('объем') or find_col('объём'),
        'box_qty': find_col('в коробке', in_row=sub_header),
    }
    if col['box_qty'] is None:
        # Some exports don't repeat the sub-header — fall back to "right
        # after Объем" which matches every export seen so far.
        col['box_qty'] = (col['pack_label'] or 0) + 1
    col['pallet_qty'] = col['box_qty'] + 1
    col['price'] = col['box_qty'] + 2

    for required in ('name', 'article'):
        if col[required] is None:
            raise ValueError(f'Не нашёл колонку "{required}" в прайс-листе — проверьте формат файла.')

    # Map embedded photos by (row, col) so we can attach the right picture
    # to the right product. Only the "Фото" column counts — row 0/col 0 is
    # usually the company logo in the corner, not a product photo.
    photos: dict[int, tuple[bytes, str]] = {}
    photo_col = find_col('фото') or 1
    for image in getattr(ws, '_images', []):
        try:
            r, c = image.anchor._from.row, image.anchor._from.col
        except AttributeError:
            continue
        if c == photo_col:
            photos[r] = (image.ref.getvalue(), image.format or 'png')

    products: list[ParsedProduct] = []
    current_brand = 'Без бренда (уточнить)'
    current_category = ''
    current_category_raw = ''
    current_product: ParsedProduct | None = None

    data_rows = all_rows[header_row_idx + 2:]  # skip header + sub-header rows
    for offset, row in enumerate(data_rows):
        row_idx = header_row_idx + 2 + offset
        name = _text(row, col['name'])
        article = normalize_article(_cell(row, col['article']))

        if not name and not article:
            continue  # fully blank spacer row

        if name and not article:
            # Category header row, e.g. " Автошампуни ... ifoam".
            current_brand, current_category = guess_brand_and_category(name)
            current_category_raw = name
            current_product = None
            continue

        wholesale_price = _cell(row, col['price'])
        if isinstance(wholesale_price, str):
            wholesale_price = None
        box_qty = _cell(row, col['box_qty'])
        pallet_qty = _cell(row, col['pallet_qty'])
        volume_value, volume_unit = parse_pack_label(_cell(row, col['pack_label']))

        variant = ParsedVariant(
            article=article,
            pack_label=_text(row, col['pack_label']),
            volume_value=volume_value,
            volume_unit=volume_unit,
            box_qty=int(box_qty) if isinstance(box_qty, (int, float)) else None,
            pallet_qty=int(pallet_qty) if isinstance(pallet_qty, (int, float)) else None,
            wholesale_price=Decimal(str(wholesale_price)) if isinstance(wholesale_price, (int, float)) else None,
        )

        if name:
            # New product master row — first variant plus name/photo/description.
            image_bytes, image_ext = None, ''
            if row_idx in photos:
                image_bytes, image_ext = photos[row_idx]
            if not current_category:
                warnings.append(f'Товар "{name}" (арт. {article}) идёт без заголовка категории выше — проверьте файл.')
            current_product = ParsedProduct(
                name=name,
                brand=current_brand,
                category_path=current_category or 'Без категории',
                source_group_path=current_category_raw,
                description=_text(row, col['description']),
                ph=_text(row, col['ph']),
                image_bytes=image_bytes,
                image_ext=image_ext,
            )
            current_product.variants.append(variant)
            products.append(current_product)
        else:
            # Continuation row: another pack size of the current product.
            if current_product is None:
                warnings.append(f'Строка с артикулом {article} без названия и без товара выше — пропустил.')
                continue
            current_product.variants.append(variant)

    return PriceListParseResult(products=products, warnings=warnings, column_map=col)


@dataclass
class StockParseResult:
    quantities: dict[str, int]  # article -> qty
    warnings: list[str]


def parse_stock(path: str) -> StockParseResult:
    wb = openpyxl.load_workbook(path, data_only=True)
    ws = wb.worksheets[0]
    warnings: list[str] = []

    header_row_idx = _find_header_row(ws, ['артикул', 'количество'])
    if header_row_idx is None:
        raise ValueError('Не нашёл строку заголовков (со словами "Артикул"/"Количество") в файле остатков.')

    all_rows = list(ws.iter_rows(min_row=1, values_only=True))
    header = list(all_rows[header_row_idx])

    def find_col(label):
        for i, v in enumerate(header):
            if v and label.lower() in str(v).strip().lower():
                return i
        return None

    article_col = find_col('артикул')
    qty_col = find_col('количество')
    if article_col is None or qty_col is None:
        raise ValueError('Не нашёл колонки "Артикул"/"Количество" в файле остатков.')

    quantities: dict[str, int] = {}
    for row in all_rows[header_row_idx + 1:]:
        article = normalize_article(_cell(row, article_col))
        qty = _cell(row, qty_col)
        if not article:
            continue  # group header / org name row, no article to match on
        if not isinstance(qty, (int, float)):
            warnings.append(f'Артикул {article}: остаток не число ({qty!r}), поставил 0.')
            qty = 0
        if article in quantities:
            warnings.append(f'Артикул {article} встретился в остатках дважды — суммирую.')
            quantities[article] += int(qty)
        else:
            quantities[article] = int(qty)

    return StockParseResult(quantities=quantities, warnings=warnings)
