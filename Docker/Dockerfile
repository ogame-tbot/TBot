FROM mcr.microsoft.com/dotnet/aspnet:6.0

LABEL maintainer="Crucio32000" \
    name="tbotogame" \
    version="0.3.4" \
    description="TBot image"

ENV CLEAN_DOCKER_CACHE=1
ENV DEBIAN_FRONTEND=noninteractive

# Additional packages
RUN apt-get update && apt-get install unzip wget -y

# Download TBOT and make it executable
ARG TBOT_RELEASE="v0.3.4"
ARG TBOT_PLATFORM="linux64"
RUN wget https://github.com/ogame-tbot/TBot/releases/download/${TBOT_RELEASE}/TBot-${TBOT_RELEASE}-${TBOT_PLATFORM}.zip && \
    unzip TBot-${TBOT_RELEASE}-${TBOT_PLATFORM}.zip
RUN mkdir -p TBot && mv publish/${TBOT_PLATFORM}/* /TBot/ && rm -rf publish
RUN chmod +x /TBot/ogamed
RUN chmod +x /TBot/TBot

# Add user and group so we don't mess with permissions
USER root
ARG UNAME=user
ARG UGROUP=user
ARG PASS=12345
ARG UID=1000
ARG GID=1000
RUN groupadd -g $GID $UGROUP
RUN useradd -m -u $UID -g $GID -o -s /bin/bash \
    -p $(openssl passwd -1 $PASS) $UNAME
RUN usermod -aG sudo $UNAME

USER $UNAME
ENTRYPOINT ["/TBot/TBot"]
