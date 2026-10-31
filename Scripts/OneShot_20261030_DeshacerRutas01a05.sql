/*
    30/10/26 URGENTE (Carlos): las rutas 01-05 NO son la ruta del cliente. La gran mayoría de esos clientes sale por
    AGENCIA (FW); 01-05 solo dice QUÉ DÍA saldría por nuestra ruta si un pedido cumple los criterios de ruta interna
    (los de AT/16, sugerencia 575) y Alfredo lo elige. El script OneShot_20261030_RutasCodigosPostalesMadrid.sql
    (aplicado a las 11:20) puso 01-05 como ruta de los códigos postales Y de ~29.000 clientes, y los pedidos con
    ruta 01-05 NO entran en el picking (las rutas del picking son AT/OT/16/00/FW) ni calculan portes.
    Hoy: 16 pedidos con ruta 01-05 sin picking.

    Qué hace:
    1) Guarda el día de reparto de cada CP en una tabla nueva dbo.CodigosPostalesDiaReparto (no se pierde el Excel).
    2) Códigos postales de la empresa 1 con ruta 01-05 → FW.
    3) Clientes de la empresa 1 con ruta 01-05 → FW, por lotes de 2.000 (trigger cuadrático). Antes eran FW todos
       salvo 35 que eran 00: quedan en FW (agencia), que es lo correcto para ellos.
    4) Pedidos de la empresa 1 con ruta 01-05 creados HOY (Fecha >= 30/10/26) → FW, y los lista para revisar los PORTES.
    5) Rutas 01-05 → Activa = 0, para que nadie las elija en Nesto.
    NO deshace: los ~1.300 clientes que pasaron de FW a 00 (00 sí entra en picking y portes) ni los códigos postales
    que pasaron de FW a 00. Si se quieren exactos, hay que sacarlos de la copia de seguridad de anoche.

    Lanzar como sa en NV. @Aplicar = 0 solo enseña.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @Aplicar bit = 0;

SELECT RTRIM(Ruta) Ruta, COUNT(*) CodigosPostales FROM dbo.[CódigosPostales]
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05') GROUP BY RTRIM(Ruta) ORDER BY 1;
SELECT RTRIM(Ruta) Ruta, COUNT(*) Clientes FROM dbo.Clientes
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05') GROUP BY RTRIM(Ruta) ORDER BY 1;
SELECT Número Pedido, RTRIM([Nº Cliente]) Cliente, RTRIM(Ruta) Ruta, Fecha FROM dbo.CabPedidoVta
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05') AND Fecha >= '20261030' ORDER BY Número;

IF @Aplicar = 0
BEGIN
    PRINT 'Solo lectura (@Aplicar = 0). Nada modificado.';
    RETURN;
END

-- 1) El día de reparto de cada CP, en su tabla
IF OBJECT_ID('dbo.CodigosPostalesDiaReparto') IS NULL
BEGIN
    CREATE TABLE dbo.CodigosPostalesDiaReparto (
        Empresa char(3) NOT NULL,
        CodigoPostal varchar(10) NOT NULL,
        Ruta char(3) NOT NULL,              -- 01 lunes … 05 viernes
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CodigosPostalesDiaReparto_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_CodigosPostalesDiaReparto PRIMARY KEY CLUSTERED (Empresa, CodigoPostal),
        CONSTRAINT CK_CodigosPostalesDiaReparto_Ruta CHECK (Ruta IN ('01','02','03','04','05'))
    );
    GRANT SELECT ON dbo.CodigosPostalesDiaReparto TO [NUEVAVISION\RDS2016$];
END
INSERT INTO dbo.CodigosPostalesDiaReparto (Empresa, CodigoPostal, Ruta)
SELECT cp.Empresa, RTRIM(cp.Número), RTRIM(cp.Ruta)
FROM dbo.[CódigosPostales] cp
WHERE cp.Empresa = '1' AND RTRIM(cp.Ruta) IN ('01','02','03','04','05')
  AND NOT EXISTS (SELECT 1 FROM dbo.CodigosPostalesDiaReparto d WHERE d.Empresa = cp.Empresa AND d.CodigoPostal = RTRIM(cp.Número));
PRINT CONCAT('Días de reparto guardados: ', @@ROWCOUNT);

-- 2) Códigos postales → FW
UPDATE dbo.[CódigosPostales] SET Ruta = 'FW'
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05');
PRINT CONCAT('Códigos postales a FW: ', @@ROWCOUNT);

-- 3) Clientes → FW, por lotes
DECLARE @Lote int = 1, @Total int = 0;
WHILE @Lote > 0
BEGIN
    UPDATE TOP (2000) dbo.Clientes SET Ruta = 'FW'
    WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05');
    SET @Lote = @@ROWCOUNT;
    SET @Total += @Lote;
    PRINT CONCAT('  lote de clientes: ', @Lote, ' (total ', @Total, ')');
END
PRINT CONCAT('Clientes a FW: ', @Total);

-- 4) Pedidos → FW (revisar sus portes: con 01-05 no se calcularon)
SELECT Número Pedido, RTRIM([Nº Cliente]) Cliente, RTRIM(Ruta) RutaQueTenia FROM dbo.CabPedidoVta
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05') AND Fecha >= '20261030' ORDER BY Número;
UPDATE dbo.CabPedidoVta SET Ruta = 'FW' -- SOLO los de hoy: hay ~104.000 pedidos antiguos con 01-05 (rutas viejas) que no se tocan
WHERE Empresa = '1' AND RTRIM(Ruta) IN ('01','02','03','04','05') AND Fecha >= '20261030';
PRINT CONCAT('Pedidos a FW: ', @@ROWCOUNT);

-- 5) Que nadie las elija
UPDATE dbo.Rutas SET Activa = 0 WHERE Empresa = '1' AND RTRIM(Número) IN ('01','02','03','04','05') AND Activa = 1;
PRINT CONCAT('Rutas 01-05 desactivadas: ', @@ROWCOUNT);
