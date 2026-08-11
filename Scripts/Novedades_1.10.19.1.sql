-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.19.1
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.19.1). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.19.1.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - Filtro de escáneres en IIS/ELMAH y arreglo de la página /Help del API (#336): interno.
--  - Endpoints api/Clientes/PorNif y api/PedidosVenta/PorReferenciaCanal + pedidos
--    Prestashop sin EF (Nesto#340): interno, sin cambio funcional para el usuario.
--  - GestorFacturasRectificativas (Verifactu #37): sin efecto hasta integrarlo (#38).
--  - GRANTs de la ventana de códigos postales (#378): ya aplicados en BD el 10/08.
--  - ComprobarDatosGenerales devuelve 400 en vez de 500: el usuario ve el mismo aviso.

DECLARE @version VARCHAR(23) = '1.10.19.1';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Remesas: los clientes con movimientos negativos ya no bloquean la remesa',
 'Hasta ahora, si un cliente de la remesa tenía algún movimiento negativo pendiente (un pago a cuenta, un abono), era obligatorio liquidarlo para poder crear la remesa, aunque no tuviera nada que ver con los recibos que se giraban. Ahora el programa avisa y pregunta: se puede liquidar en el Extracto de Cliente (doble clic en el efecto naranja) o crear la remesa sin liquidarlo.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Remesas: el IBAN se comprueba ANTES de crear la remesa',
 'Si un cliente tenía el IBAN incorrecto o incompleto, la remesa se creaba igualmente y el error saltaba después, al generar el fichero para el banco, cuando ya era tarde. Ahora esos efectos aparecen retenidos (en gris, con el motivo) en la pantalla de crear remesa, para corregir la ficha bancaria antes. También se detectan los efectos cuyo código de cuenta no existe en la ficha del cliente, que antes se quedaban fuera del fichero sin ningún aviso.', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Error al crear un cliente extranjero cuyo código postal ya existía',
 'Al dar de alta un cliente extranjero, si el código postal llegaba con un espacio invisible delante (por ejemplo del autocompletado de direcciones) y ya existía uno igual, el alta fallaba con un error técnico de clave duplicada. Ahora se limpia y se reutiliza el código postal existente.', 'NestoAPI', SUSER_SNAME());
