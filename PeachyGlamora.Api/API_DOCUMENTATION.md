# Peachy Glamora API — Reference Documentation

Base URL: `https://api.peachyglamora.com` (or `https://localhost:5001` locally)
All request/response bodies are JSON. All dates are UTC ISO-8601.

**Auth header** (for endpoints marked 🔒): `Authorization: Bearer <accessToken>`
**Admin-only** endpoints are marked 🔑 and require the caller's JWT to carry the `Admin` role
(and/or `Support` where noted).
**Guest cart header**: cart endpoints accept `X-Guest-Cart-Id: <any-client-generated-string>`
for non-logged-in shoppers.

Standard error shape (from global exception middleware / manual `BadRequest`/`NotFound` calls):
```json
{ "error": "Human-readable message here" }
```
Validation errors (FluentValidation, HTTP 400) return the default ASP.NET Core shape:
```json
{ "errors": { "Email": ["'Email' is not a valid email address."] } }
```

---

## 1. Auth — `/api/auth`

### POST `/api/auth/register`
Public. Rate-limited (10 req/min per client).

**Request fields**

| Field | Type | Required |
|---|---|---|
| fullName | string | ✅ |
| email | string (email format) | ✅ |
| password | string, min 8 chars | ✅ |
| phone | string, 10–15 digits | optional |

```json
{
  "fullName": "Ananya Rao",
  "email": "ananya@example.com",
  "password": "GlamGirl123",
  "phone": "+919876543210"
}
```

**Response 200 (`AuthResponse`)**
```json
{
  "userId": "3f2a1c9e-...-b21",
  "fullName": "Ananya Rao",
  "email": "ananya@example.com",
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAt": "2026-07-09T10:15:00Z"
}
```
**400** — `{ "error": "An account with this email already exists." }`

---

### POST `/api/auth/login`
Public. Rate-limited.

| Field | Type | Required |
|---|---|---|
| email | string | ✅ |
| password | string | ✅ |

```json
{ "email": "ananya@example.com", "password": "GlamGirl123" }
```

**Response 200** — same `AuthResponse` shape as register.
**401** — `{ "error": "Invalid email or password." }`

---

### POST `/api/auth/google`
Public. Rate-limited.

| Field | Type | Required |
|---|---|---|
| idToken | string (Google ID token from Google Identity Services on the frontend) | ✅ |

```json
{ "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6..." }
```

**Response 200** — `AuthResponse` (a new account is auto-created on first Google login).
**401** — `{ "error": "Invalid Google sign-in token." }` or `{ "error": "Google account email is not verified." }`

---

### POST `/api/auth/otp/request`
Public. Rate-limited.

| Field | Type | Required |
|---|---|---|
| phoneNumber | string, 10–15 digits | ✅ |

```json
{ "phoneNumber": "+919876543210" }
```

**Response 200**
```json
{ "message": "OTP sent." }
```

---

### POST `/api/auth/otp/verify`
Public. Rate-limited.

| Field | Type | Required |
|---|---|---|
| phoneNumber | string | ✅ |
| code | string, exactly 6 digits | ✅ |

```json
{ "phoneNumber": "+919876543210", "code": "482913" }
```

**Response 200** — `AuthResponse` (creates a lightweight account on first OTP login if the phone number is new).
**400** — `{ "error": "Invalid or expired OTP." }`

---

## 2. Categories — `/api/categories`

### GET `/api/categories`
Public. Powers the mega-menu / homepage category circles.

**Response 200**
```json
[
  {
    "id": 2,
    "name": "Necklaces",
    "slug": "necklaces",
    "imageUrl": "https://res.cloudinary.com/.../necklaces.jpg",
    "subCategories": [
      { "id": 12, "name": "Chokers", "slug": "chokers" },
      { "id": 13, "name": "Long Chains", "slug": "long-chains" }
    ]
  }
]
```

---

## 3. Products — `/api/products`

### GET `/api/products`
Public. Backs the Collection page — every filter/sort UI control maps to a query param.

**Query params (all optional)**

