# scripts/k3d-up.ps1 — локальный тестовый k8s-стенд (расширение №9 из PLAN.md §12).
#
# Что делает:
#   1. проверяет k3d/kubectl и создаёт одноузловой кластер, если его ещё нет;
#   2. готовит образы: тянет из GHCR (imagePullSecret) или импортирует локальные;
#   3. применяет манифесты k8s/overlays/dev; Secret создаётся из k8s/dev.env (вне git);
#   4. прогоняет миграции Job'ом и печатает адрес стенда.
#
# Оговорка среды: Docker Desktop должен быть запущен. На машине, где писался этот скрипт,
# Docker-демон из песочницы недоступен, поэтому на живом кластере скрипт не прогонялся:
# синтаксис проверен, шаги соответствуют документации k3d/kubectl.
#
# Примеры:
#   powershell -ExecutionPolicy Bypass -File scripts/k3d-up.ps1                    # образы из GHCR
#   powershell -ExecutionPolicy Bypass -File scripts/k3d-up.ps1 -LocalImages      # образы с этой машины
#   powershell -ExecutionPolicy Bypass -File scripts/k3d-up.ps1 -SkipCluster -SkipMigrate

[CmdletBinding()]
param(
    # Имя кластера k3d. Другое имя создаст второй кластер, а не переименует существующий.
    [string] $Cluster = 'pingboard',

    # Порт на хосте, на который смотрит ingress кластера. 80 даёт адрес http://pingboard.localhost
    # без порта: localhost-домены резолвятся в 127.0.0.1. Если 80 занят — поставьте, например, 8080.
    [int] $HttpPort = 80,

    # Только манифесты, кластер не создавать (он уже есть).
    [switch] $SkipCluster,

    # Не создавать Secret и не прогонять миграции.
    [switch] $SkipMigrate,

    # Загрузить в кластер образы, собранные локально (k3d image import), вместо pull из GHCR.
    # Теги должны совпадать с секцией images в k8s/overlays/dev/kustomization.yaml.
    [switch] $LocalImages,

    # Имена локальных образов для импорта. По умолчанию — то, что собирает docker compose.
    [string[]] $Images = @('pingboard-api:local', 'pingboard-web:local')
)

$ErrorActionPreference = 'Continue'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Assert-Command {
    param([string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Не найдена команда '$Name'. Установка: winget install k3d / winget install Kubernetes.kubectl"
    }
}

Assert-Command k3d
Assert-Command kubectl

$overlay = Join-Path $root 'k8s/overlays/dev'
if (-not (Test-Path $overlay)) {
    throw "Нет оверлея $overlay — сначала создайте манифесты k8s (DEPLOY §5.10)."
}

if (-not $SkipCluster) {
    $existing = (k3d cluster list --output json 2>$null | ConvertFrom-Json | Where-Object { $_.name -eq $Cluster })
    if ($existing) {
        Write-Host "Кластер '$Cluster' уже есть — переиспользую." -ForegroundColor Yellow
    }
    else {
        Write-Host "Создаю кластер '$Cluster' (ingress на localhost:$HttpPort)..." -ForegroundColor Cyan
        # Два kubelet-аргумента — про ротацию логов: Api и воркер пишут JSON в stdout постоянно,
        # а без ротации эти файлы на ноде растут до заполнения диска. В docker compose это
        # делает демон, здесь — обязанность настройки кластера (фактор XI).
        k3d cluster create $Cluster `
            --port "$($HttpPort):80@loadbalancer" `
            --k3s-arg '--kubelet-arg=container-log-max-size=10Mi@server:*' `
            --k3s-arg '--kubelet-arg=container-log-max-files=3@server:*' `
            --wait
        if ($LASTEXITCODE -ne 0) { throw "k3d cluster create завершился с кодом $LASTEXITCODE" }
    }

    kubectl config use-context "k3d-$Cluster"
}

# Образы: либо из GHCR (приватный пакет требует docker-registry secret), либо локальные,
# загруженные в кластер. Второе — быстрый путь для отладки манифестов без CI.
Write-Host '== Образы' -ForegroundColor Cyan
if ($LocalImages) {
    foreach ($image in $Images) {
        Write-Host "k3d image import $image" -ForegroundColor DarkGray
        k3d image import $image -c $Cluster
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Не удалось импортировать $image — соберите его: docker compose -f deploy/docker-compose.yml build"
        }
    }
}

