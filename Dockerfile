FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PaymentService/PaymentService.csproj PaymentService/
RUN dotnet restore PaymentService/PaymentService.csproj

COPY PaymentService/ PaymentService/
WORKDIR /src/PaymentService
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV PROVIDER_URL=http://provider-simulator:8081

EXPOSE 8080

ENTRYPOINT ["dotnet", "PaymentService.dll"]
