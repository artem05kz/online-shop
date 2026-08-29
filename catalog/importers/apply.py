"""Applies already-parsed 1C data (see onec.py) to the database. Shared by
the `import_1c` management command and the staff-facing upload page in
catalog/import_views.py, so both stay in sync."""
from django.core.files.base import ContentFile
from django.db import transaction
from django.utils import timezone

from catalog.models import Brand, Category, Product, ProductVariant

EMPTY_STATS = {
    'brands': 0, 'categories': 0, 'products_created': 0, 'products_updated': 0,
    'variants_created': 0, 'variants_updated': 0, 'images_added': 0,
    'stock_matched': 0, 'stock_unmatched': [],
}


def apply_price_list(result, stats=None):
    stats = stats if stats is not None else dict(EMPTY_STATS)
    now = timezone.now()
    with transaction.atomic():
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
    return stats


def apply_stock(result, stats=None):
    stats = stats if stats is not None else dict(EMPTY_STATS)
    now = timezone.now()
    matched_articles = set()
    with transaction.atomic():
        for variant in ProductVariant.objects.filter(article__in=result.quantities.keys()):
            variant.stock_qty = result.quantities[variant.article]
            variant.last_stock_import_at = now
            variant.save(update_fields=['stock_qty', 'last_stock_import_at'])
            matched_articles.add(variant.article)
            stats['stock_matched'] += 1
    stats['stock_unmatched'] = sorted(set(result.quantities) - matched_articles)
    return stats
