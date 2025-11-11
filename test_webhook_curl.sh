#!/bin/bash
# Script Bash para probar el webhook localmente con curl
# Simula lo que Google Pub/Sub enviaría

ENDPOINT="http://localhost:53364/api/sync/webhook"

# Mensaje de prueba
MENSAJE='{
  "tabla": "Clientes",
  "accion": "actualizar",
  "datos": {
    "parent": {
      "cliente_externo": "12345",
      "contacto_externo": "001",
      "name": "Test Cliente Local",
      "mobile": "666123456",
      "street": "Calle Test 123",
      "city": "Madrid",
      "zip": "28001"
    }
  }
}'

echo "📝 Mensaje JSON:"
echo "$MENSAJE"
echo ""

# Codificar en base64 (como lo hace Google)
MENSAJE_BASE64=$(echo -n "$MENSAJE" | base64 -w 0)

echo "📦 Mensaje Base64:"
echo "$MENSAJE_BASE64"
echo ""

# Crear el request como lo envía Google Pub/Sub
PUBSUB_REQUEST=$(cat <<EOF
{
  "message": {
    "data": "$MENSAJE_BASE64",
    "messageId": "test-local-$(date +%s)",
    "publishTime": "$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
  },
  "subscription": "projects/test/subscriptions/test-local"
}
EOF
)

echo "📨 Request completo (formato Google Pub/Sub):"
echo "$PUBSUB_REQUEST"
echo ""

echo "========================================"
echo "🚀 Enviando a: $ENDPOINT"
echo "========================================"
echo ""

# Enviar al endpoint
curl -X POST "$ENDPOINT" \
  -H "Content-Type: application/json" \
  -d "$PUBSUB_REQUEST" \
  -v

echo ""
echo "========================================"
echo "✅ Verifica la consola de Visual Studio"
echo "========================================"
