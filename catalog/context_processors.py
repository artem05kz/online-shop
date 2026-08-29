from django.db.models import Prefetch

from company.models import SiteContact
from orders.cart import Cart
from .models import Brand, Category


def site_nav(request):
    """Данные, нужные на каждой странице: выпадающий каталог, счётчик
    корзины и контакты магазина для шапки и подвала."""
    brands = Brand.objects.filter(is_active=True).prefetch_related(
        Prefetch('categories', queryset=Category.objects.filter(is_active=True, parent=None))
    )
    return {
        'nav_brands': brands,
        'cart_count': len(Cart(request)),
        'site_contact': SiteContact.objects.filter(is_active=True).first(),
    }
