# Peachy Glamora API (.NET 8 / ASP.NET Core)

Backend for the Peachy Glamora storefront: SQLServer + EF Core, JWT/Google/OTP auth, UPI QR
checkout, order/email/SMS notifications, and an admin API for catalog, orders, coupons and
analytics.

## 1. Prerequisites

- .NET 8 SDK
- Microsoft SQL Server (SQL Server 2019, SQL Server 2022, or SQL Server Express)
- SQL Server Management Studio (SSMS) (Recommended)
- An SMTP account for email (SendGrid, Amazon SES, Mailgun, or even Gmail SMTP for testing)
- A UPI VPA to receive payments (any `name@bank` handle works to start)

## 2. Configure

Copy your real values into `appsettings.Development.json` (local) or environment
variables / a secrets manager (production — **never commit real secrets to `appsettings.json`**):

| Key | What it's for |
|---|---|
| `ConnectionStrings:Default` | SQLServer connection string |
| `Jwt:Secret` | Random 64+ char string signing the JWTs — generate with `openssl rand -base64 48` |
| `Upi:VpaId` / `Upi:PayeeName` | Your UPI ID and display name, embedded in the QR |
| `Smtp:*` | SMTP host/port/user/password/from-address for order confirmation emails |
| `Sms:ApiKey` / `Sms:SenderId` | MSG91 auth key + DLT-approved sender ID for OTP/order SMS |
| `Hangfire:DashboardUser` / `Hangfire:DashboardPassword` | HTTP Basic Auth credentials guarding `/jobs` |
| `Google:ClientId` | OAuth Client ID from Google Cloud Console, for Google Sign-In |
| `AllowedOrigins` | Frontend URL(s) allowed to call this API (CORS) |
| `Cloudinary:*` | For product image uploads (not yet wired into a controller — see Known Gaps) |

In production, set these via environment variables using ASP.NET Core's double-underscore
convention, e.g. `Jwt__Secret`, `ConnectionStrings__Default`.

## 3. Create the database

```bash
dotnet tool install --global dotnet-ef   # once, if you don't have it
dotnet ef migrations add InitialCreate
dotnet ef database update
```

This creates all tables (Identity + catalog + orders + blog) and seeds the six top-level
categories (Earrings, Necklaces, Bangles, Rings, Bracelets, Combo Sets).

## 4. Run

```bash
dotnet restore
dotnet run
```

- API: `https://localhost:5001` (or whatever port `launchSettings.json` assigns)
- Swagger UI: `/swagger` (Development environment only)
- Hangfire dashboard (background jobs): `/jobs` — **lock this down before deploying**; it's
  wide open in this starter config.

## 5. Create your first admin user

There's no self-serve admin signup (by design). Register a normal account through
`POST /api/auth/register`, then promote it manually:

```sql
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r
WHERE u."Email" = 'you@peachyglamora.com' AND r."Name" = 'Admin';
```

## 6. Endpoint map (→ which frontend page calls what)

