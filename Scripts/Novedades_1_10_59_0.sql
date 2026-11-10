/*
    Novedades de la versión 1.10.59.0 de Nesto (10/11/2026).
    NestoAPI#828 (factura física en el packing, cortes S1-S6 y revisión), prueba de entrega de cualquier agencia (reparto propio, CTT y
    GLS), anular un envío tramitado del reparto propio y aviso de 0 € con «Cobrar» (sugerencia 1162), quitar el picking por líneas
    (comentarios 967 y 974 de Alfredo en la novedad 1146), aviso del picking sin nada que sacar (sugerencia 1164), cheque regalo en un
    pedido solo de cursos (sugerencia 1166), logo de Nueva Visión en la etiqueta RP, hojas de ubicaciones del inventario como el informe
    antiguo, el N+M de la tienda como regalo, la validación de los pedidos de la tienda con las ofertas del día del pedido y la hora de
    los leads de Meta importados por fichero.

    ORDEN:
      1) Scripts sa: ninguno pendiente. Ya lanzados por Carlos el 10/11: Issue828_DocumentosPapelPacking.sql,
         Issue828b_DocumentosPapelPacking_CartaCheque.sql, Issue_EmbudoOdooNV_ClientesEmbudo.sql (dbo.ClientesEmbudo) e
         Issue590_VendedorClientes_BloqueoNestoViejo.sql.
      2) Publicar la API (master) → ClickOnce (Clean + Rebuild) → 1.10.59.0 → Ariadna 2.33.
      3) DESPUÉS de publicar la API y la ClickOnce: ESTE script, Novedades_Ariadna_2_33.sql y el PUT de las sugerencias implementadas.
      NO duplicar (la sugerencia ES la novedad; van por PUT api/Novedades/Sugerencias/{id} con Titulo, Descripcion y Version, y aquí no
      llevan fila): 973 (Alfredo, la factura física sale en el packing; incluye Facturar rutas), 1162 (Alfredo, anular un envío
      tramitado del reparto propio y el aviso de 0 € con «Cobrar» marcado), 1164 (Alfredo, «No hay ningún pedido que pueda salir
      ahora») y 1166 (Pilar, cheque regalo en un pedido de cursos). La 1165 (Miguel, Maps en el móvil) va con Ariadna 2.33.
      La de quitar el picking por líneas SÍ lleva fila: lo pidió Alfredo en comentarios (967, 974) de la novedad 1146, no en una sugerencia.
      Se omiten a propósito (nada perceptible para el usuario o va por otro lado): el embudo de clientes hacia Odoo (Republicar y
      Sembrar), el modelo de llamadas por subgrupo encendido y su reentreno semanal de los sábados de madrugada, los endurecimientos de
      seguridad (enlace del justificante solo http/https, el de GLS solo de gls-spain.es y con tope de tamaño), que la etiqueta de la
      ruta propia siga facturando e imprimiendo sus copias como antes del #828, el 409 de #847 también al registrar un pendiente
      (se ofrece reimprimir el envío que ya tiene el pedido, como en «Insertar e Imprimir») y la carta del cheque regalo (solo Ariadna).

    Ejecutar en SSMS contra NV como sa. Idempotente (compara por versión y título). Los literales van con N'...'.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.59.0';
DECLARE @fecha DATE = '2026-11-10';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Alfredo y Pilar',
     N'Esta versión sale en buena parte de lo que nos han contado por «Novedades»: la factura en papel que ahora sale al hacer la etiqueta, poder anular un envío del reparto propio ya impreso, quitar el picking de una sola línea y el aviso del picking que parecía un error (Alfredo), y el cheque regalo en un pedido de cursos (Pilar). Muchas gracias: sigan contándonos.',
     NULL),
    ('Nuevo', N'Ver la prueba de entrega de cualquier agencia',
     N'En Agencias › Tramitados, el botón «Ver prueba de entrega» sirve ya para cualquier envío entregado, no solo para los del reparto propio: con CTT sale el justificante de entrega (PDF), con GLS el albarán firmado y con nuestro reparto la firma del cliente. Además, en el detalle del pedido, cada envío entregado de la lista tiene su botón «Ver prueba». Se ve quién lo recibió, su DNI si la agencia lo da y la fecha. Si la agencia no tiene la prueba, Nesto dice por qué. Lo pueden ver Almacén, Administración y Dirección.',
     N'Almacén,Administración,Dirección'),
    ('Mejorado', N'Picking: «Quitar picking» deja elegir las líneas',
     N'Al quitar el picking de un pedido con varias líneas, Nesto enseña las líneas con picking con una casilla cada una: con todas marcadas se quita el del pedido entero, como hasta ahora; si desmarca alguna, solo se quita el de las marcadas (por ejemplo, solo los portes) y el resto sigue en el picking. Las que no se pueden quitar salen desmarcadas y con el motivo. Si el mozo ya ha leído algo de esas líneas, la PDA recibe el aviso de apartarlo. Y si el picking ya está terminado en la PDA, el mensaje ahora lo dice claro: «Desde Nesto todavía no se puede quitar el picking de lo ya cogido.».',
     N'Almacén,Dirección'),
    ('Mejorado', N'Inventario: las hojas de ubicaciones vuelven a ser una por ubicación',
     N'Informes › Inventarios › «Inventario» vuelve a sacar las hojas como el informe antiguo: una hoja A4 por ubicación, con el rótulo «Pasillo X. Fila Y. Columna Z», hueco para apuntar lo contado y, al pie, «Contado por» y «Contabilizado por», en el orden del recorrido. Son unas 700 hojas para Algete.',
     N'Almacén,Dirección'),
    ('Corregido', N'Reparto propio: la etiqueta lleva el logo de Nueva Visión',
     N'Las etiquetas del reparto propio (RP) y las de recogida salían con el logo de la agencia Sending. Ahora llevan el de Nueva Visión.',
     N'Almacén'),
    ('Mejorado', N'Tienda online: las ofertas N+M entran ya con su unidad de regalo',
     N'Cuando un pedido de la tienda online trae una oferta como el 6+1, al pasarlo a Nesto la unidad de regalo entra ya como línea de regalo (mismo producto, a precio 0) junto a las que se pagan, igual que si se hubiera hecho en la plantilla, en vez de una línea de descuento que había que retocar a mano. Si la oferta no se puede leer con seguridad, entra como hasta ahora.',
     N'Administración,Dirección'),
    ('Corregido', N'Tienda online: los pedidos se validan con las ofertas del día del pedido',
     N'Un pedido de la tienda online hecho durante una campaña ya no da error de ofertas o descuentos no permitidos si se pasa o se modifica después de que la campaña haya acabado: se comprueba con las ofertas y descuentos que estaban en vigor el día en que el cliente hizo el pedido (hasta 15 días atrás). Los demás pedidos, como siempre, con las de hoy.',
     N'Administración,Dirección'),
    ('Corregido', N'Clientes: la hora de los leads de Meta importados por fichero',
     N'Al «Importar leads de Meta» desde el Excel que descarga Meta, la hora del lead quedaba una hora adelantada. Ahora se guarda la hora de España.',
     N'Dirección');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
SELECT Id, Categoria, Titulo FROM dbo.Novedades WHERE Version = @version ORDER BY Id;
