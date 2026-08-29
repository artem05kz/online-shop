from .models import Brand


def nav_brands(request):
    """Makes the brand list available in the header on every page, not just
    the home view — cached per-request via request, not globally, so admin
    edits show up immediately without a server restart."""
    return {'nav_brands': Brand.objects.filter(is_active=True)}
