from django.db import models
from django.utils import timezone


class BannerQuerySet(models.QuerySet):
    def visible_now(self):
        now = timezone.now()
        return self.filter(is_active=True).filter(
            models.Q(starts_at__isnull=True) | models.Q(starts_at__lte=now)
        ).filter(
            models.Q(ends_at__isnull=True) | models.Q(ends_at__gte=now)
        )


class Banner(models.Model):
    """A slide in the home-page carousel. The manager uploads the picture and
    picks when it should be shown — no code changes needed for a new promo."""

    title = models.CharField('Заголовок', max_length=200, blank=True)
    image = models.ImageField('Изображение', upload_to='banners/%Y/%m/')
    link_url = models.CharField(
        'Ссылка при нажатии', max_length=300, blank=True,
        help_text='Куда вести при клике на баннер, например /catalog/grass/ (необязательно)',
    )
    order = models.PositiveIntegerField(
        'Очерёдность в слайдере', default=0,
        help_text='Определяет, в каком порядке баннеры сменяют друг друга на главной странице: '
                   'от меньшего числа к большему. Например, баннер с числом 1 покажется первым.',
    )
    is_active = models.BooleanField('Показывать на сайте', default=True)
    starts_at = models.DateTimeField('Показывать с', null=True, blank=True, help_text='Необязательно')
    ends_at = models.DateTimeField('Показывать до', null=True, blank=True, help_text='Необязательно')

    objects = BannerQuerySet.as_manager()

    class Meta:
        ordering = ['order', '-id']
        verbose_name = 'Баннер'
        verbose_name_plural = 'Баннеры'

    def __str__(self):
        return self.title or f'Баннер №{self.pk}'
