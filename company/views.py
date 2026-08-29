from django.shortcuts import render

from .models import Store


def store_list(request):
    """Адреса физических точек: пункты самовывоза и магазины."""
    stores = Store.objects.filter(is_active=True)
    cities = sorted({s.city for s in stores})
    selected_city = request.GET.get('city', '').strip()
    if selected_city:
        stores = [s for s in stores if s.city == selected_city]
    else:
        stores = list(stores)
    return render(request, 'company/store_list.html', {
        'stores': stores,
        'cities': cities,
        'selected_city': selected_city,
    })