| Param | Type | Notes |
|---|---|---|
| categorySlug | string | e.g. `necklaces` |
| minPrice, maxPrice | decimal | |
| colors | string[] (repeat param, e.g. `colors=RoseGold&colors=Gold`) | |
| materials | string[] | values from `ProductMaterial` enum: `RoseGoldPlated, GoldPlated, Kundan, Pearl, AmericanDiamond, Oxidised, Beaded` |
| occasions | string[] | values from `ProductOccasion` enum: `Bridal, Party, Office, Daily, Festive` |
| isNewArrival, isBestSeller, isTrending, inStockOnly | bool | |
| minRating | int (1–5) | |
| sortBy | string | `newest` (default) \| `priceLow` \| `priceHigh` \| `bestselling` \| `popular` |
| page | int | default 1 |
| pageSize | int | default 12 |

**Example request**
```
GET /api/products?categorySlug=necklaces&occasions=Bridal&sortBy=priceLow&page=1&pageSize=12
```

**Response 200 (`PagedResult<ProductListItemDto>`)**
```json
{
  "items": [
    {
      "id": 101,
      "name": "Rosette Cascade Bridal Necklaces",
      "slug": "rosette-cascade-bridal-necklaces",
      "categoryName": "Necklaces",
      "price": 2149.00,
      "compareAtPrice": 3499.00,
      "primaryImageUrl": "https://res.cloudinary.com/.../rosette.jpg",
      "averageRating": 4.8,
      "reviewCount": 312,
      "inStock": true,
      "tag": "Bestseller"
    }
  ],
  "totalCount": 124,
  "page": 1,
  "pageSize": 12
}
```

---

### GET `/api/products/{slug}`
Public. Backs the Product Detail page.

**Response 200 (`ProductDetailDto`)**
```json
{
  "id": 101,
  "name": "Rosette Cascade Bridal Necklaces",
  "slug": "rosette-cascade-bridal-necklaces",
  "description": "Rose gold plated brass base with kundan and pearl detailing...",
  "sku": "PG-NKS-1042-RG",
  "price": 2149.00,
  "compareAtPrice": 3499.00,
  "taxRatePercent": 3.0,
  "imageUrls": ["https://.../1.jpg", "https://.../2.jpg"],
  "variants": [
    { "id": 501, "color": "Rose Gold", "colorHex": "#C08469", "size": "16\"", "price": 2149.00, "stockQuantity": 6 },
    { "id": 502, "color": "Gold", "colorHex": "#C6A15B", "size": "16\"", "price": 2149.00, "stockQuantity": 3 }
  ],
  "averageRating": 4.8,
  "reviewCount": 312,
  "stockQuantity": 9
}
```
**404** if slug not found or product inactive.

---

### GET `/api/products/{id}/related`
Public. "You May Also Like" — same category, 4 items.

**Response 200** — array of `ProductListItemDto` (same shape as the list endpoint).

---

## 4. Reviews & Q&A — `/api/products/{productId}/reviews`, `/api/products/{productId}/questions`

### GET `/api/products/{productId}/reviews`
Public.

**Query params:** `page` (default 1), `pageSize` (default 10)

**Response 200**
```json
{
  "total": 312,
  "average": 4.8,
  "breakdown": [
    { "stars": 5, "count": 243 },
    { "stars": 4, "count": 44 },
    { "stars": 3, "count": 16 }
  ],
  "items": [
    {
      "id": 88,
      "rating": 5,
      "title": "Stunning for the price",
      "comment": "Wore this for my cousin's wedding — got so many compliments.",
      "isVerifiedPurchase": true,
      "createdAt": "2026-06-18T09:12:00Z",
      "userName": "Ritika S."
    }
  ]
}
```

### POST `/api/products/{productId}/reviews` 🔒
Requires login. "Verified Purchase" badge is set automatically if the user has a **delivered** order containing this product — not a client-supplied field.

| Field | Type | Required |
|---|---|---|
| rating | int (1–5) | ✅ |
| title | string | optional |
| comment | string | ✅ |

