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
    quantity = int(request.POST.get('quantity', 1)) if request.method == 'POST' else 1
    Cart(request).add(variant, quantity)
    messages.success(request, f'«{variant.product.name}» добавлен в корзину.')
    return redirect(request.POST.get('next') or 'orders:cart')


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
            'contact_name': default_address.full_name,
            'contact_phone': default_address.phone,
            'city': default_address.city,
            'street_and_details': default_address.street_and_details,
        }

    if request.method == 'POST':
        form = CheckoutForm(request.POST, initial=initial)
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
                    price=item['price'],
                    quantity=item['quantity'],
                )
            cart.clear()
            messages.success(request, f'Заказ №{order.pk} оформлен! Оплата — наличными/картой курьеру при получении.')
            return redirect('orders:order_success', order_id=order.pk)
    else:
        form = CheckoutForm(initial=initial)

    return render(request, 'orders/checkout.html', {'form': form, 'cart': cart})


@login_required
def order_success(request, order_id):
    order = get_object_or_404(Order, pk=order_id, user=request.user)
    return render(request, 'orders/order_success.html', {'order': order})


@login_required
def order_list(request):
    orders = request.user.orders.prefetch_related('items')
    return render(request, 'orders/order_list.html', {'orders': orders})
