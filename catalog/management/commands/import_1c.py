"""Import products/prices from the 1C price-list export, and/or stock
quantities from the 1C warehouse export.

Usage:
    python manage.py import_1c --price-list "прайс лист.xlsx" --dry-run
    python manage.py import_1c --price-list "прайс лист.xlsx" --stock "остатки.xlsx"
    python manage.py import_1c --stock "остатки.xlsx"          # just refresh stock numbers

Always run with --dry-run first on a file you haven't imported before — it
parses and prints a full summary (categories/products/variants found,
warnings about rows it couldn't classify) without writing anything to the
database.

There's also a browser-based version of this for the manager at /import/
(staff login required) — see catalog/import_views.py. This command and that
page share the same parsing/apply code in catalog/importers/.
"""
from django.core.management.base import BaseCommand, CommandError

from catalog.importers.apply import EMPTY_STATS, apply_price_list, apply_stock
from catalog.importers.onec import parse_price_list, parse_stock


class Command(BaseCommand):
    help = __doc__

    def add_arguments(self, parser):
        parser.add_argument('--price-list', dest='price_list', help='Путь к файлу "прайс лист.xlsx"')
        parser.add_argument('--stock', dest='stock', help='Путь к файлу "остатки(из 1с).xlsx"')
        parser.add_argument('--dry-run', action='store_true', help='Только показать, что будет сделано, ничего не сохранять')

    def handle(self, *args, **options):
        price_list_path = options.get('price_list')
        stock_path = options.get('stock')
        dry_run = options['dry_run']

        if not price_list_path and not stock_path:
            raise CommandError('Укажите хотя бы один файл: --price-list и/или --stock')

        stock_result = None
        if stock_path:
            stock_result = parse_stock(stock_path)
            self.stdout.write(self.style.MIGRATE_HEADING(
                f'Остатки: разобрано {len(stock_result.quantities)} артикулов'
            ))
            for w in stock_result.warnings[:20]:
                self.stdout.write(self.style.WARNING(f'  ! {w}'))
            if len(stock_result.warnings) > 20:
                self.stdout.write(self.style.WARNING(f'  ... и ещё {len(stock_result.warnings) - 20} предупреждений'))

        price_result = None
        if price_list_path:
            price_result = parse_price_list(price_list_path)
            n_variants = sum(len(p.variants) for p in price_result.products)
            n_photos = sum(1 for p in price_result.products if p.image_bytes)
            self.stdout.write(self.style.MIGRATE_HEADING(
                f'Прайс-лист: разобрано {len(price_result.products)} товаров, '
                f'{n_variants} артикулов(вариантов), {n_photos} фото'
            ))
            self.stdout.write(f'  колонки: {price_result.column_map}')
            brands_seen = sorted({p.brand for p in price_result.products})
            self.stdout.write(f'  бренды: {brands_seen}')
            for w in price_result.warnings[:20]:
                self.stdout.write(self.style.WARNING(f'  ! {w}'))
            if len(price_result.warnings) > 20:
                self.stdout.write(self.style.WARNING(f'  ... и ещё {len(price_result.warnings) - 20} предупреждений'))

            self.stdout.write('  Примеры первых 5 товаров:')
            for p in price_result.products[:5]:
                arts = ', '.join(f'{v.article}({v.pack_label})' for v in p.variants)
                self.stdout.write(f'    [{p.brand}] {p.category_path} :: {p.name} -> {arts}')

        if dry_run:
            self.stdout.write(self.style.SUCCESS('Dry-run: в базу ничего не записано.'))
            return

        stats = dict(EMPTY_STATS)
        if price_result:
            apply_price_list(price_result, stats)
        if stock_result:
            apply_stock(stock_result, stats)

        self.stdout.write(self.style.SUCCESS(
            'Готово. ' + ', '.join(f'{k}={v}' for k, v in stats.items() if k != 'stock_unmatched')
        ))
        if stock_result and stats['stock_unmatched']:
            self.stdout.write(self.style.WARNING(
                f"Остатки без совпадения по товару в каталоге ({len(stats['stock_unmatched'])}): "
                + ', '.join(stats['stock_unmatched'][:30])
            ))
