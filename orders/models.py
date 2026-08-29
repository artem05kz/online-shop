from django.conf import settings
from django.db import models

from catalog.models import ProductVariant


class Address(models.Model):
    user = models.ForeignKey(settings.AUTH_USER_MODEL, on_delete=models.CASCADE, related_name='addresses', verbose_name='Покупатель')
    full_name = models.CharField('Имя получателя', max_length=200)
    phone = models.CharField('Телефон', max_length=30)
    city = models.CharField('Город', max_length=100, default='Актау')
    street_and_details = models.CharField('Улица, дом, квартира', max_length=300)
    is_default = models.BooleanField('Использовать по умолчанию', default=False)

    class Meta:
        ordering = ['-is_default', '-id']
        verbose_name = 'Адрес'
        verbose_name_plural = 'Адреса'

    def __str__(self):
        return f'{self.full_name}, {self.city}, {self.street_and_details}'


class Order(models.Model):
    class Status(models.TextChoices):
        NEW = 'new', 'Новый'
        CONFIRMED = 'confirmed', 'Подтверждён'
        DELIVERING = 'delivering', 'Доставляется'
        COMPLETED = 'completed', 'Выполнен'
        CANCELED = 'canceled', 'Отменён'

    user = models.ForeignKey(settings.AUTH_USER_MODEL, on_delete=models.PROTECT, related_name='orders', verbose_name='Покупатель')
    status = models.CharField('Статус', max_length=20, choices=Status.choices, default=Status.NEW)

    # Snapshot of the delivery details at order time, so later edits to the
    # user's saved address book don't rewrite history.
    contact_name = models.CharField('Имя получателя', max_length=200)
    contact_phone = models.CharField('Телефон', max_length=30)
    city = models.CharField('Город', max_length=100)
    street_and_details = models.CharField('Улица, дом, квартира', max_length=300)
    comment = models.TextField('Комментарий', blank=True)

    created_at = models.DateTimeField('Дата создания', auto_now_add=True)
    updated_at = models.DateTimeField('Дата изменения', auto_now=True)

    class Meta:
        ordering = ['-created_at']
        verbose_name = 'Заказ'
        verbose_name_plural = 'Заказы'

    def __str__(self):
        return f'Заказ №{self.pk} ({self.get_status_display()})'

    @property
    def total(self):
        return sum((item.price * item.quantity for item in self.items.all()), start=0)


class OrderItem(models.Model):
    order = models.ForeignKey(Order, on_delete=models.CASCADE, related_name='items', verbose_name='Заказ')
    variant = models.ForeignKey(ProductVariant, on_delete=models.PROTECT, related_name='order_items', verbose_name='Артикул товара')
    # Snapshot the price/name at order time — the catalog price can change later.
    product_name = models.CharField('Название товара', max_length=300)
    pack_label = models.CharField('Фасовка', max_length=50, blank=True)
    price = models.DecimalField('Цена', max_digits=12, decimal_places=2)
    quantity = models.PositiveIntegerField('Количество', default=1)

    class Meta:
        verbose_name = 'Позиция заказа'
        verbose_name_plural = 'Позиции заказа'

    @property
    def subtotal(self):
        return self.price * self.quantity