```json
{ "rating": 5, "title": "Beautiful!", "comment": "Exactly like the photos, arrived fast." }
```

**Response 200**
```json
{ "message": "Review submitted." }
```

---

### GET `/api/products/{productId}/questions`
Public. Returns only **answered** questions.

**Response 200**
```json
[
  {
    "id": 4,
    "productId": 101,
    "userId": "3f2a1c9e-...",
    "question": "Does this come with matching earrings?",
    "answer": "Yes, the set includes a matching pair of drop earrings.",
    "askedAt": "2026-06-10T08:00:00Z",
    "answeredAt": "2026-06-10T14:22:00Z"
  }
]
```

### POST `/api/products/{productId}/questions` 🔒
**Body:** a raw JSON string (not an object) — the question text itself.
```json
"Is the chain length adjustable?"
```
**Response 200**
```json
{ "message": "Question submitted — our team typically answers within 24 hours." }
```

---

## 5. Wishlist — `/api/wishlist` 🔒 (all endpoints require login)

### GET `/api/wishlist`
```json
[
  { "id": 101, "name": "Rosette Cascade Bridal Necklaces", "slug": "rosette-cascade-...", "imageUrl": "https://.../1.jpg" }
]
```

### POST `/api/wishlist/{productId}`
No body. Idempotent — adding a product already in the wishlist is a no-op.
**Response 200** (empty body)

### DELETE `/api/wishlist/{productId}`
No body. **Response 200** (empty body)

---

## 6. Cart — `/api/cart`

Identify the caller either via 🔒 JWT (logged-in) **or** the `X-Guest-Cart-Id` header (guest).
If neither is present, the cart is effectively empty/unscoped.

### GET `/api/cart?coupon={code}`
Public (guest or logged-in). `coupon` query param optional — re-applies a previously entered code.

**Response 200 (`CartSummaryDto`)**
```json
{
  "items": [
    {
      "id": 12,
      "productVariantId": 501,
      "productName": "Rosette Cascade Bridal Necklaces",
      "imageUrl": "https://.../1.jpg",
      "color": "Rose Gold",
      "size": "16\"",
      "unitPrice": 2149.00,
      "quantity": 1,
      "availableStock": 6
    }
  ],
  "subtotal": 2149.00,
  "discountAmount": 429.80,
  "estimatedTax": 51.57,
  "shippingEstimate": 0.00,
  "total": 1770.77,
  "appliedCouponCode": "GLAM20"
}
```

### POST `/api/cart/items`
| Field | Type | Required |
|---|---|---|
| productVariantId | int | ✅ |
| quantity | int, 1–20 | ✅ |

```json
{ "productVariantId": 501, "quantity": 1 }
```
**Response 200** — updated `CartSummaryDto` (as above).
**400** — `{ "error": "Not enough stock available." }`

### PUT `/api/cart/items/{id}`
| Field | Type | Required |
|---|---|---|
| quantity | int (0 removes the item) | ✅ |

```json
{ "quantity": 2 }
```
**Response 200** — updated `CartSummaryDto`.

### DELETE `/api/cart/items/{id}`
No body. **Response 200** — updated `CartSummaryDto`.

### POST `/api/cart/coupon`
| Field | Type | Required |
|---|---|---|
| code | string | ✅ |

```json
{ "code": "GLAM20" }
```
**Response 200** — `CartSummaryDto` with `discountAmount` applied.
**400** — `{ "error": "Minimum order value of ₹999 required." }` (or expired/invalid/limit-reached messages)

### GET `/api/shipping/check-pincode/{pincode}`
Public. (Mounted via the Cart controller but at its own top-level route.)

**Response 200 (`PincodeCheckResponse`)**
```json
{ "isServiceable": true, "estimatedDeliveryDays": 4, "codAvailable": true }
```

---

## 7. Orders — `/api/orders`, `/api/returns` 🔒 (all endpoints require login)

