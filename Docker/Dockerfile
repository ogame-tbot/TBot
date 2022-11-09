FROM ubuntu:20.04

LABEL maintainer="Crucio32000" \
    name="tbotogame" \
    version="0.2" \
    description="TBot image"

ENV CLEAN_DOCKER_CACHE=1
ENV DEBIAN_FRONTEND=noninteractive

# Basic packages
RUN apt-get update --fix-missing
RUN apt-get install -y git --fix-missing
RUN apt-get install -y wget --fix-missing

# DOT NET
# See https://docs.microsoft.com/it-it/dotnet/core/install/linux-scripted-manual 
# See https://github.com/dotnet/core/blob/main/Documentation/build-and-install-rhel6-prerequisites.md
# Script allows to install end-of-cycle versions
ARG DOTNET_VERSION=6.0.10
RUN apt-get install -y ca-certificates --fix-missing
RUN update-ca-certificates
RUN apt-get install -y libicu-dev libssl-dev --fix-missing
RUN wget https://dot.net/v1/dotnet-install.sh --no-check-certificate && chmod +x dotnet-install.sh
RUN ./dotnet-install.sh --runtime dotnet --version ${DOTNET_VERSION} --install-dir /dotnet
ENV DOTNET_ROOT /dotnet
ENV PATH "$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools"

# Additional packages
RUN apt-get install -y nano --fix-missing
RUN apt-get install -y screen --fix-missing

# Download TBOT and make it executable
ARG TBOT_RELEASE="v0.2.65"
ARG TBOT_PLATFORM="linuxarm"
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
CMD ["/TBot/TBot"]
