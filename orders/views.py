from django.contrib import messages
from django.contrib.auth.decorators import login_required
from django.shortcuts import get_object_or_404, redirect, render

from catalog.models import ProductVariant
from .cart import Cart
from .forms import CheckoutForm
from .models import Order, OrderItem


def cart_detail(request):
    return render(request, 'orders/cart.html', {'cart': Cart(request)})


def cart_add(request, variant_id):
    variant = get_object_or_404(ProductVariant, pk=variant_id, is_active=True)
    quantity = 1
    if request.method == 'POST':
        try:
            quantity = max(1, int(request.POST.get('quantity', 1)))
        except (TypeError, ValueError):
            quantity = 1
    Cart(request).add(variant, quantity)
    messages.success(request, f'«{variant.product.name}» — добавлено в корзину.')
    return redirect(request.POST.get('next') or 'orders:cart')


def cart_update(request, variant_id):
    """Quantity changes from the cart page itself."""
    variant = get_object_or_404(ProductVariant, pk=variant_id)
    if request.method == 'POST':
        try:
            quantity = int(request.POST.get('quantity', 1))
        except (TypeError, ValueError):
            quantity = 1
        Cart(request).set_quantity(variant, quantity)
    return redirect('orders:cart')


def cart_remove(request, variant_id):
    variant = get_object_or_404(ProductVariant, pk=variant_id)
    Cart(request).remove(variant)
    return redirect('orders:cart')


@login_required
def checkout(request):
    cart = Cart(request)
    if len(cart) == 0:
        messages.info(request, 'Корзина пуста.')
        return redirect('catalog:home')

    default_address = request.user.addresses.filter(is_default=True).first()
    initial = {}
    if default_address:
        initial = {
            'saved_address': default_address.pk,
            'contact_name': default_address.full_name,
            'contact_phone': default_address.phone,
            'city': default_address.city,
            'street_and_details': ', '.join(
                p for p in [default_address.street_line, default_address.details_line] if p
            ),
        }
    elif request.user.first_name or request.user.last_name:
        initial['contact_name'] = f'{request.user.first_name} {request.user.last_name}'.strip()

    if request.method == 'POST':
        form = CheckoutForm(request.POST, user=request.user)
        if form.is_valid():
            order = form.save(commit=False)
            order.user = request.user
            order.save()
            for item in cart:
                OrderItem.objects.create(
                    order=order,
                    variant=item['variant'],
                    product_name=item['variant'].product.name,
                    pack_label=item['variant'].pack_label,
                    article=item['variant'].article,
                    price=item['price'],
                    quantity=item['quantity'],
                )
            cart.clear()
            messages.success(request, f'Заказ №{order.pk} оформлен. Оплата при получении.')
            return redirect('orders:order_detail', order_id=order.pk)
    else:
        form = CheckoutForm(initial=initial, user=request.user)

    return render(request, 'orders/checkout.html', {
        'form': form,
        'cart': cart,
        'addresses': request.user.addresses.all(),
    })


@login_required
def order_list(request):
    orders = request.user.orders.prefetch_related('items')
    return render(request, 'orders/order_list.html', {
        'orders': orders,
        'active_tab': 'orders',
    })


@login_required
def order_detail(request, order_id):
    order = get_object_or_404(
        Order.objects.prefetch_related('items__variant__product'),
        pk=order_id, user=request.user,
    )
    return render(request, 'orders/order_detail.html', {
        'order': order,
        'active_tab': 'orders',
    })
