"""A simple session-based cart — no login required to browse and add items,
matching how the current site works. Login is only required at checkout."""
from decimal import Decimal

from catalog.models import ProductVariant

SESSION_KEY = 'cart'


class Cart:
    def __init__(self, request):
        self.session = request.session
        cart = self.session.get(SESSION_KEY)
        if cart is None:
            cart = self.session[SESSION_KEY] = {}
        self.cart = cart

    def add(self, variant: ProductVariant, quantity: int = 1):
        key = str(variant.pk)
        self.cart[key] = self.cart.get(key, 0) + quantity
        self.save()

    def set_quantity(self, variant: ProductVariant, quantity: int):
        key = str(variant.pk)
        if quantity <= 0:
            self.cart.pop(key, None)
        else:
            self.cart[key] = quantity
        self.save()

    def remove(self, variant: ProductVariant):
        self.cart.pop(str(variant.pk), None)
        self.save()

    def clear(self):
        self.session[SESSION_KEY] = {}
        self.save()

    def save(self):
        self.session.modified = True

    def __iter__(self):
        variants = ProductVariant.objects.filter(pk__in=self.cart.keys()).select_related('product')
        for variant in variants:
            quantity = self.cart[str(variant.pk)]
            price = variant.price or Decimal('0')
            yield {
                'variant': variant,
                'quantity': quantity,
                'price': price,
                'subtotal': price * quantity,
            }

    def __len__(self):
        return sum(self.cart.values())

    @property
    def total(self):
        return sum((item['subtotal'] for item in self), start=Decimal('0'))
