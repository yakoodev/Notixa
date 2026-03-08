FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Notixa.Api/Notixa.Api.csproj", "Notixa.Api/"]
RUN dotnet restore "Notixa.Api/Notixa.Api.csproj"

COPY . .
WORKDIR "/src/Notixa.Api"
RUN dotnet publish "Notixa.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/app_data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Notixa.Api.dll"]
