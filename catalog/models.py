from django.db import models
from django.urls import reverse
from django.utils.text import slugify


class Brand(models.Model):
    """Top-level product line, e.g. 'Ifoam' or 'Grass'. Shown as the big
    catalog headers on the home page."""

    name = models.CharField(max_length=100, unique=True)
    slug = models.SlugField(max_length=110, unique=True, blank=True)
    order = models.PositiveIntegerField(default=0)
    is_active = models.BooleanField(default=True)

    class Meta:
        ordering = ['order', 'name']

    def __str__(self):
        return self.name

    def save(self, *args, **kwargs):
        if not self.slug:
            self.slug = slugify(self.name, allow_unicode=True)
        super().save(*args, **kwargs)


class Category(models.Model):
    """A category under a brand, e.g. 'Профессиональная автохимия'.
    Supports nesting (parent) because 1C exports its groups as a tree
    (category -> subcategory -> product)."""

    brand = models.ForeignKey(Brand, on_delete=models.CASCADE, related_name='categories')
    parent = models.ForeignKey(
        'self', on_delete=models.CASCADE, related_name='children',
        null=True, blank=True,
    )
    name = models.CharField(max_length=200)
    slug = models.SlugField(max_length=220, blank=True)
    order = models.PositiveIntegerField(default=0)
    is_active = models.BooleanField(default=True)

    class Meta:
        ordering = ['order', 'name']
        verbose_name_plural = 'categories'
        unique_together = [('brand', 'parent', 'name')]

    def __str__(self):
        return f'{self.brand.name} / {self.name}'

    def save(self, *args, **kwargs):
        if not self.slug:
            self.slug = slugify(self.name, allow_unicode=True)
        super().save(*args, **kwargs)

    def get_absolute_url(self):
        return reverse('catalog:category', kwargs={'brand_slug': self.brand.slug, 'category_slug': self.slug})


class Product(models.Model):
    """One product card as shown on the site. A product can have several
    variants (pack sizes) with their own article/price/stock — 1C treats a
    22kg canister and a 1kg bottle of the same product as separate SKUs."""

    category = models.ForeignKey(Category, on_delete=models.PROTECT, related_name='products')
    name = models.CharField(max_length=300)
    slug = models.SlugField(max_length=320, blank=True)
    description = models.TextField(blank=True)
    ph = models.CharField('pH', max_length=50, blank=True)
    is_active = models.BooleanField(default=True)

    # Traceability back to the raw 1C export, useful while tuning the importer.
    source_group_path = models.CharField(max_length=500, blank=True)

    created_at = models.DateTimeField(auto_now_add=True)
    updated_at = models.DateTimeField(auto_now=True)

    class Meta:
        ordering = ['name']

    def __str__(self):
        return self.name

    def save(self, *args, **kwargs):
        if not self.slug:
            self.slug = slugify(self.name, allow_unicode=True)[:320]
        super().save(*args, **kwargs)

    def get_absolute_url(self):
        return reverse('catalog:product', kwargs={'slug': self.slug})

    @property
    def primary_image(self):
        return self.images.filter(is_primary=True).first() or self.images.first()

    @property
    def min_price(self):
        prices = [v.price for v in self.variants.filter(is_active=True) if v.price is not None]
        return min(prices) if prices else None


class ProductVariant(models.Model):
    """One purchasable SKU: a specific article/pack size of a product."""

    product = models.ForeignKey(Product, on_delete=models.CASCADE, related_name='variants')
    article = models.CharField('Артикул', max_length=50, unique=True, db_index=True)
    pack_label = models.CharField('Фасовка', max_length=50, blank=True)  # e.g. "22 кг", "600 мл"
    volume_value = models.DecimalField(max_digits=10, decimal_places=3, null=True, blank=True)
    volume_unit = models.CharField(max_length=20, blank=True)  # кг / л / мл / шт

    box_qty = models.PositiveIntegerField('Фасовка, шт (в коробке)', null=True, blank=True)
    pallet_qty = models.PositiveIntegerField('Штук на паллете', null=True, blank=True)

    wholesale_price = models.DecimalField(max_digits=12, decimal_places=2, null=True, blank=True)
    price = models.DecimalField('Розничная цена', max_digits=12, decimal_places=2, null=True, blank=True)
    old_price = models.DecimalField('Цена до скидки', max_digits=12, decimal_places=2, null=True, blank=True)

    stock_qty = models.IntegerField('Остаток на складе', default=0)
    is_active = models.BooleanField(default=True)

    last_price_import_at = models.DateTimeField(null=True, blank=True)
    last_stock_import_at = models.DateTimeField(null=True, blank=True)

    class Meta:
        ordering = ['volume_value']

    def __str__(self):
        return f'{self.product.name} ({self.pack_label or self.article})'

    @property
    def in_stock(self):
        return self.stock_qty > 0


class ProductImage(models.Model):
    product = models.ForeignKey(Product, on_delete=models.CASCADE, related_name='images')
    image = models.ImageField(upload_to='products/%Y/%m/')
    is_primary = models.BooleanField(default=False)
    order = models.PositiveIntegerField(default=0)

    class Meta:
        ordering = ['order', 'id']

    def __str__(self):
        return f'image for {self.product.name}'
