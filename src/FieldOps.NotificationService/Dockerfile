# Day 76: this service's own multi-stage build, deliberately separate from
# the root Dockerfile (FieldOps.Api's) — no shared build context beyond the
# source tree, and no COPY of any FieldOps.Api or module .csproj at all. A
# single ProjectReference-free project makes this Dockerfile much simpler
# than FieldOps.Api's own (no other projects' .csproj files to restore
# separately first).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FieldOps.NotificationService/FieldOps.NotificationService.csproj src/FieldOps.NotificationService/
RUN dotnet restore src/FieldOps.NotificationService/FieldOps.NotificationService.csproj

COPY src/FieldOps.NotificationService/ src/FieldOps.NotificationService/
RUN dotnet publish src/FieldOps.NotificationService/FieldOps.NotificationService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FieldOps.NotificationService.dll"]
