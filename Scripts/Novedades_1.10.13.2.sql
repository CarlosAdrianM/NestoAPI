-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.13.2
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.13.2). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.13.2.
-- Se omiten a propósito (el usuario no las percibe o son internas):
--  - Verifactu Fase B (#35 QR, #36 y #87 rectificativas, #325 facturas simplificadas): sigue
--    todo en fase de SOMBRA contra el entorno de pruebas de la AEAT, sin efecto fiscal ni QR
--    impreso. Se contará cuando entre en producción (dic/26).
--  - Remesas por API (Nesto#340 1C.14, slices 2 y 3) y refactor del cálculo de código de
--    barras por agencia (#258 b.1): internos, sin cambio visible.
--  - ELMAH ya no pierde los errores ocurridos dentro de una transacción (#182) y el
--    diagnóstico de bloqueos deja rastro cuando no identifica al bloqueador (#321):
--    diagnóstico interno.
--  - Índice y tiempo de espera del trigger de contabilidad (#322): la parte que el usuario
--    percibe sí se cuenta abajo, el detalle técnico no.
--  - Tests resucitados del csproj (#313) y coste medido del script del índice: internos.

DECLARE @version VARCHAR(23) = '1.10.13.2';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Copiar el nº de pedido desde la pestaña de Incidentados',
 'En Agencias, pestaña Incidentados, ya se puede copiar solo el número de pedido: pulsa el botón derecho sobre la fila y elige "Copiar nº de pedido". Antes, Ctrl+C copiaba la fila entera (agencia, fecha, cliente, dirección...) y había que limpiarla a mano para pegarlo en otro sitio.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'El correo de presupuesto indica la fecha de entrega',
 'Los correos de presupuesto y pedido ya muestran la fecha de entrega. Si todas las líneas se entregan el mismo día aparece una vez al pie; si hay líneas con fechas distintas, cada una lleva la suya en una columna nueva. Así el cliente sabe cuándo recibirá el pedido sin tener que preguntar.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'El aviso de picking lleva el importe total y agrupa por pedido',
 'El correo que se manda cuando un pedido coge picking ahora destaca el total con IVA y lleva el nº de pedido y el cliente en el asunto, de modo que el importe se ve sin abrir el correo y Outlook agrupa todos los avisos del mismo pedido en una conversación. Además, al responder, la respuesta va al almacén.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'El packing list se lee mejor',
 'El título va centrado, el número de pedido deja de repetirse en cada bloque (salvo que el cliente lleve varios pedidos en el mismo envío) y el rótulo de productos pendientes ya no se queda solo al final de la página.', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Contabilizar pagos grandes ya no se queda sin tiempo',
 'Al contabilizar un pago con muchas líneas (por ejemplo el pago mensual de Amazon en Canales Externos), la operación se quedaba sin tiempo de espera y fallaba, pero solo a algunos usuarios: a otros les funcionaba en segundos con el mismo pago. Ya funciona igual para todos.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Los pagos de Canales Externos ya no dejan apuntes a medias',
 'Si la contabilización de un pago de Canales Externos fallaba, podían quedar apuntes sueltos en la contabilidad que había que borrar a mano. Ahora la operación es completa o no es: si falla, no deja rastro, y el error queda registrado para poder revisarlo.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Al unir pedidos ya se pregunta "¿de todas formas?" y no se miente con el resultado',
 'Al unir o ampliar pedidos con un descuento no autorizado no salía la pregunta de confirmación que sí aparece al crear un pedido normal, así que la operación fallaba sin más. Ahora pregunta igual que en el resto de casos. Además, el mensaje de "se han unido correctamente" salía siempre, incluso cuando la unión había fallado: ahora dice la verdad.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Unir pedidos aguanta cuando la base de datos va lenta',
 'Cuando la unión de pedidos tardaba mucho (por ejemplo con la oficina cargada), la operación caducaba y el mensaje no decía qué hacer. Ahora dispone de más tiempo y, si aun así caduca, el aviso explica el motivo y el siguiente paso.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'El código de barras del envío usa la agencia del propio envío',
 'En listas con envíos de varias agencias, el código de barras se calculaba con la agencia seleccionada en la ventana en vez de con la del envío, lo que podía dar un código incorrecto en envíos de Sending o ASM. Ya se usa siempre la agencia del envío.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Las ofertas sin regalo ya no se muestran como si lo tuvieran',
 'En el mantenimiento de ofertas combinadas, una oferta sin unidades de regalo (por ejemplo un pack de 14 unidades a un importe mínimo) aparecía en la lista como si fuera un "13+1". Las columnas de unidades cobradas y regaladas ahora solo se muestran en las ofertas que realmente regalan la referencia de menor importe.', 'Nesto', SUSER_SNAME());
