from django.db.models import Prefetch

from orders.cart import Cart
from .models import Brand, Category


def site_nav(request):
    """Header data needed on every page: the catalog dropdown (brands with
    their categories) and the number of items in the cart."""
    brands = Brand.objects.filter(is_active=True).prefetch_related(
        Prefetch('categories', queryset=Category.objects.filter(is_active=True, parent=None))
    )
    return {
        'nav_brands': brands,
        'cart_count': len(Cart(request)),
    }
