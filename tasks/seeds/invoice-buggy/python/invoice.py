"""Invoice library: prices an order and formats a receipt. All money is in whole cents."""
import json

TAX_BASIS_POINTS = {"CA": 800, "NY": 900, "OR": 0}
DEFAULT_TAX_BASIS_POINTS = 500
FREE_SHIPPING_FROM = 5000
SHIPPING_FEE = 599


class UnknownCode(Exception):
    pass


def parse_order(text):
    data = json.loads(text)
    items = [{"sku": i["sku"], "qty": i["qty"], "price_cents": i["price_cents"]} for i in data["items"]]
    return {"region": data["region"], "code": data["code"], "items": items}


def line_total(item):
    return item["qty"] * item["price_cents"]


def subtotal(items):
    return sum(line_total(i) for i in items)


def discount(code, amount):
    if code == "":
        return 0
    if code == "SAVE10":
        return amount // 10
    if code == "FLAT5":
        return min(500, amount)
    raise UnknownCode(code)


def shipping(amount_after_discount):
    return 0 if amount_after_discount >= FREE_SHIPPING_FROM else SHIPPING_FEE


def tax_rate(region):
    return TAX_BASIS_POINTS.get(region, DEFAULT_TAX_BASIS_POINTS)


def tax(region, amount):
    return (amount * tax_rate(region) + 5000) // 10000


def format_cents(cents):
    sign = "-" if cents < 0 else ""
    c = abs(cents)
    return f"{sign}{c // 100}.{c % 100:02d}"


def format_rate(bp):
    return f"{bp // 100}.{bp % 100:02d}%"


def receipt(order):
    items = order["items"]
    sub = subtotal(items)
    off = discount(order["code"], sub)
    after = sub - off
    ship = shipping(sub)
    t = tax(order["region"], after)
    total = after + ship + t
    lines = [f"{i['sku']} x{i['qty']} @ {format_cents(i['price_cents'])} = {format_cents(line_total(i))}" for i in items]
    lines.append(f"Subtotal: {format_cents(sub)}")
    if order["code"] != "":
        lines.append(f"Discount ({order['code']}): {format_cents(-off)}")
    lines.append(f"Shipping: {format_cents(ship)}")
    lines.append(f"Tax ({order['region']} {format_rate(tax_rate(order['region']))}): {format_cents(t)}")
    lines.append(f"Total: {format_cents(total)}")
    return lines
