from decimal import Decimal, InvalidOperation

from django.core.paginator import Paginator
from django.db.models import Count, Min, Prefetch, Q
from django.shortcuts import get_object_or_404, render

from banners.models import Banner
from .models import Brand, Category, Product

# value -> (visible label, order_by argument)
SORT_OPTIONS = {
    'name': ('По названию', 'name'),
    'price_asc': ('Сначала дешевле', 'sort_price'),
    'price_desc': ('Сначала дороже', '-sort_price'),
    'new': ('Сначала новинки', '-created_at'),
}
DEFAULT_SORT = 'name'
PER_PAGE = 24


def _base_product_qs():
    return (
        Product.objects.filter(is_active=True)
        .select_related('category', 'category__brand')
        .prefetch_related('variants', 'images')
        .annotate(sort_price=Min('variants__price'))
    )


def _decimal_or_none(raw):
    if not raw:
        return None
    try:
        return Decimal(str(raw).replace(',', '.'))
    except (InvalidOperation, ValueError):
        return None


def _apply_filters_and_sort(request, queryset):
    """Shared filtering/sorting for the category and search listings.
    Returns (queryset, context_bits) so templates can re-render the form
    with whatever the customer picked still selected."""
    selected_brands = request.GET.getlist('brand')
    selected_categories = request.GET.getlist('category')
    in_stock_only = request.GET.get('in_stock') == '1'
    price_min = _decimal_or_none(request.GET.get('price_min'))
    price_max = _decimal_or_none(request.GET.get('price_max'))

    if selected_brands:
        queryset = queryset.filter(category__brand__slug__in=selected_brands)
    if selected_categories:
        queryset = queryset.filter(category__slug__in=selected_categories)
    if in_stock_only:
        queryset = queryset.filter(variants__stock_qty__gt=0, variants__is_active=True)
    if price_min is not None:
        queryset = queryset.filter(variants__price__gte=price_min)
    if price_max is not None:
        queryset = queryset.filter(variants__price__lte=price_max)

    # Filtering across the variants relation can duplicate product rows.
    if in_stock_only or price_min is not None or price_max is not None:
        queryset = queryset.distinct()

    sort = request.GET.get('sort', DEFAULT_SORT)
    if sort not in SORT_OPTIONS:
        sort = DEFAULT_SORT
    order_by = SORT_OPTIONS[sort][1]
    # Products with no price at all should never crowd out the priced ones.
    queryset = queryset.order_by(*( [order_by] if sort == 'name' else [order_by, 'name'] ))

    context = {
        'sort': sort,
        'sort_options': [(value, label) for value, (label, _) in SORT_OPTIONS.items()],
        'selected_brands': selected_brands,
        'selected_categories': selected_categories,
        'in_stock_only': in_stock_only,
        'price_min': request.GET.get('price_min', ''),
        'price_max': request.GET.get('price_max', ''),
        'has_active_filters': bool(
            selected_brands or selected_categories or in_stock_only or price_min or price_max
        ),
    }
    return queryset, context


def _paginate(request, queryset):
    paginator = Paginator(queryset, PER_PAGE)
    return paginator.get_page(request.GET.get('page'))


def home(request):
    banners = Banner.objects.visible_now()
    categories = (
        Category.objects.filter(is_active=True, parent=None)
        .select_related('brand')
        .annotate(product_count=Count('products', filter=Q(products__is_active=True)))
        .filter(product_count__gt=0)
    )
    featured = (
        _base_product_qs()
        .filter(variants__stock_qty__gt=0)
        .distinct()
        .order_by('-created_at')[:8]
    )
    brands = Brand.objects.filter(is_active=True).prefetch_related(
        Prefetch(
            'categories',
            queryset=Category.objects.filter(is_active=True, parent=None).prefetch_related(
                Prefetch('products', queryset=_base_product_qs())
            ),
        )
    )
    return render(request, 'catalog/home.html', {
        'banners': banners,
        'categories': categories,
        'featured': featured,
        'brands': brands,
    })


def category_detail(request, brand_slug, category_slug):
    category = get_object_or_404(
        Category.objects.select_related('brand'),
        brand__slug=brand_slug, slug=category_slug, is_active=True,
    )
    queryset = _base_product_qs().filter(category=category)
    queryset, filter_context = _apply_filters_and_sort(request, queryset)

    context = {
        'category': category,
        'brand': category.brand,
        'page_obj': _paginate(request, queryset),
        'total_count': queryset.count(),
        # On a category page the brand/category are already fixed, so the
        # sidebar only offers the filters that still make sense here.
        'show_taxonomy_filters': False,
    }
    context.update(filter_context)
    return render(request, 'catalog/category.html', context)


def search(request):
    query = request.GET.get('q', '').strip()
    queryset = _base_product_qs()
    if query:
        queryset = queryset.filter(
            Q(name__icontains=query)
            | Q(description__icontains=query)
            | Q(variants__article__icontains=query)
        ).distinct()
    else:
        queryset = queryset.none()

    queryset, filter_context = _apply_filters_and_sort(request, queryset)

    context = {
        'query': query,
        'page_obj': _paginate(request, queryset),
        'total_count': queryset.count(),
        'all_brands': Brand.objects.filter(is_active=True),
        'all_categories': Category.objects.filter(is_active=True, parent=None).select_related('brand'),
        'show_taxonomy_filters': True,
    }
    context.update(filter_context)
    return render(request, 'catalog/search.html', context)


def product_detail(request, slug):
    product = get_object_or_404(
        Product.objects.select_related('category', 'category__brand').prefetch_related('variants', 'images'),
        slug=slug, is_active=True,
    )
    related = (
        _base_product_qs()
        .filter(category=product.category)
        .exclude(pk=product.pk)[:4]
    )
    return render(request, 'catalog/product_detail.html', {
        'product': product,
        'related': related,
    })