# Секреты приложения: k8s/dev.env (не в git), создаётся руками — как .env для compose.
$secretFile = Join-Path $root 'k8s/dev.env'
if (-not $SkipMigrate) {
    if (Test-Path $secretFile) {
        Write-Host '== Secret pingboard-secrets из k8s/dev.env' -ForegroundColor Cyan
        kubectl -n pingboard create secret generic pingboard-secrets `
            --from-env-file=$secretFile --dry-run=client -o yaml | kubectl apply -f -
    }
    else {
        Write-Warning "Нет $secretFile — Secret pingboard-secrets не создан. Образец: k8s/dev.env.example"
    }
}

Write-Host '== Применяю манифесты' -ForegroundColor Cyan
# Тег образа живёт в k8s/overlays/dev/kustomization.yaml (секция images) — штатный способ
# kustomize, а не sed по манифестам: правка текста задела бы и другие поля с тем же именем.
kubectl apply -k $overlay
if ($LASTEXITCODE -ne 0) { throw "kubectl apply завершился с кодом $LASTEXITCODE" }

# imagePullSecret создаётся после apply: до него в кластере нет namespace pingboard.
# Ссылку imagePullSecrets в манифестах держим пустой: для локальных образов (k3d image import)
# она не нужна, а жёстко прописанный Secret ломал бы pull из публичного реестра.
if ((-not $LocalImages) -and $env:GHCR_USER -and $env:GHCR_TOKEN) {
    Write-Host '== imagePullSecret для GHCR' -ForegroundColor Cyan
    kubectl -n pingboard create secret docker-registry ghcr-pull `
        --docker-server=ghcr.io `
        --docker-username=$env:GHCR_USER `
        --docker-password=$env:GHCR_TOKEN `
        --dry-run=client -o yaml | kubectl apply -f -
    Write-Host 'Секрет ghcr-pull создан. Добавьте его в манифесты: imagePullSecrets: [{name: ghcr-pull}]' -ForegroundColor DarkGray
}
elseif (-not $LocalImages) {
    Write-Warning 'GHCR_USER/GHCR_TOKEN не заданы: pull приватного образа не сработает. Либо задайте их, либо запустите с -LocalImages.'
}

Write-Host '== Жду готовности Postgres' -ForegroundColor Cyan
kubectl -n pingboard rollout status statefulset/postgres --timeout=180s

if (-not $SkipMigrate) {
    # Порядок «сначала миграции, потом Api/Worker» — тот же, что в prod-ветке compose
    # ($DC --profile migrate run --rm migrate). Job одноразовый, поэтому его нет
    # в base/kustomization.yaml: иначе apply пересоздавал бы его на каждом запуске.
    Write-Host '== Миграции (Job migrate)' -ForegroundColor Cyan
    kubectl -n pingboard delete job migrate --ignore-not-found
    kubectl apply -f k8s/base/60-migrate-job.yaml
    kubectl -n pingboard wait --for=condition=complete job/migrate --timeout=300s
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Job migrate не завершился: kubectl -n pingboard logs job/migrate'
    }
}

Write-Host '== Жду готовности Api' -ForegroundColor Cyan
kubectl -n pingboard rollout status deployment/api --timeout=180s

$url = if ($HttpPort -eq 80) { 'http://pingboard.localhost' } else { "http://pingboard.localhost:$HttpPort" }
Write-Host ''
Write-Host "Стенд: $url" -ForegroundColor Green
Write-Host 'Если домен не резолвится: curl -H "Host: pingboard.localhost" http://localhost/' -ForegroundColor DarkGray
Write-Host 'Логи:  kubectl -n pingboard logs -f deployment/api   |   kubectl -n pingboard logs -f deployment/worker' -ForegroundColor DarkGray
