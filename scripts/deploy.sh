#!/bin/bash
# Reporting API - Deploy Script
# Run this to deploy updates

set -e

echo "=========================================="
echo "  Reporting API - Deploy"
echo "=========================================="

DEPLOY_DIR="/opt/FacilReports"

# Check if we're in the right directory
if [ ! -f "docker-compose.yml" ]; then
    echo "Error: docker-compose.yml not found"
    echo "Run this script from the project root"
    exit 1
fi

echo "[1/4] Pulling latest changes..."
git pull origin main

echo "[2/4] Building new Docker image..."
docker compose build --no-cache

echo "[3/4] Stopping current container..."
docker compose down

echo "[4/4] Starting new container..."
docker compose up -d

echo ""
echo "=========================================="
echo "  Deploy Complete!"
echo "=========================================="

# Wait for health check
sleep 5

echo "Checking health..."
if curl -sf http://localhost:5000/health > /dev/null; then
    echo "✓ API is healthy!"
else
    echo "⚠ API might still be starting up..."
fi

echo ""
echo "Container status:"
docker compose ps
