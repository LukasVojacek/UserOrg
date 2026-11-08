#!/usr/bin/env bash
set -euo pipefail

# Build + push BOTH images (API + Gateway) tagged $IMG_VERSION via the .NET SDK container target,
# which pushes straight to the registry (no Docker daemon / dind needed). IMG_VERSION is exported by
# the calling job (rc: <next>-rc.<count>, preview: <next>-preview.<iid>, primary: X.Y.Z).

: "${IMG_VERSION:?IMG_VERSION not set}"
# Runtime base for the produced image, set per track by the calling job: preview -> dotnet-preview-base
# (debug + vsdbg), rc / primary -> dotnet-prod-base (minimal). Both live in $CI_REGISTRY.
: "${APP_BASE_IMAGE:?APP_BASE_IMAGE not set}"

# The SDK uses these creds for BOTH the base-image pull (source) and the image push (destination);
# source and destination are the same $CI_REGISTRY, so one credential set covers both.
export DOTNET_CONTAINER_REGISTRY_UNAME="$CI_REGISTRY_USER"
export DOTNET_CONTAINER_REGISTRY_PWORD="$CI_JOB_TOKEN"

echo "Publishing API + Gateway images tagged: $IMG_VERSION (base: $APP_BASE_IMAGE)"

publish() { # <csproj> <container-repository>
  dotnet publish "$1" -c Release --os linux --arch x64 /t:PublishContainer \
    -p:Version="$IMG_VERSION" \
    -p:ContainerRegistry="$CI_REGISTRY" \
    -p:ContainerRepository="$2" \
    -p:ContainerTags="$IMG_VERSION" \
    -p:ContainerBaseImage="$APP_BASE_IMAGE"
}

publish ./src/Core/API/API.csproj "$CI_PROJECT_PATH"
publish ./src/Modules/Services/Gateway/UserOrg.Gateway/UserOrg.Gateway.csproj "$CI_PROJECT_PATH/userorg.gateway"

echo "Pushed: $CI_PROJECT_PATH:$IMG_VERSION and $CI_PROJECT_PATH/userorg.gateway:$IMG_VERSION"
