FROM mcr.microsoft.com/dotnet/aspnet:7.0

#RUN apt-get update && apt-get install -y apt-utils libgdiplus libc6-dev
#RUN groupadd -g 1003 urnikuser
#RUN useradd -g 1003 -u 1003 -m -s /bin/bash urnikuser
#USER urnikuser

ARG source 
WORKDIR /app

#RUN sed -i 's/DEFAULT@SECLEVEL=2/DEFAULT@SECLEVEL=1/g' /etc/ssl/openssl.cnf

#RUN sed -i 's/DEFAULT@SECLEVEL=2/DEFAULT@SECLEVEL=1/g' /usr/lib/ssl/openssl.cnf

# Copy the JSON configuration file into the container
#COPY urniknet-d95a3-0d3e477b7e13.json /app/google.json

EXPOSE 80
COPY ${source:-obj/Docker/publish} . 
ENTRYPOINT ["dotnet", "soniox_test_2.dll"]