### POST `/api/orders/checkout`
| Field | Type | Required |
|---|---|---|
| shippingAddressId | int | ✅ |
| billingAddressId | int | ✅ |
| paymentMethod | string — one of `UPI`, `Card`, `NetBanking`, `Wallet`, `CashOnDelivery`, `GiftCard` | ✅ |
| couponCode | string | optional |
| giftCardCode | string | optional (not yet redeemed in logic — modeled only) |

```json
{
  "shippingAddressId": 7,
  "billingAddressId": 7,
  "paymentMethod": "UPI",
  "couponCode": "GLAM20"
}
```

**Response 200 (`OrderConfirmationDto`)**
```json
{
  "orderNumber": "PG-2607024821",
  "totalAmount": 1770.77,
  "status": "Pending",
  "estimatedDeliveryDate": "2026-07-07T00:00:00Z",
  "upiIntentUri": "upi://pay?pa=peachyglamora@okhdfcbank&pn=Peachy%20Glamora&tr=PG-2607024821&am=1770.77&cu=INR&tn=Peachy%20Glamora%20Order%20PG-2607024821",
  "upiQrCodeBase64Png": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg..."
}
```
For `CashOnDelivery`, `upiIntentUri`/`upiQrCodeBase64Png` are both `null` and `status` is `"Confirmed"` immediately.
**400** — `{ "error": "Your bag is empty." }` or `{ "error": "<Product> no longer has enough stock." }`

---

### GET `/api/orders`
Powers Account → Orders list.

**Response 200**
```json
[
  {
    "orderNumber": "PG-2607024821",
    "status": "Confirmed",
    "totalAmount": 1770.77,
    "createdAt": "2026-07-02T10:00:00Z",
    "estimatedDeliveryDate": "2026-07-07T00:00:00Z",
    "itemCount": 1
  }
]
```

### GET `/api/orders/{orderNumber}`
Order tracking detail with full status timeline.

**Response 200**
```json
{
  "id": 44,
  "orderNumber": "PG-2607024821",
  "status": "Confirmed",
  "totalAmount": 1770.77,
  "items": [ { "id": 90, "productNameSnapshot": "Rosette Cascade Bridal Necklaces", "unitPriceSnapshot": 2149.00, "quantity": 1 } ],
  "statusHistory": [
    { "status": "Pending", "note": "Order placed", "changedAt": "2026-07-02T10:00:00Z" },
    { "status": "Confirmed", "note": "UPI payment verified by staff", "changedAt": "2026-07-02T10:20:00Z" }
  ],
  "shippingAddress": { "fullName": "Ananya Rao", "line1": "...", "city": "Mumbai", "pincode": "400001" },
  "payment": { "method": "UPI", "status": "Paid", "amount": 1770.77 }
}
```
**404** if the order doesn't exist or doesn't belong to the caller.

### POST `/api/orders/{orderNumber}/cancel`
No body.
**Response 200** — `{ "message": "Order cancelled." }`
**400** — `{ "error": "This order can no longer be cancelled — it has already shipped. Please request a return instead." }`

---

### POST `/api/returns` 🔒
| Field | Type | Required |
|---|---|---|
| orderItemId | int | ✅ |
| reason | string | ✅ |
| isExchange | bool | ✅ |

```json
{ "orderItemId": 90, "reason": "Wrong size", "isExchange": true }
```
**Response 200** — `{ "message": "Return request submitted. Our team will review it within 24 hours." }`
**400** — `{ "error": "Returns can only be requested for delivered orders." }`

---

## 8. Payments — `/api/payments` 🔒

### GET `/api/payments/{orderNumber}/qr`
Re-fetches the UPI QR for a pending order.

**Response 200**
```json
{
  "upiIntentUri": "upi://pay?pa=...",
  "qrCodeBase64Png": "data:image/png;base64,...",
  "paymentStatus": "Pending"
}
```

### POST `/api/payments/{orderNumber}/mark-paid-by-customer`
No body. Flags the order for priority manual verification — **does not** mark it paid.

**Response 200** — `{ "message": "Thanks! We'll confirm your payment shortly and update your order." }`

