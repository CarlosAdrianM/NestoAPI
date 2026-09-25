-- NestoAPI#538: limpiar las cabeceras de pedido vacías (sin cliente, contacto o plazos de pago, y sin
-- ninguna línea).
--
-- De dónde salen (25/09/26): del Nesto viejo (VB6). Al pulsar «Nuevo pedido» inserta la cabecera
-- enseguida, para reservar el número, con solo Empresa, Número, Fecha (sin hora), Serie, Origen,
-- Usuario y a veces Contacto en blanco; el resto lo rellena al guardar. Si el usuario cierra sin
-- guardar, o vuelve a pulsar «Nuevo», la cabecera se queda así para siempre. Se ve en la BD:
--   · ModoServicio NULL y sin Primer Vencimiento: el POST de la API siempre los rellena, y además
--     rechaza un pedido sin cliente, sin plazos de pago o sin líneas.
--   · Parejas del mismo usuario a segundos de distancia (926953/926954, 907566-907571), y justo
--     después un pedido bueno del mismo usuario también con ModoServicio NULL, es decir, también del
--     Nesto viejo (927071 vacío a las 09:15:34 y 927072 con su línea a las 09:15:58, reina\Laura).
--   · El Nesto nuevo (WPF) ya no tiene EF ni ADO: solo crea pedidos con POST api/PedidosVenta.
-- Mientras se siga usando el Nesto viejo seguirán apareciendo (unas 70 al año), así que este script
-- se puede volver a lanzar cuando haga falta.
--
-- Qué se borra: cabeceras de CabPedidoVta
--   1. SIN NINGUNA línea en LinPedidoVta,
--   2. sin cliente, sin contacto o sin plazos de pago (las del Nesto viejo; las que tienen todos los
--      datos y se han quedado sin líneas por otros motivos —unir pedidos, borrar las líneas a mano—
--      NO se tocan: eso está pendiente de decidir en la issue),
--   3. con la fecha y la última modificación de hace más de @Dias días (nadie las está editando),
--   4. y que no tengan NADA colgando: efectos, prepagos, vendedores por grupo, envíos de agencia,
--      movimientos de stock, ubicaciones, pedidos especiales, notas de entrega, alquileres, comisiones,
--      rectificativas pendientes, notificaciones, Modificaciones, etc. (ver la lista de abajo).
--
-- Cómo lanzarlo: como sa en SSMS, mejor fuera de horario (recorre LinPedidoVta y ExtractoProducto
-- por número de pedido). Con @Borrar = 0 (por defecto) solo enseña lo que borraría y deshace el
-- DELETE; con @Borrar = 1 lo confirma. Recuento del 25/09/26: 5.849 cabeceras (70 de 2026), de las
-- que solo 2 tienen algo colgando (movimientos en ExtractoProducto) y se quedan.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Dias int = 7;
DECLARE @Borrar bit = 0;   -- 0 = solo ver (ROLLBACK); 1 = borrar (COMMIT)

DECLARE @Limite datetime = DATEADD(DAY, -@Dias, GETDATE());

-------------------------------------------------------------------------------------------------
-- 1. Candidatas
-------------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Candidatas') IS NOT NULL DROP TABLE #Candidatas;

SELECT c.Empresa, c.Número, c.Fecha, c.[Fecha Modificación] AS FechaModificacion, c.Usuario, c.Serie,
       c.[Nº Cliente] AS Cliente, c.Contacto, c.PlazosPago
INTO #Candidatas
FROM dbo.CabPedidoVta c WITH (NOLOCK)
WHERE (NULLIF(LTRIM(RTRIM(c.[Nº Cliente])), '') IS NULL
       OR NULLIF(LTRIM(RTRIM(c.Contacto)), '') IS NULL
       OR c.PlazosPago IS NULL)
  AND c.[Fecha Modificación] < @Limite
  AND (c.Fecha IS NULL OR c.Fecha < @Limite)
  AND NOT EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número);

CREATE UNIQUE CLUSTERED INDEX IX_Candidatas ON #Candidatas (Empresa, Número);

