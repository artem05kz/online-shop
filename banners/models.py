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

    title = models.CharField(max_length=200, blank=True)
    image = models.ImageField(upload_to='banners/%Y/%m/')
    link_url = models.CharField(
        max_length=300, blank=True,
        help_text='Куда вести при клике на баннер, например /catalog/grass/ (необязательно)',
    )
    order = models.PositiveIntegerField(default=0)
    is_active = models.BooleanField(default=True)
    starts_at = models.DateTimeField(null=True, blank=True, help_text='Показывать с (необязательно)')
    ends_at = models.DateTimeField(null=True, blank=True, help_text='Показывать до (необязательно)')

    objects = BannerQuerySet.as_manager()

    class Meta:
        ordering = ['order', '-id']

    def __str__(self):
        return self.title or f'Banner #{self.pk}'
