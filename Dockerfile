# Day 58: multi-stage build for FieldOps.Api.
#
# Stage 1 ("build") uses the full SDK image (compilers, MSBuild, everything
# needed to build) — this image is large and never ships.
# Stage 2 ("final") uses the much smaller ASP.NET Core RUNTIME-only image
# (no compiler, no SDK) and copies in only the already-published output —
# this is the image that actually ships and runs.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the .csproj files first, matching their real relative folder
# structure (needed so each ProjectReference's relative path still resolves)
# — this lets Docker cache the "dotnet restore" layer separately from the
# actual source code, so editing a .cs file later doesn't force a full
# re-restore of every NuGet package.
COPY src/FieldOps.Api/FieldOps.Api.csproj src/FieldOps.Api/
COPY src/FieldOps.Modules.Organizations/FieldOps.Modules.Organizations.csproj src/FieldOps.Modules.Organizations/
COPY src/FieldOps.Modules.Employees/FieldOps.Modules.Employees.csproj src/FieldOps.Modules.Employees/
COPY src/FieldOps.Modules.WorkOrders/FieldOps.Modules.WorkOrders.csproj src/FieldOps.Modules.WorkOrders/
COPY src/FieldOps.Modules.Customers/FieldOps.Modules.Customers.csproj src/FieldOps.Modules.Customers/
COPY src/FieldOps.Modules.AuditLogs/FieldOps.Modules.AuditLogs.csproj src/FieldOps.Modules.AuditLogs/
RUN dotnet restore src/FieldOps.Api/FieldOps.Api.csproj

# Now copy the real source and publish. --no-restore reuses the layer above.
COPY src/FieldOps.Api/ src/FieldOps.Api/
COPY src/FieldOps.Modules.Organizations/ src/FieldOps.Modules.Organizations/
COPY src/FieldOps.Modules.Employees/ src/FieldOps.Modules.Employees/
COPY src/FieldOps.Modules.WorkOrders/ src/FieldOps.Modules.WorkOrders/
COPY src/FieldOps.Modules.Customers/ src/FieldOps.Modules.Customers/
COPY src/FieldOps.Modules.AuditLogs/ src/FieldOps.Modules.AuditLogs/
RUN dotnet publish src/FieldOps.Api/FieldOps.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FieldOps.Api.dll"]
