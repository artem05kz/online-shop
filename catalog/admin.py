from django.contrib import admin
from django.urls import path

from . import import_views
from .admin_style import StyledAdminMixin
from .models import Brand, Category, Product, ProductVariant, ProductImage

# Own name for the admin panel instead of the default "Django site admin" —
# this is the screen the manager works in every day.
admin.site.site_header = 'Управление магазином GRASS'
admin.site.site_title = 'Управление магазином GRASS'
admin.site.index_title = 'Главная страница'


@admin.register(Brand)
class BrandAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['name', 'order', 'is_active']
    list_editable = ['order', 'is_active']
    prepopulated_fields = {'slug': ('name',)}


@admin.register(Category)
class CategoryAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['name', 'brand', 'parent', 'order', 'is_active']
    list_filter = ['brand', 'is_active']
    search_fields = ['name']
    prepopulated_fields = {'slug': ('name',)}


class ProductVariantInline(admin.TabularInline):
    model = ProductVariant
    extra = 0
    fields = ['article', 'pack_label', 'wholesale_price', 'price', 'old_price', 'stock_qty', 'is_active']


class ProductImageInline(admin.TabularInline):
    model = ProductImage
    extra = 1
    fields = ['image', 'is_primary', 'order']


@admin.register(Product)
class ProductAdmin(StyledAdminMixin, admin.ModelAdmin):
    # Sortable (click a column header) and searchable out of the box —
    # Django admin does this automatically for real model fields.
    list_display = ['name', 'brand', 'category', 'variant_count', 'is_active', 'updated_at']
    list_filter = ['category__brand', 'category', 'is_active']
    search_fields = ['name', 'description', 'variants__article']
    ordering = ['name']
    prepopulated_fields = {'slug': ('name',)}
    inlines = [ProductVariantInline, ProductImageInline]

    # Grouped so the fields a manager actually edits after an import
    # (name/description/pH) are front and center; import bookkeeping is
    # kept but pushed down and marked read-only.
    fieldsets = [
        ('Основное', {'fields': ['name', 'slug', 'category', 'is_active']}),
        ('Описание', {'fields': ['description', 'ph']}),
        ('Импорт из 1С (справочно)', {
            'fields': ['source_group_path', 'created_at', 'updated_at'],
            'classes': ['collapse'],
        }),
    ]
    readonly_fields = ['source_group_path', 'created_at', 'updated_at']

    @admin.display(description='Бренд', ordering='category__brand')
    def brand(self, obj):
        return obj.category.brand

    @admin.display(description='Количество фасовок')
    def variant_count(self, obj):
        return obj.variants.count()


@admin.register(ProductVariant)
class ProductVariantAdmin(StyledAdminMixin, admin.ModelAdmin):
    """A flat, fast-editing list of every SKU — this is the screen the
    manager will actually live in day-to-day: search by article, fix a
    price/stock number, done. No hunting through product forms."""

    list_display = ['article', 'product', 'pack_label', 'price', 'stock_qty', 'is_active']
    list_editable = ['price', 'stock_qty', 'is_active']
    search_fields = ['article', 'product__name']
    list_filter = ['product__category__brand', 'is_active']
    ordering = ['product__name', 'volume_value']
    list_select_related = ['product']


# --- 1C import, wired into /admin/ instead of a separate public page -----
# admin.site.admin_view() gives us staff-only access + CSRF + no-cache for
# free, same as every built-in admin page.
_default_get_urls = admin.site.get_urls


def _get_urls_with_import():
    custom_urls = [
        path('catalog/import/', admin.site.admin_view(import_views.import_upload), name='catalog_import_upload'),
        path('catalog/import/stock/', admin.site.admin_view(import_views.import_stock_upload), name='catalog_import_stock_upload'),
        path('catalog/import/confirm/', admin.site.admin_view(import_views.import_confirm), name='catalog_import_confirm'),
    ]
    return custom_urls + _default_get_urls()


admin.site.get_urls = _get_urls_with_import