DECLARE @Encontradas int = (SELECT COUNT(*) FROM #Candidatas);

-------------------------------------------------------------------------------------------------
-- 2. Fuera las que tienen algo colgando. Las tablas sin empresa se cruzan solo por número (los números
--    de pedido son globales, de ContadoresGlobales): así, ante la duda, la cabecera se queda.
-------------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#ConAlgoColgando') IS NOT NULL DROP TABLE #ConAlgoColgando;
CREATE TABLE #ConAlgoColgando (Empresa char(3) NOT NULL, Número int NOT NULL, Motivo varchar(50) NOT NULL);

-- Con FK a CabPedidoVta (ON DELETE CASCADE: se irían con la cabecera, por eso se miran antes)
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'VendedoresPedidoGrupoProducto' FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.VendedoresPedidoGrupoProducto x WITH (NOLOCK) WHERE x.Empresa = k.Empresa AND x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'EfectosPedidoVenta'            FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.EfectosPedidoVenta x WITH (NOLOCK) WHERE x.Empresa = k.Empresa AND x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'Prepagos'                      FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.Prepagos x WITH (NOLOCK) WHERE x.Empresa = k.Empresa AND x.Pedido = k.Número);
-- Sin FK, pero apuntan al pedido
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'EnviosAgencia'                 FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.EnviosAgencia x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'ExtractoProducto'              FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.ExtractoProducto x WITH (NOLOCK) WHERE x.[NºPedido] = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'PreExtrProducto'               FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.PreExtrProducto x WITH (NOLOCK) WHERE x.[NºPedido] = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'Ubicaciones'                   FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.Ubicaciones x WITH (NOLOCK) WHERE x.PedidoVta = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'PedidosEspeciales'             FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.PedidosEspeciales x WITH (NOLOCK) WHERE x.[NºPedidoVta] = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'FacturasBorradas'              FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.FacturasBorradas x WITH (NOLOCK) WHERE x.[NºPEDIDO] = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'FormasPagoPendientes'          FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.FormasPagoPendientes x WITH (NOLOCK) WHERE x.PedidoOrigen = k.Número OR x.PedidoDestino = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'NotasEntregaMantenerJunto'     FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.NotasEntregaMantenerJunto x WITH (NOLOCK) WHERE x.NumeroPedido = k.Número OR x.PedidoNota = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'CabAlquileres'                 FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.CabAlquileres x WITH (NOLOCK) WHERE x.CabPedidoVta = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'VendedorLinPedidoVta'          FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.VendedorLinPedidoVta x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'ComisionesAnualesDetalle'      FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.ComisionesAnualesDetalle x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'CajaCongreso'                  FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.CajaCongreso x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'AmazonFacturasSubidas'         FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.AmazonFacturasSubidas x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'ComparativaAgenciaSombra'      FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.ComparativaAgenciaSombra x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'Leasing'                       FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.Leasing x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'NotificacionesEstadoPedido'    FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.NotificacionesEstadoPedido x WITH (NOLOCK) WHERE x.Pedido = k.Número);
INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'RectificativaPendiente'        FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM dbo.RectificativaPendiente x WITH (NOLOCK) WHERE x.NumeroPedido = k.Número);

