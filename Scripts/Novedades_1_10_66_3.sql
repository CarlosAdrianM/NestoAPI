/*
    Novedades de Nesto 1.10.66.3 (24/11/2026, revisión): Black Friday (aviso «solo en stock» y el mejor descuento), Kinetics,
    abonos deshechos y el texto del aviso de permisos de Devueltos.
    Orden: API → Nesto (misma versión 1.10.66, sube solo la última cifra) → este script.
    Scripts de esta publicación (ya lanzados salvo los dos últimos): BlackFriday2026_CampanasCondicionStock.sql, Issue894_AlmacenesFicticios.sql,
    Issue903_ClientesFacturaElectronica.sql (pendiente) y el del almacén de cada vendedor (pendiente, en Documents\Issue894).
    Después de publicar la API: relanzar el alta en bloque del Black Friday (añade el aviso de stock) y probar un traspaso de una unidad con un almacén ficticio.
    Se omite a propósito: Facturae para FACe (NestoAPI#903: sin botón en Nesto todavía, Nesto#652), los almacenes ficticios (#894: sin pantalla,
    Nesto#651 y NestoApp#292), las muestras con etiqueta en el cajón (#884: va con Ariadna 2.44 y Nesto#650) y los endpoints de la hoja de muestras.
    Ejecutar contra NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @version VARCHAR(23) = '1.10.66.3';
DECLARE @fecha DATE = '2026-11-24';

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Nuevo', N'Black Friday (25 al 30 de noviembre): aviso si no hay stock suficiente',
       N'Los descuentos de Black Friday son solo para lo que haya en stock. Al guardar un pedido con un producto de la campaña del que no hay bastante entre Algete, Reina y Alcobendas (descontando lo que ya tienen pedido otros clientes), Nesto avisa de cuántas unidades hay. El aviso no bloquea ni quita el descuento: decide quien pasa el pedido. También sale en el correo del pedido.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Black Friday (25 al 30 de noviembre): aviso si no hay stock suficiente');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Mejorado', N'Precios: con dos descuentos del mismo producto, sale el mejor para el cliente',
       N'Cuando un producto tiene a la vez su descuento o precio de siempre y el de una campaña (como el Black Friday), Nesto aplica el más favorable para el cliente. Antes podía salir cualquiera de los dos.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Precios: con dos descuentos del mismo producto, sale el mejor para el cliente');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Mejorado', N'Kinetics: el cliente que ya lo ha comprado puede seguir comprándolo',
       N'La norma de que Kinetics no se vende a quien compra Faby o Greenik deja de aplicarse al cliente que ya tiene Kinetics en algún pedido: hecha la primera excepción, los siguientes pedidos ya no piden autorización por ese motivo.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Kinetics: el cliente que ya lo ha comprado puede seguir comprándolo');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Corregido', N'Abonos: deshacer un abono dentro del mismo pedido ya se declara bien, y no se puede deshacer dos veces',
       N'Al deshacer un abono copiando la rectificativa invertida dentro de un pedido de rectificativas, la factura nueva queda enlazada con la que corrige y se envía bien a Hacienda (antes se quedaba sin enviar). Además, si una rectificativa ya está corregida por otra, Nesto no deja deshacerla otra vez y dice cuál la corrige.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Abonos: deshacer un abono dentro del mismo pedido ya se declara bien, y no se puede deshacer dos veces');

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Corregido', N'Agencias › Devueltos: el aviso de «no tiene permiso» dice bien a quién pedírselo',
       N'Sobre un envío devuelto deciden quien hizo el pedido, su vendedor, Almacén o Dirección. El aviso decía por error que se pidiera a Administración.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Agencias › Devueltos: el aviso de «no tiene permiso» dice bien a quién pedírselo');

SELECT COUNT(*) AS NovedadesDeEstaVersion FROM dbo.Novedades WHERE Version = @version;