### POST `/api/payments/{orderNumber}/confirm` 🔑 (Admin or Support)
**Body:** raw JSON string or `null` — the gateway/bank transaction reference.
```json
"UPI-2026070212345"
```
**Response 200** — `{ "message": "Payment confirmed." }` (also triggers a status-update SMS/email to the customer)

---

## 9. Addresses — `/api/addresses` 🔒

### GET `/api/addresses`
```json
[
  { "id": 7, "fullName": "Ananya Rao", "phone": "+919876543210", "line1": "12 Rose Villa",
    "line2": null, "city": "Mumbai", "state": "Maharashtra", "pincode": "400001",
    "country": "India", "type": "Shipping", "isDefault": true }
]
```

### POST `/api/addresses`
| Field | Type | Required |
|---|---|---|
| fullName | string | ✅ |
| phone | string | ✅ |
| line1 | string | ✅ |
| line2 | string | optional |
| city | string | ✅ |
| state | string | ✅ |
| pincode | string | ✅ |
| type | `"Shipping"` \| `"Billing"` | ✅ |
| isDefault | bool | ✅ |

```json
{
  "fullName": "Ananya Rao", "phone": "+919876543210",
  "line1": "12 Rose Villa", "line2": null, "city": "Mumbai",
  "state": "Maharashtra", "pincode": "400001", "type": "Shipping", "isDefault": true
}
```
**Response 200** — the created address object (same shape as GET).

### PUT `/api/addresses/{id}`
Same body as POST. **Response 200** — updated address.

### DELETE `/api/addresses/{id}`
No body. **Response 200** (empty body). **404** if not found / not owned by caller.

---

## 10. Support Tickets — `/api/support-tickets` 🔒

### GET `/api/support-tickets`
```json
[ { "id": 3, "subject": "Where is my order?", "status": "Open", "createdAt": "2026-07-01T12:00:00Z" } ]
```

### GET `/api/support-tickets/{id}`
```json
{
  "id": 3, "subject": "Where is my order?", "message": "It's been 5 days...",
  "relatedOrderId": 44, "status": "InProgress",
  "replies": [ { "authorId": "...", "isFromStaff": true, "message": "It shipped yesterday!", "createdAt": "2026-07-02T09:00:00Z" } ]
}
```

### POST `/api/support-tickets`
| Field | Type | Required |
|---|---|---|
| subject | string | ✅ |
| message | string | ✅ |
| relatedOrderId | int | optional |

```json
{ "subject": "Where is my order?", "message": "It's been 5 days and no update.", "relatedOrderId": 44 }
```
**Response 200** — the created ticket.

### POST `/api/support-tickets/{id}/replies`
| Field | Type | Required |
|---|---|---|
| message | string | ✅ |

```json
{ "message": "Thanks, just checking in again." }
```
**Response 200** (empty body). Ticket status auto-flips to `InProgress`.

---

## 11. Notifications — `/api/notifications` 🔒

### GET `/api/notifications`
Latest 50, newest first.
```json
[ { "id": 15, "title": "Order Shipped", "body": "Your order PG-2607024821 is on its way!", "linkUrl": "/account/orders/PG-2607024821", "isRead": false, "createdAt": "2026-07-02T11:00:00Z" } ]
```

### POST `/api/notifications/{id}/read`
No body. **Response 200** (empty body).

---

## 12. Blog — `/api/blog`

### GET `/api/blog/posts?category={slug}&search={q}&page=1&pageSize=9`
Public. All query params optional.
```json
{
  "total": 24, "page": 1, "pageSize": 9,
  "items": [
    { "title": "5 Ways to Style Kundan Jewellery", "slug": "5-ways-style-kundan",
      "excerpt": "From daily wear to your best friend's wedding...",
      "coverImageUrl": "https://.../cover.jpg", "publishedAt": "2026-06-20T00:00:00Z",
      "categoryName": "Styling Tips" }
  ]
}
```

### GET `/api/blog/posts/{slug}`
Public.
```json
{
  "post": { "title": "5 Ways to Style Kundan Jewellery", "contentHtml": "<p>...</p>", "authorName": "Team Peachy Glamora" },
  "related": [ { "title": "Bridal Jewellery Checklist", "slug": "bridal-checklist", "coverImageUrl": "https://.../2.jpg" } ]
}
```

