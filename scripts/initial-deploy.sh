#!/bin/bash
# Reporting API - Initial Server Deployment
# Run this after setup-server.sh

set -e

echo "=========================================="
echo "  Reporting API - Initial Deploy"
echo "=========================================="

DEPLOY_DIR="/opt/FacilReports"
REPO_URL="https://github.com/facil-apps-online/Facil-Reports.git"

# Clone repository
echo "[1/6] Cloning repository..."
if [ -d "$DEPLOY_DIR/.git" ]; then
    echo "Repository already cloned. Pulling latest..."
    cd $DEPLOY_DIR
    git pull origin main
else
    sudo rm -rf $DEPLOY_DIR
    sudo git clone $REPO_URL $DEPLOY_DIR
    sudo chown -R $USER:$USER $DEPLOY_DIR
    cd $DEPLOY_DIR
fi

# Configure environment
echo "[2/6] Configuring environment..."
if [ ! -f ".env" ]; then
    cp .env.example .env
    echo ""
    echo "⚠ Please edit .env file with your actual API keys"
    echo "Press Enter when done..."
    read
fi

# Build and start
echo "[3/6] Building Docker image..."
docker compose build --no-cache

echo "[4/6] Starting container..."
docker compose up -d

# Configure Nginx
echo "[5/6] Configuring Nginx..."
sudo cp nginx/default.conf /etc/nginx/sites-available/facilreports
sudo ln -sf /etc/nginx/sites-available/facilreports /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx

# SSL
echo "[6/6] Setting up SSL..."
echo "Make sure DNS A record points to this server's IP"
echo "Press Enter to continue with Certbot..."
read
sudo certbot --nginx -d reports.facil-apps.online -d reports.facil-apps.com --non-interactive --agree-tos -m your-email@domain.com

echo ""
echo "=========================================="
echo "  Deployment Complete!"
echo "=========================================="
echo ""
echo "API URL: https://reports.facil-apps.online"
echo "Health: https://reports.facil-apps.online/health"
echo ""
echo "Test with:"
echo "curl https://reports.facil-apps.online/health"
