-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.21.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.21.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.21.0.
-- Se omiten a propósito (internas o sin efecto visible para el usuario):
--  - Gate de rectificativas (abono puro → serie RV automática) y auto-curado de
--    vinculaciones: el usuario factura igual, la serie sale sola.
--  - Retirada de los flags MotorPdf (Facturas/Extracto/PedidoCompra/Picking): todos los
--    usuarios ya iban por QuestPDF, el PDF sale idéntico.
--  - Modo degradado del certificado AEAT, GRANT de LinFacturaVtaRectificacion, endpoint
--    de etiquetas de tienda (piloto con flag), catálogo de parámetros editables: internos.
--  - Consolidación del CRUD de envíos (errores visibles y sin falsos éxitos): va en
--    "Corregido" con el caso de la etiqueta.

DECLARE @version VARCHAR(23) = '1.10.21.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Cambiarse el almacén de pedidos desde el menú Parámetros',
 'La ventana de Parámetros (menú de siempre) deja de ser solo de consulta: los usuarios de Tienda Online pueden cambiarse el almacén de pedidos (por ejemplo AMZ para facturar los FBA y ALG los días que cubren rutas). Al arrancar Nesto, si el almacén activo no es el titular, se ofrece volver a él para que el cambio temporal no se quede puesto por olvido.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Las líneas de inmovilizado preguntan por qué grupo comisionan',
 'Al guardar un pedido con una línea de inmovilizado, Nesto pregunta a qué grupo de producto comisiona (antes se guardaba sin grupo y no comisionaba, sin avisar). Se elige de una lista y el resto lo resuelve el servidor.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Remesas: forzar un efecto retenido por la entrega',
 'Un efecto retenido porque su envío no consta entregado ya se puede meter en la remesa: se marca la fila gris, se confirma el aviso y va al banco. Solo se pueden forzar las retenciones por entrega pendiente o incidencia; los envíos devueltos, los IBAN incorrectos y los estados bloqueados siguen sin poderse forzar.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Notas de entrega y pedidos: ponía "Nº Factura" sobre el número de pedido',
 'En el PDF de una nota de entrega o de un pedido, la cabecera decía "Nº Factura" aunque el número impreso era el del pedido. Ahora pone "Nº Pedido" (y "Nº Albarán" en los albaranes); las facturas siguen igual.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Remesas: el apunte del banco salía sin delegación ni forma de venta',
 'El apunte que va a la cuenta del banco al crear la remesa ahora lleva la delegación y la forma de venta (la mayoritaria entre los recibos de la remesa).', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Remesas: los efectos enviados por Correos Express nunca iban al banco',
 'Los envíos de agencias sin seguimiento automático (como Correos Express) no se marcan nunca como entregados, y eso retenía sus recibos indefinidamente: vencían y no se remesaban. Ahora la retención solo aplica a las agencias con seguimiento real (ASM, Innovatrans).', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Canales Externos: "Etiqueta creada" cuando en realidad había fallado',
 'Al crear la etiqueta de un pedido desde Canales Externos podía salir "Etiqueta creada" aunque el guardado hubiera fallado (y la etiqueta no aparecía en Agencias). El fallo del servidor se corrigió a mediodía y además el mensaje ya solo sale cuando la etiqueta se ha creado de verdad; si falla, se ve el motivo.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Vendedores → Clientes: cambiar solo el estado no se guardaba',
 'Si se cambiaba el estado del cliente sin cambiar también el vendedor, decía "guardado correctamente" pero el estado se quedaba como estaba. Ya se guarda siempre.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'NIF incorrectos: marcar "no censado" exige un NIF real',
 'La AEAT solo admite declarar como "no censado" un NIF bien formado (aunque no esté censado): marcar una ficha con un NIF de relleno daba error a diario. Ahora el botón lo comprueba y lo explica, y el cliente sigue saliendo en la ventana para corregirle el NIF cuando se consiga; al corregirlo, sus facturas pendientes se declaran solas.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Series VC y DV retiradas del selector',
 'Al crear un pedido ya no se ofrecen las series VC (no existía) ni DV (deja de usarse; sus abonos van por la serie de rectificativas). Los pedidos antiguos de esas series se siguen viendo con la marca "(serie histórica)".', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Correo de nuevo pedido: control de la financiación',
 'Cuando un pedido se financia por encima de lo permitido (efectos de menos de 150 € con una financiación media superior a 30 días), administración va en copia y el asunto queda marcado "[Financiación a revisar]". Además, al desmarcar servir junto o mantener junto, administración solo va en copia si los plazos generan más de un efecto (con contado o un solo plazo no hace falta).', 'NestoAPI', SUSER_SNAME());