### GET `/api/blog/categories`
Public. Returns raw `BlogCategory[]`: `[{ "id": 1, "name": "Styling Tips", "slug": "styling-tips" }]`

---

## 13. Admin API — all routes below require 🔑 `Admin` role (a few also allow `Support`)

### 13.1 Products — `/api/admin/products`

**GET `/api/admin/products?page=1&pageSize=20&search=necklace`**
```json
{ "total": 340, "page": 1, "pageSize": 20,
  "items": [ { "id": 101, "name": "Rosette Cascade...", "slug": "...", "isActive": true, "categoryName": "Necklaces", "totalStock": 9, "minPrice": 2149.00 } ] }
```

**GET `/api/admin/products/{id}`** — full `Product` entity incl. `variants[]`, `images[]`.

**POST `/api/admin/products`** — creates a product.

| Field | Type | Required |
|---|---|---|
| name | string | ✅ |
| slug | string | ✅ |
| description | string | ✅ |
| shortDescription | string | ✅ |
| categoryId | int | ✅ |
| occasion | `Bridal\|Party\|Office\|Daily\|Festive` | ✅ |
| material | `RoseGoldPlated\|GoldPlated\|Kundan\|Pearl\|AmericanDiamond\|Oxidised\|Beaded` | ✅ |
| basePrice | decimal | ✅ |
| compareAtPrice | decimal | optional |
| taxRatePercent | decimal | ✅ |
| isNewArrival, isBestSeller, isTrending, isFeatured, isActive | bool | ✅ |
| metaTitle, metaDescription | string | optional |

```json
{
  "name": "Peach Blossom Studs", "slug": "peach-blossom-studs",
  "description": "Delicate everyday studs...", "shortDescription": "Everyday peach studs",
  "categoryId": 1, "occasion": "Daily", "material": "RoseGoldPlated",
  "basePrice": 499, "compareAtPrice": 799, "taxRatePercent": 3,
  "isNewArrival": true, "isBestSeller": false, "isTrending": false, "isFeatured": false, "isActive": true,
  "metaTitle": null, "metaDescription": null
}
```
**Response 201** — the created `Product`, `Location` header points to `GET /api/admin/products/{id}`.

**PUT `/api/admin/products/{id}`** — same body as POST. **Response 200** — updated product.

**DELETE `/api/admin/products/{id}`** — soft-delete (`isActive = false`, keeps historical orders valid). No body. **Response 200** — `{ "message": "Product deactivated." }`

**POST `/api/admin/products/{id}/variants`**

| Field | Type | Required |
|---|---|---|
| sku | string | ✅ |
| color, colorHex, size | string | optional |
| priceOverride | decimal | ✅ |
| stockQuantity | int | ✅ |
| isDefault | bool | ✅ |

```json
{ "sku": "PG-EAR-2001-RG", "color": "Rose Gold", "colorHex": "#C08469", "size": null, "priceOverride": 499, "stockQuantity": 40, "isDefault": true }
```
**Response 200** — created `ProductVariant`.

**PUT `/api/admin/products/variants/{variantId}/stock`** — **Body:** raw int, e.g. `35`. **Response 200** — updated variant.

**POST `/api/admin/products/{id}/images`**

| Field | Type | Required |
|---|---|---|
| url | string (from Media upload) | ✅ |
| altText | string | optional |
| displayOrder | int | ✅ |
| isPrimary | bool | ✅ |

```json
{ "url": "https://res.cloudinary.com/.../peach-studs.jpg", "altText": "Peach Blossom Studs", "displayOrder": 0, "isPrimary": true }
```
**Response 200** — created `ProductImage`.

---

### 13.2 Categories — `/api/admin/categories`

**GET** — full `Category[]` list (flat, all statuses).

**POST / PUT `/api/admin/categories[/{id}]`**

| Field | Type | Required |
|---|---|---|
| name | string | ✅ |
| slug | string | ✅ |
| description, imageUrl | string | optional |
| parentCategoryId | int | optional |
| displayOrder | int | ✅ |
| isActive | bool | ✅ |

