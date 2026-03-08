FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TelegramNotifications.Api/TelegramNotifications.Api.csproj", "TelegramNotifications.Api/"]
RUN dotnet restore "TelegramNotifications.Api/TelegramNotifications.Api.csproj"

COPY . .
WORKDIR "/src/TelegramNotifications.Api"
RUN dotnet publish "TelegramNotifications.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/app_data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TelegramNotifications.Api.dll"]