| Frontend page | Key endpoints |
|---|---|
| Homepage | `GET /api/categories`, `GET /api/products?isFeatured=true` etc. |
| Collection page | `GET /api/products?categorySlug=...&minPrice=...&sortBy=...` |
| Product page | `GET /api/products/{slug}`, `GET /api/products/{id}/related`, `GET/POST /api/products/{id}/reviews`, `GET/POST /api/products/{id}/questions` |
| Cart | `GET/POST/PUT/DELETE /api/cart[/items]`, `POST /api/cart/coupon`, `GET /api/shipping/check-pincode/{pincode}` |
| Checkout | `POST /api/orders/checkout` → returns `UpiIntentUri` + `UpiQrCodeBase64Png` to render |
| Checkout (UPI screen) | `GET /api/payments/{orderNumber}/qr`, `POST /api/payments/{orderNumber}/mark-paid-by-customer` |
| Account → Orders | `GET /api/orders`, `GET /api/orders/{orderNumber}`, `POST /api/orders/{orderNumber}/cancel` |
| Account → Returns | `POST /api/returns` |
| Account → Wishlist | `GET/POST/DELETE /api/wishlist[/{productId}]` |
| Account → Addresses | `GET/POST/PUT/DELETE /api/addresses[/{id}]` |
| Account → Support | `GET/POST /api/support-tickets[/{id}/replies]` |
| Account → Notifications | `GET /api/notifications`, `POST /api/notifications/{id}/read` |
| Blog | `GET /api/blog/posts[/{slug}]`, `GET /api/blog/categories` |
| Login/Register | `POST /api/auth/register`, `/login`, `/google`, `/otp/request`, `/otp/verify` |
| **Admin dashboard** | `GET /api/admin/analytics/summary`, `/top-products`, `/revenue-by-day` |
| **Admin products** | `GET/POST/PUT/DELETE /api/admin/products[/{id}]`, `POST .../variants`, `PUT .../variants/{id}/stock`, `POST .../images` |
| **Admin categories** | `GET/POST/PUT/DELETE /api/admin/categories[/{id}]` |
| **Admin orders** | `GET/PUT /api/admin/orders[/{id}/status]` |
| **Admin returns** | `GET/PUT /api/admin/returns[/{id}/status]` |
| **Admin customers** | `GET /api/admin/customers[/{id}]` |
| **Admin coupons** | `GET/POST/PUT/DELETE /api/admin/coupons[/{id}]` |
| **Admin reviews/Q&A** | `GET/DELETE /api/admin/reviews[/{id}]`, `POST /api/admin/reviews/questions/{id}/answer` |
| **Admin blog** | `GET/POST/PUT/DELETE /api/admin/blog[/{id}]` |
| **Admin payments** | `POST /api/payments/{orderNumber}/confirm` (Admin/Support only — reconciles a UPI payment) |

## 7. How UPI payment actually gets confirmed

A plain UPI VPA has no webhook — banks don't expose real-time payment callbacks to
individuals/small merchants. The flow here is:

1. Checkout generates a QR (`UpiQrPaymentService`) → customer scans and pays in their UPI app.
2. Customer taps "I've Paid" → `POST /api/payments/{orderNumber}/mark-paid-by-customer` (flags
   it for priority review; does **not** mark the order paid on its own).
3. Staff checks the bank statement / UPI app alert and calls
   `POST /api/payments/{orderNumber}/confirm` → order moves to `Confirmed`, customer gets an
   email + SMS.

If order volume grows enough that manual reconciliation doesn't scale, swap
`UpiQrPaymentService` for a UPI-enabled aggregator (Razorpay, Cashfree, PhonePe PG) that has a
real webhook — `IPaymentGatewayService` is the seam built for that swap; nothing else changes.

## 8. Known gaps / next steps

- **Migrations haven't been generated or run** — do that in your own environment (step 3).
- **Not yet compiled** — written without a live `dotnet build` (no network in the authoring
  sandbox). Run `dotnet build` before trusting it fully; fix any straggling typos it surfaces.
- **Loyalty points, referral program, and gift card redemption** are modeled in the database
  but have no business logic wired up yet.
- **Hangfire dashboard basic-auth credentials default to a placeholder password** —
  `Hangfire:DashboardPassword` in `appsettings.json` must be changed before deploying, same as
  every other `CHANGE_ME` value.
- **Rate limiting** is only applied to `/api/auth/*` — consider extending it to checkout/cart
  endpoints too if you see abuse.

## 9. Deployment notes

- Any Docker-capable host works (Railway, Render, Azure App Service, Fly.io). A `Dockerfile`
  isn't included yet — standard ASP.NET Core 8 multi-stage build applies.
- Point `ConnectionStrings:Default` at your managed SQLServer instance.
- Set `ASPNETCORE_ENVIRONMENT=Production` so Swagger and stack traces are disabled.
- Put the API behind HTTPS at the load balancer/proxy if the host doesn't terminate TLS for you.
