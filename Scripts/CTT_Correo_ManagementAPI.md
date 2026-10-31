# Correo a CTT (Integraciones de cliente): dudas sobre la Management API v1.5.2

**Para**: contacto de integraciones de CTT Express (en el PDF firma Héctor Furió, Client Integrations)
**Asunto**: Nueva Visión (centro 8090300001) – Management API: logística inversa, parar en vuelo y entorno de pruebas

Buenos días:

Somos Nueva Visión, S.A. (código de centro 8090300001). Tenemos integrado el manifiesto (v2.0), la anulación, las etiquetas y el seguimiento, y queremos integrar ahora la Management API (documento «Managements_API_v1.5.2»). El PDF describe los endpoints y los estados en que se admiten, pero no el cuerpo de algunas peticiones ni lo que devuelven. Les agradeceríamos que nos aclarasen:

1. **Logística inversa** (`rpc-create-reverse-shipping`, envíos entregados):
   - ¿Qué cuerpo lleva la petición? (dirección de recogida, fecha o franja, número de bultos, peso, referencia nuestra…).
   - ¿Devuelve un código de envío nuevo para el retorno? ¿Hay etiqueta (PDF/ZPL) que el cliente deba pegar, o la lleva el repartidor?
   - Nuestro caso habitual: el cliente abre el paquete, se queda con parte y nos devuelve el resto. ¿Puede devolverse en una caja distinta con una etiqueta nueva impresa por el cliente en PDF?
   - ¿Hasta cuántos días después de la entrega se admite?
2. **Parar en vuelo** (`rpc-stop-on-the-fly`): ¿qué cuerpo lleva y qué ocurre con el envío (vuelve a origen, queda retenido en delegación)? ¿Tiene coste?
3. **Cambio de dirección y de fecha** (`rpc-change-delivery-address` / `rpc-change-delivery-options`): confirmar que el envío conserva el mismo código y que el reembolso y los adicionales (RET) se mantienen.
4. ¿Hay alguna operación para **modificar el reembolso o añadir el adicional de retorno** a un envío ya recogido? Entendemos por el documento que no; confírmennoslo.
5. **Entorno de pruebas** (`api-test.cttexpress.com`): credenciales y código de centro de pruebas para validar estas operaciones antes de pasar a producción.

Muchas gracias.

Carlos Adrián Martínez
Nueva Visión, S.A.
