from django.conf import settings
from django.db import models

from catalog.models import ProductVariant


class Address(models.Model):
    """A saved delivery address. Split into separate fields (street / house /
    apartment / ...) rather than one free-text line so the courier gets the
    details in a predictable shape, and so the map picker can fill in the
    street part while the customer types the rest by hand."""

    user = models.ForeignKey(settings.AUTH_USER_MODEL, on_delete=models.CASCADE, related_name='addresses', verbose_name='Покупатель')
    label = models.CharField('Название адреса', max_length=100, blank=True, help_text='Например: Дом, Работа')

    full_name = models.CharField('Имя получателя', max_length=200)
    phone = models.CharField('Телефон', max_length=30)

    city = models.CharField('Город', max_length=100, default='Актау')
    street = models.CharField('Улица / микрорайон', max_length=200)
    house = models.CharField('Дом', max_length=30, blank=True)
    apartment = models.CharField('Квартира / офис', max_length=30, blank=True)
    entrance = models.CharField('Подъезд', max_length=30, blank=True)
    floor = models.CharField('Этаж', max_length=30, blank=True)
    intercom = models.CharField('Домофон', max_length=30, blank=True)
    comment = models.TextField('Комментарий для курьера', blank=True)

    # Filled in by the map picker on the profile page; optional, because an
    # address typed by hand is still perfectly valid.
    latitude = models.DecimalField('Широта', max_digits=9, decimal_places=6, null=True, blank=True)
    longitude = models.DecimalField('Долгота', max_digits=9, decimal_places=6, null=True, blank=True)

    is_default = models.BooleanField('Использовать по умолчанию', default=False)

    class Meta:
        ordering = ['-is_default', '-id']
        verbose_name = 'Адрес'
        verbose_name_plural = 'Адреса'

    def __str__(self):
        return self.short_text

    @property
    def street_line(self):
        """Street + house, e.g. «14 микрорайон, дом 5»."""
        parts = [self.street]
        if self.house:
            parts.append(f'дом {self.house}')
        return ', '.join(p for p in parts if p)

    @property
    def details_line(self):
        """The apartment/entrance/floor extras, skipping anything empty."""
        bits = []
        if self.apartment:
            bits.append(f'кв./офис {self.apartment}')
        if self.entrance:
            bits.append(f'подъезд {self.entrance}')
        if self.floor:
            bits.append(f'этаж {self.floor}')
        if self.intercom:
            bits.append(f'домофон {self.intercom}')
        return ', '.join(bits)

    @property
    def short_text(self):
        parts = [self.city, self.street_line]
        details = self.details_line
        if details:
            parts.append(details)
        return ', '.join(p for p in parts if p)


class Order(models.Model):
    class Status(models.TextChoices):
        NEW = 'new', 'Новый'
        CONFIRMED = 'confirmed', 'Подтверждён'
        DELIVERING = 'delivering', 'Доставляется'
        COMPLETED = 'completed', 'Выполнен'
        CANCELED = 'canceled', 'Отменён'

    # Which statuses count as "finished" — used to grey out the progress
    # tracker shown to the customer in their order history.
    STATUS_FLOW = [Status.NEW, Status.CONFIRMED, Status.DELIVERING, Status.COMPLETED]

    user = models.ForeignKey(settings.AUTH_USER_MODEL, on_delete=models.PROTECT, related_name='orders', verbose_name='Покупатель')
    status = models.CharField('Статус', max_length=20, choices=Status.choices, default=Status.NEW)

    # Snapshot of the delivery details at order time, so later edits to the
    # user's saved address book don't rewrite history.
    contact_name = models.CharField('Имя получателя', max_length=200)
    contact_phone = models.CharField('Телефон', max_length=30)
    city = models.CharField('Город', max_length=100)
    street_and_details = models.CharField('Адрес доставки', max_length=300)
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

    @property
    def total_quantity(self):
        return sum(item.quantity for item in self.items.all())

    @property
    def is_canceled(self):
        return self.status == self.Status.CANCELED

    @property
    def progress_steps(self):
        """[(label, is_done, is_current)] for the status tracker."""
        if self.is_canceled:
            return []
        try:
            current_index = self.STATUS_FLOW.index(self.status)
        except ValueError:
            current_index = 0
        steps = []
        for i, status in enumerate(self.STATUS_FLOW):
            steps.append({
                'label': self.Status(status).label,
                'is_done': i < current_index,
                'is_current': i == current_index,
            })
        return steps


class OrderItem(models.Model):
    order = models.ForeignKey(Order, on_delete=models.CASCADE, related_name='items', verbose_name='Заказ')
    variant = models.ForeignKey(ProductVariant, on_delete=models.PROTECT, related_name='order_items', verbose_name='Артикул товара')
    # Snapshot the price/name at order time — the catalog price can change later.
    product_name = models.CharField('Название товара', max_length=300)
    pack_label = models.CharField('Фасовка', max_length=50, blank=True)
    article = models.CharField('Артикул', max_length=50, blank=True)
    price = models.DecimalField('Цена', max_digits=12, decimal_places=2)
    quantity = models.PositiveIntegerField('Количество', default=1)

    class Meta:
        verbose_name = 'Позиция заказа'
        verbose_name_plural = 'Позиции заказа'

    def __str__(self):
        return f'{self.product_name} ({self.pack_label})'

    @property
    def subtotal(self):
        return self.price * self.quantity
