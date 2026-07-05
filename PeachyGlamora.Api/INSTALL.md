# Peachy Glamora API - New Machine Setup Guide

## Prerequisites

-   .NET 8 SDK
-   SQL Server (Express / Developer / Standard)
-   SQL Server Management Studio (SSMS) *(Recommended)*
-   Visual Studio 2022 with **ASP.NET and web development** workload

------------------------------------------------------------------------

## 1. Clone or Copy the Project

``` bash
git clone <repository-url>
```

Or copy the project folder.

------------------------------------------------------------------------

## 2. Restore NuGet Packages

### .NET CLI

``` bash
dotnet restore
```

### Visual Studio Package Manager Console

``` powershell
Restore-Package
```

------------------------------------------------------------------------

## 3. Configure Connection String

### Windows Authentication

``` json
"ConnectionStrings": {
  "Default": "Server=YOUR_SERVER;Database=PeachyGlamora;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

### SQL Server Authentication

``` json
"ConnectionStrings": {
  "Default": "Server=YOUR_SERVER;Database=PeachyGlamora;User Id=sa;Password=YourPassword;TrustServerCertificate=True;"
}
```

------------------------------------------------------------------------

## 4. Create/Update Database

Package Manager Console:

``` powershell
Update-Database
```

or CLI:

``` bash
dotnet ef database update
```

------------------------------------------------------------------------

## 5. Build the Project

``` bash
dotnet build
```

------------------------------------------------------------------------

## 6. Run the API

``` bash
dotnet run
```

Or press **F5** in Visual Studio.

Swagger:

    https://localhost:<port>/swagger

------------------------------------------------------------------------

## 7. Install EF CLI (if required)

``` bash
dotnet tool install --global dotnet-ef
```

Update:

``` bash
dotnet tool update --global dotnet-ef
```

Verify:

``` bash
dotnet ef --version
```

------------------------------------------------------------------------

## Packages Used

If starting from a blank project, install:

``` powershell
Install-Package Microsoft.EntityFrameworkCore.SqlServer -Version 8.0.20
Install-Package Microsoft.EntityFrameworkCore.Tools -Version 8.0.20
Install-Package Microsoft.EntityFrameworkCore.Design -Version 8.0.20

Install-Package Hangfire.AspNetCore -Version 1.8.23
Install-Package Hangfire.SqlServer -Version 1.8.23

Install-Package FluentValidation.AspNetCore -Version 11.3.0
Install-Package MailKit -Version 4.17.0
Install-Package Swashbuckle.AspNetCore -Version 6.7.3
Install-Package CloudinaryDotNet -Version 1.26.1
Install-Package Google.Apis.Auth -Version 1.68.0
Install-Package QRCoder -Version 1.6.0
Install-Package Microsoft.AspNetCore.Authentication.JwtBearer -Version 8.0.8
Install-Package Microsoft.AspNetCore.Identity.EntityFrameworkCore -Version 8.0.8
```

> Normally these do **not** need to be installed manually.
> `dotnet restore` restores them automatically from the project file.

------------------------------------------------------------------------

## First-Time Setup Checklist

Run:

``` powershell
dotnet restore
dotnet ef database update
dotnet build
dotnet run
```

------------------------------------------------------------------------

## Verification Checklist

-   .NET 8 SDK installed
-   SQL Server running
-   Connection string updated
-   NuGet packages restored
-   Database created successfully
-   Project builds without errors
-   API starts successfully
-   Swagger opens successfully
