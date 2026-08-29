from django.db.models import Prefetch
from django.shortcuts import get_object_or_404, render

from banners.models import Banner
from .models import Brand, Category, Product, ProductVariant


def home(request):
    banners = Banner.objects.visible_now()
    categories_qs = Category.objects.filter(is_active=True, parent=None).prefetch_related(
        Prefetch(
            'products',
            queryset=Product.objects.filter(is_active=True).prefetch_related('variants', 'images'),
        )
    )
    brands = Brand.objects.filter(is_active=True).prefetch_related(
        Prefetch('categories', queryset=categories_qs)
    )
    query = request.GET.get('q', '').strip()
    search_results = None
    if query:
        search_results = (
            Product.objects.filter(is_active=True)
            .filter(name__icontains=query)
            .select_related('category', 'category__brand')[:40]
        )
    return render(request, 'catalog/home.html', {
        'banners': banners,
        'brands': brands,
        'query': query,
        'search_results': search_results,
    })


def category_detail(request, brand_slug, category_slug):
    category = get_object_or_404(
        Category.objects.select_related('brand'), brand__slug=brand_slug, slug=category_slug,
    )
    products = (
        category.products.filter(is_active=True)
        .prefetch_related('variants', 'images')
        .order_by('name')
    )
    return render(request, 'catalog/category.html', {
        'category': category,
        'products': products,
    })


def product_detail(request, slug):
    product = get_object_or_404(
        Product.objects.select_related('category', 'category__brand').prefetch_related('variants', 'images'),
        slug=slug, is_active=True,
    )
    return render(request, 'catalog/product_detail.html', {'product': product})