```json
{ "name": "Chokers", "slug": "chokers", "description": null, "imageUrl": null, "parentCategoryId": 2, "displayOrder": 1, "isActive": true }
```

**DELETE `/api/admin/categories/{id}`** — hard delete, blocked if products still reference it: `{ "error": "Cannot delete a category that still has products. Reassign or deactivate them first." }`

---

### 13.3 Orders — `/api/admin/orders` (Admin, Support)

**GET `/api/admin/orders?status=Pending&search=PG-260702&page=1&pageSize=25`**
```json
{ "total": 8, "page": 1, "pageSize": 25,
  "items": [ { "id": 44, "orderNumber": "PG-2607024821", "status": "Pending", "totalAmount": 1770.77, "createdAt": "2026-07-02T10:00:00Z", "customerName": "Ananya Rao", "customerEmail": "ananya@example.com" } ] }
```

**GET `/api/admin/orders/{id}`** — full order incl. items, addresses, payment, status history.

**PUT `/api/admin/orders/{id}/status`**

| Field | Type | Required |
|---|---|---|
| status | one of the `OrderStatus` enum values: `Pending, Confirmed, Processing, Shipped, OutForDelivery, Delivered, Cancelled, Returned, RefundInitiated, Refunded` | ✅ |
| note | string | optional |

```json
{ "status": "Shipped", "note": "Dispatched via Delhivery, AWB 88213XXXXX" }
```
**Response 200** — `{ "message": "Order status updated and customer notified." }` (fires SMS + email automatically)

---

### 13.4 Returns — `/api/admin/returns` (Admin, Support)

**GET `/api/admin/returns?status=Requested`** — array of `ReturnRequest` with nested `orderItem.order`.

**PUT `/api/admin/returns/{id}/status`** — **Body:** raw string, one of `Requested, Approved, Rejected, PickedUp, Refunded`.
```json
"Approved"
```
**Response 200** — updated `ReturnRequest`.

---

### 13.5 Customers — `/api/admin/customers`

**GET `/api/admin/customers?search=ananya&page=1&pageSize=25`**
```json
{ "total": 1240, "page": 1, "pageSize": 25,
  "items": [ { "id": "3f2a...", "fullName": "Ananya Rao", "email": "ananya@example.com", "phoneNumber": "+919876543210", "createdAt": "2026-05-01T00:00:00Z", "loyaltyPoints": 0, "orderCount": 3, "totalSpent": 5312.40 } ] }
```

**GET `/api/admin/customers/{id}`** — full user record incl. `addresses[]` and `orders[]`.

---

### 13.6 Coupons — `/api/admin/coupons`

**GET** — full `Coupon[]`.

**POST / PUT `/api/admin/coupons[/{id}]`**

| Field | Type | Required |
|---|---|---|
| code | string (auto-uppercased) | ✅ |
| type | `PercentOff\|FlatOff\|FreeShipping\|BuyXGetY` | ✅ |
| value | decimal (percent or flat ₹) | ✅ |
| minOrderValue | decimal | optional |
| maxDiscountAmount | decimal | optional |
| validFrom, validTo | datetime | ✅ |
| usageLimitPerUser | int | optional |
| totalUsageLimit | int | optional |
| isActive | bool | ✅ |

```json
{
  "code": "GLAM20", "type": "PercentOff", "value": 20,
  "minOrderValue": 999, "maxDiscountAmount": 500,
  "validFrom": "2026-07-01T00:00:00Z", "validTo": "2026-12-31T23:59:59Z",
  "usageLimitPerUser": 1, "totalUsageLimit": 5000, "isActive": true
}
```
**400** on POST if code already exists: `{ "error": "A coupon with this code already exists." }`

**DELETE `/api/admin/coupons/{id}`** — soft-delete (`isActive = false`).

---

### 13.7 Reviews & Q&A moderation — `/api/admin/reviews`

