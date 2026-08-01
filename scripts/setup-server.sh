#!/bin/bash
# Reporting API - Server Setup Script
# Run this on a fresh Ubuntu 22.04/24.04 droplet

set -e

echo "=========================================="
echo "  Reporting API - Server Setup"
echo "=========================================="

# Update system
echo "[1/7] Updating system..."
sudo apt-get update && sudo apt-get upgrade -y

# Install Docker
echo "[2/7] Installing Docker..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com | sudo sh
    sudo usermod -aG docker $USER
    echo "Docker installed. You may need to log out and back in."
else
    echo "Docker already installed."
fi

# Install Docker Compose plugin
echo "[3/7] Installing Docker Compose..."
sudo apt-get install -y docker-compose-plugin

# Install Nginx
echo "[4/7] Installing Nginx..."
sudo apt-get install -y nginx

# Install Certbot for SSL
echo "[5/7] Installing Certbot..."
sudo apt-get install -y certbot python3-certbot-nginx

# Configure firewall
echo "[6/7] Configuring firewall..."
sudo ufw allow 'Nginx Full'
sudo ufw allow OpenSSH
sudo ufw --force enable

# Create project directory
echo "[7/7] Setting up project directory..."
sudo mkdir -p /opt/FacilReports
sudo chown $USER:$USER /opt/FacilReports

echo ""
echo "=========================================="
echo "  Setup Complete!"
echo "=========================================="
echo ""
echo "Next steps:"
echo "1. Clone your repository to /opt/FacilReports"
echo "2. Configure .env file with your API keys"
echo "3. Run: docker compose up -d"
echo "4. Configure Nginx (see nginx/default.conf)"
echo "5. Run: sudo certbot --nginx -d reports.facil-apps.online"
echo ""
echo "Important: Log out and back in for Docker group changes to take effect."
