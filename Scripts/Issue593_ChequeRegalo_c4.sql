/*
    NestoAPI#593 (c4): canje del cheque regalo. Decisiones finales del tablero (09/10/26) en la campaña de octubre y
    columnas nuevas para las exclusiones del mínimo (se repetirá en febrero sin programar).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene ALTER). Idempotente: se puede
    lanzar dos veces. Hace falta Issue593_ChequeRegalo_Tablas.sql (c2) antes. Se puede lanzar ANTES o DESPUÉS de
    publicar la API: sin las columnas nuevas, la API reconoce igual la línea del cheque y, como la campaña sigue
    apagada, la rechaza.

    Qué NO hace: no activa la campaña (Activa sigue a 0). El día del lanzamiento:
      1) UPDATE dbo.ChequesRegaloCampanas SET Activa = 1 WHERE Codigo = 'CHEQUE50_OCT_2026';
      2) En el Web.config del REPO: ChequesRegalo:Generar = "true", y publicar la API.

    Decisiones (donde Manuel y Alberto difieren manda Manuel):
      - 50 € + IVA, un cheque por código de cliente, un solo uso, en OTRO pedido.
      - Sin espera: se puede usar desde que se emite la factura que lo genera (DiasEsperaTrasEntrega = 0).
      - Sin mínimo en la factura que genera (MinimoFacturaQueGenera = 0).
      - Canje hasta el 7 de noviembre de 2026 incluido (cuenta el día en que se guarda el cheque en el pedido).
      - El pedido que canjea tiene que SUPERAR 250 € de producto (250,00 no vale), en base imponible y después de
        descuentos. No suman: cuentas contables, ficticios (también el propio cheque), nombre que empieza por
        «PACK 26» y grupo PEL (peluquería). Pueden ir en el pedido y llevarse el descuento; solo no suman.
      - Listas separadas por punto y coma (p. ej. 'PACK 26;PACK 27').

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL
    RAISERROR('Falta la tabla dbo.ChequesRegaloCampanas: lanza antes Issue593_ChequeRegalo_Tablas.sql (c2).', 16, 1);
GO

------------------------------------------------------------------------------------------------
-- 1. Columnas nuevas: lo que no suma para el mínimo del pedido que canjea
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'PrefijosNombreExcluidosMinimo') IS NULL
    ALTER TABLE dbo.ChequesRegaloCampanas ADD PrefijosNombreExcluidosMinimo nvarchar(200) NULL;
GO
IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'GruposExcluidosMinimo') IS NULL
    ALTER TABLE dbo.ChequesRegaloCampanas ADD GruposExcluidosMinimo varchar(200) NULL;
GO

------------------------------------------------------------------------------------------------
-- 2. La campaña de octubre con las decisiones finales. Activa NO se toca.
------------------------------------------------------------------------------------------------
UPDATE dbo.ChequesRegaloCampanas
SET CanjeHasta = '20261107',
    MinimoFacturaQueGenera = 0,
    DiasEsperaTrasEntrega = 0,
    MinimoCanje = 250,
    PrefijosNombreExcluidosMinimo = N'PACK 26',
    GruposExcluidosMinimo = 'PEL',
    Descripcion = N'Cheque regalo de octubre 2026: primera factura del 12 al 29/10, canje hasta el 07/11 en otro pedido de más de 250 €',
    Usuario = SUSER_SNAME(),
    FechaModificacion = GETDATE()
WHERE Codigo = 'CHEQUE50_OCT_2026'
      AND (CanjeHasta <> '20261107' OR MinimoFacturaQueGenera <> 0 OR DiasEsperaTrasEntrega <> 0 OR MinimoCanje <> 250
           OR ISNULL(PrefijosNombreExcluidosMinimo, N'') <> N'PACK 26' OR ISNULL(GruposExcluidosMinimo, '') <> 'PEL');

SELECT @@ROWCOUNT AS CampanasActualizadasAhora;
GO

-- La API ya tiene SELECT en ChequesRegaloCampanas y SELECT, INSERT, UPDATE en ChequesRegalo (c2); el canje solo hace
-- UPDATE de ChequesRegalo (reservar y soltar), así que no hace falta ningún GRANT nuevo.

-- Comprobación
SELECT Codigo, RTRIM(Producto) Producto, ImporteBase, GeneraDesde, GeneraHasta, CanjeHasta, MinimoFacturaQueGenera, MinimoCanje,
       DiasEsperaTrasEntrega, PrefijosNombreExcluidosMinimo, GruposExcluidosMinimo, Activa
FROM dbo.ChequesRegaloCampanas;
GO