**GET `/api/admin/reviews?page=1&pageSize=25`** — array of:
```json
{ "id": 88, "rating": 5, "title": "Stunning", "comment": "...", "isVerifiedPurchase": true, "createdAt": "2026-06-18T09:12:00Z", "productName": "Rosette Cascade...", "customerName": "Ritika S." }
```

**DELETE `/api/admin/reviews/{id}`** — removes a review. No body.

**POST `/api/admin/reviews/questions/{id}/answer`** — **Body:** raw string, the answer text.
```json
"Yes, it includes matching earrings."
```
**Response 200** — updated `ProductQuestion`.

---

### 13.8 Blog — `/api/admin/blog`

**GET** — full `BlogPost[]`.

**POST / PUT `/api/admin/blog[/{id}]`**

| Field | Type | Required |
|---|---|---|
| title | string | ✅ |
| slug | string | ✅ |
| excerpt | string | ✅ |
| contentHtml | string | ✅ |
| coverImageUrl | string | ✅ |
| blogCategoryId | int | ✅ |
| authorName | string | ✅ |
| isPublished | bool | ✅ |
| metaTitle, metaDescription | string | optional |

```json
{
  "title": "5 Ways to Style Kundan Jewellery", "slug": "5-ways-style-kundan",
  "excerpt": "From daily wear to your best friend's wedding...",
  "contentHtml": "<p>Kundan jewellery has a way of...</p>",
  "coverImageUrl": "https://res.cloudinary.com/.../cover.jpg",
  "blogCategoryId": 1, "authorName": "Team Peachy Glamora", "isPublished": true,
  "metaTitle": null, "metaDescription": null
}
```

**DELETE `/api/admin/blog/{id}`** — hard delete.

---

### 13.9 Analytics — `/api/admin/analytics`

**GET `/api/admin/analytics/summary?days=30`**
```json
{
  "periodDays": 30, "totalRevenue": 412500.00, "orderCount": 189,
  "averageOrderValue": 2182.54, "newCustomers": 76,
  "pendingOrders": 4, "lowStockVariants": 11, "outOfStockVariants": 3
}
```

**GET `/api/admin/analytics/top-products?take=10`**
```json
[ { "productName": "Rosette Cascade Bridal Necklaces", "unitsSold": 58, "revenue": 124642.00 } ]
```

**GET `/api/admin/analytics/revenue-by-day?days=14`**
```json
[ { "date": "2026-06-20T00:00:00Z", "revenue": 18420.00, "orders": 9 } ]
```

---

### 13.10 Media (Cloudinary uploads) — `/api/admin/media`

**POST `/api/admin/media/upload?folder=products`**
Content-Type: `multipart/form-data`, field name `file`. `folder` query param: `products | categories | blog | banners` (anything else routes to a `misc` folder).
Max size 8MB; allowed types: `.jpg .jpeg .png .webp`.

**Response 200**
```json
{ "url": "https://res.cloudinary.com/peachyglamora/image/upload/v.../peach-studs.jpg", "publicId": "peachy-glamora/products/peach-studs_ab12cd", "width": 1200, "height": 1200 }
```
**400** — `{ "error": "File too large — maximum size is 8MB." }` / `{ "error": "Only JPG, PNG, and WEBP images are allowed." }`

**DELETE `/api/admin/media/{publicId}`** — `publicId` is the Cloudinary public ID returned from upload (URL-encode any `/` in it). No body. **Response 200** — `{ "message": "Image deleted." }`

---

## Quick reference: which fields are actually required vs optional

A field is **required** unless its C# type is nullable (`string?`, `int?`, `decimal?`, etc.) or the
record/DTO explicitly defaults it. FluentValidation currently enforces stricter rules only on:
`RegisterRequest`, `LoginRequest`, `RequestOtpRequest`, `VerifyOtpRequest`, `AddToCartRequest`,
`CheckoutRequest` (see `Validators/RequestValidators.cs`). Every other DTO relies on ASP.NET
Core's default model binding — a missing non-nullable `string` field will bind to `""` rather
than reject the request, so add validators for any admin DTO you want strictly enforced before
going live.
