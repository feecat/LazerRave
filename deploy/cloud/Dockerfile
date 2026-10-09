FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --chown=app:app server/ ./
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cloud.dll"]
