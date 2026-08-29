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
"""
from django.core.management.base import BaseCommand, CommandError
from django.core.files.base import ContentFile
from django.db import transaction
from django.utils import timezone

from catalog.importers.onec import parse_price_list, parse_stock
from catalog.models import Brand, Category, Product, ProductVariant, ProductImage


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

        stats = {'brands': 0, 'categories': 0, 'products_created': 0, 'products_updated': 0,
                  'variants_created': 0, 'variants_updated': 0, 'images_added': 0,
                  'stock_matched': 0, 'stock_unmatched': []}

        with transaction.atomic():
            if price_result:
                self._apply_price_list(price_result, stats)
            if stock_result:
                self._apply_stock(stock_result, stats)

        self.stdout.write(self.style.SUCCESS(
            'Готово. ' + ', '.join(f'{k}={v}' for k, v in stats.items() if k != 'stock_unmatched')
        ))
        if stock_result and stats['stock_unmatched']:
            self.stdout.write(self.style.WARNING(
                f"Остатки без совпадения по товару в каталоге ({len(stats['stock_unmatched'])}): "
                + ', '.join(stats['stock_unmatched'][:30])
            ))

    def _apply_price_list(self, result, stats):
        now = timezone.now()
        for parsed in result.products:
            brand, brand_created = Brand.objects.get_or_create(name=parsed.brand)
            stats['brands'] += int(brand_created)

            category, cat_created = Category.objects.get_or_create(
                brand=brand, parent=None, name=parsed.category_path,
            )
            stats['categories'] += int(cat_created)

            product, product_created = Product.objects.get_or_create(
                category=category, name=parsed.name,
                defaults={
                    'description': parsed.description,
                    'ph': parsed.ph,
                    'source_group_path': parsed.source_group_path,
                },
            )
            if not product_created:
                product.description = parsed.description or product.description
                product.ph = parsed.ph or product.ph
                product.source_group_path = parsed.source_group_path
                product.save(update_fields=['description', 'ph', 'source_group_path', 'updated_at'])
                stats['products_updated'] += 1
            else:
                stats['products_created'] += 1

            if parsed.image_bytes and not product.images.exists():
                ext = parsed.image_ext or 'jpg'
                product.images.create(
                    image=ContentFile(parsed.image_bytes, name=f'{parsed.variants[0].article}.{ext}'),
                    is_primary=True,
                )
                stats['images_added'] += 1

            for v in parsed.variants:
                if not v.article:
                    continue
                variant, variant_created = ProductVariant.objects.get_or_create(
                    article=v.article,
                    defaults={
                        'product': product,
                        'pack_label': v.pack_label,
                        'volume_value': v.volume_value,
                        'volume_unit': v.volume_unit,
                        'box_qty': v.box_qty,
                        'pallet_qty': v.pallet_qty,
                        'wholesale_price': v.wholesale_price,
                        'last_price_import_at': now,
                    },
                )
                if not variant_created:
                    variant.product = product
                    variant.pack_label = v.pack_label
                    variant.volume_value = v.volume_value
                    variant.volume_unit = v.volume_unit
                    variant.box_qty = v.box_qty
                    variant.pallet_qty = v.pallet_qty
                    variant.wholesale_price = v.wholesale_price
                    variant.last_price_import_at = now
                    variant.save()
                    stats['variants_updated'] += 1
                else:
                    stats['variants_created'] += 1

    def _apply_stock(self, result, stats):
        now = timezone.now()
        matched_articles = set()
        for variant in ProductVariant.objects.filter(article__in=result.quantities.keys()):
            variant.stock_qty = result.quantities[variant.article]
            variant.last_stock_import_at = now
            variant.save(update_fields=['stock_qty', 'last_stock_import_at'])
            matched_articles.add(variant.article)
            stats['stock_matched'] += 1
        stats['stock_unmatched'] = sorted(set(result.quantities) - matched_articles)
