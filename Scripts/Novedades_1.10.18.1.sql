-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.18.1
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.18.1). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.18.1.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - GRANTs de PlanesVentajas (#372): permisos de BD, infraestructura.
--  - Verifactu OSS con IDOtro tipo 04 (#375): lo percibe la AEAT, no el usuario
--    (dejarán de verse rechazos "censo VIES" en los avisos internos).
--  - Validación del CCC del contacto de cobro al facturar (#373): cambia un error
--    críptico por un mensaje claro, solo lo ve quien factura con datos rotos.
--  - Arreglo del resumen diario de rapports (#374): correo interno de dirección.
--  - Fix de compilación de tests de CanalesExternos (#434): interno.

DECLARE @version VARCHAR(23) = '1.10.18.1';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Canales Externos: los pedidos de Amazon Business se distinguen a simple vista',
 'En Canales Externos → Pedidos (Amazon), los pedidos de cliente empresarial (Amazon Business) aparecen ahora con un distintivo azul "EMPRESA" y la fila resaltada, para localizarlos de un vistazo. Además, el pedido de Nesto que se crea lleva la marca "PEDIDO AMAZON BUSINESS" en los comentarios.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Escanear un producto a medio dar de alta daba error',
 'Al buscar por código de barras un producto al que todavía le faltaban datos del alta (familia, subgrupo o PVP), la ficha daba un error genérico. Ahora la ficha se abre con los datos que haya.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'No se podían imprimir etiquetas de GLS de envíos con servicios antiguos',
 'Al imprimir la etiqueta de un envío de GLS cuyo servicio ya no está en la lista actual, saltaba el error "Sequence contains no matching element" y la etiqueta no salía. Ahora se imprime siempre; si el nombre del servicio no se conoce, se imprime su código.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Crear un enlace de pago fallaba al cambiar el importe o con abonos',
 'Al crear un enlace de pago desde la ficha del cliente, si se cambiaba el importe a mano (o la selección de recibos incluía abonos y la suma no cuadraba), daba el error "la suma de los efectos no coincide con el importe total". Ahora el enlace se crea por el importe indicado; los recibos solo se asocian cuando cuadran con él.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Error al validar un pedido con una línea a cantidad 0',
 'Un pedido con una línea a cantidad 0 (por ejemplo, una línea vaciada) daba un error genérico al validar. Ahora se trata como línea sin importe y la validación sigue.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Geolocalizar una dirección sin resultados daba un error genérico',
 'Al tramitar un envío, si Google no encontraba la dirección (o la devolvía incompleta), saltaba un error técnico sin explicación. Ahora el mensaje indica la dirección que no se ha podido geolocalizar para poder corregirla.', 'Nesto', SUSER_SNAME());
