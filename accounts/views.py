from django.conf import settings
from django.contrib import messages
from django.contrib.auth import login
from django.contrib.auth.decorators import login_required
from django.shortcuts import redirect, render

from orders.forms import AddressForm
from orders.models import Address
from .forms import ProfileForm, RegisterForm


def register(request):
    if request.method == 'POST':
        form = RegisterForm(request.POST)
        if form.is_valid():
            user = form.save()
            login(request, user)
            return redirect('catalog:home')
    else:
        form = RegisterForm()
    return render(request, 'accounts/register.html', {'form': form})


@login_required
def profile(request):
    """Personal details tab. Addresses and orders live on their own pages so
    each screen stays short enough to read on a phone."""
    if request.method == 'POST':
        form = ProfileForm(request.POST, instance=request.user)
        if form.is_valid():
            form.save()
            messages.success(request, 'Данные сохранены.')
            return redirect('accounts:profile')
    else:
        form = ProfileForm(instance=request.user)
    return render(request, 'accounts/profile.html', {
        'form': form,
        'active_tab': 'profile',
    })


@login_required
def address_list(request):
    return render(request, 'accounts/address_list.html', {
        'addresses': request.user.addresses.all(),
        'active_tab': 'addresses',
    })


@login_required
def address_edit(request, pk=None):
    instance = None
    if pk is not None:
        instance = request.user.addresses.filter(pk=pk).first()
        if instance is None:
            messages.error(request, 'Адрес не найден.')
            return redirect('accounts:address_list')

    if request.method == 'POST':
        form = AddressForm(request.POST, instance=instance)
        if form.is_valid():
            address = form.save(commit=False)
            address.user = request.user
            address.save()
            # Only one address can be the default one.
            if address.is_default:
                request.user.addresses.exclude(pk=address.pk).update(is_default=False)
            messages.success(request, 'Адрес сохранён.')
            return redirect('accounts:address_list')
    else:
        initial = {}
        if instance is None:
            full_name = f'{request.user.first_name} {request.user.last_name}'.strip()
            if full_name:
                initial['full_name'] = full_name
        form = AddressForm(instance=instance, initial=initial)

    return render(request, 'accounts/address_form.html', {
        'form': form,
        'address': instance,
        'active_tab': 'addresses',
        # Ключ Google задаётся в настройках; без него берём открытые карты.
        'google_maps_key': settings.GOOGLE_MAPS_API_KEY,
    })


@login_required
def address_delete(request, pk):
    address = request.user.addresses.filter(pk=pk).first()
    if address is None:
        messages.error(request, 'Адрес не найден.')
    elif request.method == 'POST':
        address.delete()
        messages.success(request, 'Адрес удалён.')
    return redirect('accounts:address_list')