-- Nota de entrega automática (#542): otra cabecera que dice venir de esta. Dinámico porque la columna
-- PedidoOrigen solo existe después de Issue542_ModoFacturacion.sql.
IF COL_LENGTH('dbo.CabPedidoVta', 'PedidoOrigen') IS NOT NULL
    EXEC (N'INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, ''CabPedidoVta.PedidoOrigen'' FROM #Candidatas k
            WHERE EXISTS (SELECT 1 FROM dbo.CabPedidoVta x WITH (NOLOCK) WHERE x.Empresa = k.Empresa AND x.PedidoOrigen = k.Número);');

-- Modificaciones: el JSON de cada guardado del pedido (Tabla «Pedidos»: ..."numero":926262,...) y los
-- cambios de cabecera (Tabla «CabPedidoVta»: «Pedido 926964 Serie=NV»). Se sacan los números una vez.
IF OBJECT_ID('tempdb..#NumerosEnModificaciones') IS NOT NULL DROP TABLE #NumerosEnModificaciones;
SELECT DISTINCT TRY_CAST(SUBSTRING(t.Texto, p.Pos, PATINDEX('%[^0-9]%', SUBSTRING(t.Texto, p.Pos, 20) + 'x') - 1) AS int) AS Numero
INTO #NumerosEnModificaciones
FROM dbo.Modificaciones m WITH (NOLOCK)
CROSS APPLY (VALUES (m.Anterior), (m.Nuevo)) t(Texto)
CROSS APPLY (SELECT CASE WHEN CHARINDEX('"numero":', t.Texto) > 0 THEN CHARINDEX('"numero":', t.Texto) + 9
                         WHEN t.Texto LIKE 'Pedido [0-9]%' THEN 8
                         ELSE 0 END AS Pos) p
WHERE m.Tabla IN ('Pedidos', 'CabPedidoVta') AND p.Pos > 0;

INSERT #ConAlgoColgando SELECT k.Empresa, k.Número, 'Modificaciones' FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM #NumerosEnModificaciones x WHERE x.Numero = k.Número);

DELETE k FROM #Candidatas k WHERE EXISTS (SELECT 1 FROM #ConAlgoColgando x WHERE x.Empresa = k.Empresa AND x.Número = k.Número);

-------------------------------------------------------------------------------------------------
-- 3. SELECT previo: qué se queda y qué se va
-------------------------------------------------------------------------------------------------
SELECT @Encontradas AS CabecerasVaciasEncontradas,
       (SELECT COUNT(DISTINCT CAST(Empresa AS varchar(3)) + '|' + CAST(Número AS varchar(10))) FROM #ConAlgoColgando) AS SeQuedanPorTenerAlgoColgando,
       (SELECT COUNT(*) FROM #Candidatas) AS SeBorran;

SELECT Motivo, COUNT(*) AS Cabeceras, MIN(Número) AS PrimerPedido, MAX(Número) AS UltimoPedido
FROM #ConAlgoColgando GROUP BY Motivo ORDER BY Motivo;

SELECT Empresa, YEAR(Fecha) AS Año, COUNT(*) AS SeBorran
FROM #Candidatas GROUP BY Empresa, YEAR(Fecha) ORDER BY Empresa, Año;

SELECT TOP 200 Empresa, Número, Fecha, FechaModificacion, Usuario, Serie, Cliente, Contacto, PlazosPago
FROM #Candidatas ORDER BY Número DESC;

-------------------------------------------------------------------------------------------------
-- 4. Borrado en transacción. Se vuelve a comprobar TODO sobre la fila bloqueada, por si alguien le
--    ha metido una línea o la ha tocado entre el SELECT y el DELETE.
-------------------------------------------------------------------------------------------------
BEGIN TRANSACTION;

DELETE c
FROM dbo.CabPedidoVta c WITH (UPDLOCK, HOLDLOCK)
INNER JOIN #Candidatas k ON k.Empresa = c.Empresa AND k.Número = c.Número
WHERE (NULLIF(LTRIM(RTRIM(c.[Nº Cliente])), '') IS NULL
       OR NULLIF(LTRIM(RTRIM(c.Contacto)), '') IS NULL
       OR c.PlazosPago IS NULL)
  AND c.[Fecha Modificación] < @Limite
  AND NOT EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (UPDLOCK, HOLDLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.VendedoresPedidoGrupoProducto x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.EfectosPedidoVenta x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.Prepagos x WHERE x.Empresa = c.Empresa AND x.Pedido = c.Número);

DECLARE @Borradas int = @@ROWCOUNT;

SELECT @Borradas AS CabecerasBorradas,
       (SELECT COUNT(*) FROM #Candidatas) - @Borradas AS CandidatasQueYaNoCumplian;

IF @Borrar = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT CONCAT('NestoAPI#538: borradas ', @Borradas, ' cabeceras vacías (COMMIT).');
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT CONCAT('NestoAPI#538: se borrarían ', @Borradas, ' cabeceras vacías. Deshecho (ROLLBACK): pon @Borrar = 1 para borrarlas de verdad.');
END
