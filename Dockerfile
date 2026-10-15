FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/SandwichOrders.Core/SandwichOrders.Core.csproj src/SandwichOrders.Core/
COPY src/SandwichOrders.Web/SandwichOrders.Web.csproj src/SandwichOrders.Web/
RUN dotnet restore src/SandwichOrders.Web/SandwichOrders.Web.csproj
COPY src/ src/
RUN dotnet publish src/SandwichOrders.Web/SandwichOrders.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SandwichOrders.Web.dll"]
