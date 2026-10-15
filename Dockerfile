# Two stages: the SDK image builds and publishes the app, the much smaller ASP.NET image only runs it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first with only the project files, so this layer is reused as long as the packages do not change.
COPY Directory.Build.props ./
COPY src/SandwichOrders.Core/SandwichOrders.Core.csproj src/SandwichOrders.Core/
COPY src/SandwichOrders.Web/SandwichOrders.Web.csproj src/SandwichOrders.Web/
RUN dotnet restore src/SandwichOrders.Web/SandwichOrders.Web.csproj
COPY src/ src/
RUN dotnet publish src/SandwichOrders.Web/SandwichOrders.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# The app runs as the image's non-root user "app". The photo folder is created here and given to that user;
# a Docker volume mounted on it starts with this owner, so uploads can be written.
RUN mkdir -p /data/uploads && chown app:app /data/uploads
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SandwichOrders.Web.dll"]
