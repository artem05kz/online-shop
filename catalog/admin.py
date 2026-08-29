from django.contrib import admin

from .models import Brand, Category, Product, ProductVariant, ProductImage


@admin.register(Brand)
class BrandAdmin(admin.ModelAdmin):
    list_display = ['name', 'order', 'is_active']
    list_editable = ['order', 'is_active']
    prepopulated_fields = {'slug': ('name',)}


@admin.register(Category)
class CategoryAdmin(admin.ModelAdmin):
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
class ProductAdmin(admin.ModelAdmin):
    list_display = ['name', 'category', 'is_active', 'updated_at']
    list_filter = ['category__brand', 'category', 'is_active']
    search_fields = ['name', 'variants__article']
    prepopulated_fields = {'slug': ('name',)}
    inlines = [ProductVariantInline, ProductImageInline]


@admin.register(ProductVariant)
class ProductVariantAdmin(admin.ModelAdmin):
    """A flat, fast-editing list of every SKU — this is the screen the
    manager will actually live in day-to-day: search by article, fix a
    price/stock number, done. No hunting through product forms."""

    list_display = ['article', 'product', 'pack_label', 'price', 'stock_qty', 'is_active']
    list_editable = ['price', 'stock_qty', 'is_active']
    search_fields = ['article', 'product__name']
    list_filter = ['product__category__brand', 'is_active']
    list_select_related = ['product']
