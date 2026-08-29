from django.db import models


class Store(models.Model):
    """Физический магазин или пункт самовывоза. Менеджер добавляет их сам,
    без правки кода."""

    class Kind(models.TextChoices):
        PICKUP = 'pickup', 'Пункт самовывоза'
        SHOP = 'shop', 'Магазин'
        WAREHOUSE = 'warehouse', 'Склад'

    city = models.CharField('Город', max_length=100, default='Актау')
    address = models.CharField(
        'Адрес', max_length=300,
        help_text='Как показывать покупателю целиком. Например: '
                  'г. Актау, 21 микрорайон, рынок «Алем», бутик №74',
    )
    kind = models.CharField('Тип точки', max_length=20, choices=Kind.choices, default=Kind.PICKUP)

    working_hours = models.CharField(
        'Часы работы', max_length=200, blank=True,
        help_text='Например: Пн–Сб, 9:00–18:00',
    )
    lunch_hours = models.CharField(
        'Обед', max_length=100, blank=True,
        help_text='Например: с 13:00 до 14:00. Оставьте пустым, если обеда нет.',
    )
    phone = models.CharField('Телефон', max_length=50, blank=True)

    latitude = models.DecimalField('Широта', max_digits=9, decimal_places=6, null=True, blank=True)
    longitude = models.DecimalField('Долгота', max_digits=9, decimal_places=6, null=True, blank=True)
    map_url = models.URLField(
        'Ссылка «как добраться»', max_length=500, blank=True,
        help_text='Необязательно. Ссылка на точку в картах — кнопка «Как добраться».',
    )

    order = models.PositiveIntegerField(
        'Очерёдность в списке', default=0,
        help_text='Точки выводятся по этому числу от меньшего к большему.',
    )
    is_active = models.BooleanField('Показывать на сайте', default=True)

    class Meta:
        ordering = ['order', 'city', 'id']
        verbose_name = 'Адрес магазина'
        verbose_name_plural = 'Адреса магазинов'

    def __str__(self):
        return self.address or f'Точка №{self.pk}'

    @property
    def hours_line(self):
        """Одна строка про режим работы — для карточки на сайте."""
        parts = []
        if self.working_hours:
            parts.append(self.working_hours)
        if self.lunch_hours:
            parts.append(f'обед {self.lunch_hours}')
        return ' | '.join(parts)


class SiteContact(models.Model):
    """Контакты магазина: показываются в шапке и подвале сайта.

    Запись используется одна — та, у которой стоит галочка «Использовать
    на сайте»; остальные можно держать как черновики.
    """

    title = models.CharField(
        'Название набора', max_length=100, default='Основные контакты',
        help_text='Только для вас, на сайте не показывается.',
    )

    phone = models.CharField('Телефон', max_length=50, blank=True)
    phone_extra = models.CharField('Дополнительный телефон', max_length=50, blank=True)
    email = models.EmailField('Электронная почта', blank=True)

    whatsapp = models.CharField(
        'WhatsApp', max_length=200, blank=True,
        help_text='Номер или ссылка. Например: +7 701 245-48-48',
    )
    telegram = models.CharField(
        'Telegram', max_length=200, blank=True,
        help_text='Имя пользователя или ссылка. Например: @grass_aktau',
    )
    instagram = models.CharField(
        'Instagram', max_length=200, blank=True,
        help_text='Имя пользователя или ссылка. Например: grass_aktau',
    )

    city = models.CharField('Город', max_length=100, default='Актау')
    working_hours = models.CharField('Часы работы', max_length=200, blank=True, default='Пн–Сб, 9:00–18:00')

    is_active = models.BooleanField(
        'Использовать на сайте', default=True,
        help_text='Показывается тот набор, у которого стоит эта галочка.',
    )

    class Meta:
        ordering = ['-is_active', 'id']
        verbose_name = 'Контакты сайта'
        verbose_name_plural = 'Контакты сайта'

    def __str__(self):
        return self.title

    # --- Ссылки, приведённые к рабочему виду ------------------------------
    @staticmethod
    def _digits(value):
        return ''.join(ch for ch in value if ch.isdigit())

    @property
    def phone_link(self):
        return f'+{self._digits(self.phone)}' if self.phone else ''

    @property
    def whatsapp_link(self):
        if not self.whatsapp:
            return ''
        if self.whatsapp.startswith('http'):
            return self.whatsapp
        digits = self._digits(self.whatsapp)
        return f'https://wa.me/{digits}' if digits else ''

    @property
    def telegram_link(self):
        if not self.telegram:
            return ''
        if self.telegram.startswith('http'):
            return self.telegram
        return f'https://t.me/{self.telegram.lstrip("@")}'

    @property
    def instagram_link(self):
        if not self.instagram:
            return ''
        if self.instagram.startswith('http'):
            return self.instagram
        return f'https://instagram.com/{self.instagram.lstrip("@")}'
